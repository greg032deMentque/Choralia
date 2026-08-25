using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Middleware;

public sealed class ApiErrorFrame
{
    public string Function { get; set; } = "";
    public string? File { get; set; }
    public int? Line { get; set; }
}

public sealed class ApiErrorResponse
{
    public int StatusCode { get; set; }
    public string TraceId { get; set; } = "";
    public string RequestMethod { get; set; } = "";
    public string RequestPath { get; set; } = "";
    public string? Endpoint { get; set; }
    public string ExceptionType { get; set; } = "";
    public string Message { get; set; } = "";
    public ApiErrorFrame? Location { get; set; }
    public List<ApiErrorFrame> CallPath { get; set; } = new();
}

public sealed class ApiClientErrorResponse
{
    public int StatusCode { get; set; }
    public string TraceId { get; set; } = "";
    public string Message { get; set; } = "";
    public List<string>? Errors { get; set; }
}

public sealed class ExceptionMiddleware
{
    // Prefixe des namespaces du produit, utilise par BuildCallPath pour ne garder que les
    // frames applicatives. Il valait "Choir", qui ne prefixe aucun namespace du depot (ils
    // commencent tous par "ChoraleBackEnd") : le filtre ne matchait jamais et tout log de 500
    // retombait sur la branche de repli, ne portant que la frame de tete au lieu du chemin
    // d'appel. A tenir aligne si les namespaces sont un jour renommes.
    private const string AppNamespacePrefix = "ChoraleBackEnd";

    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;

    public ExceptionMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        using var scope = _scopeFactory.CreateScope();
        var logSvc = scope.ServiceProvider.GetRequiredService<ILogService>();

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        if (IsClientCancellation(context, ex))
        {
            logSvc.LogInformation(
                "Request cancelled by client. TraceId: {TraceId}, Method: {Method}, Path: {Path}",
                traceId, context.Request.Method, context.Request.Path.Value ?? string.Empty);
            return;
        }

        var (status, clientMessage, clientErrors) = MapClientOutcome(ex);

        if (IsNominalRejection(ex, status))
        {
            logSvc.LogWarning(
                "Nominal rejection. TraceId: {TraceId}, Status: {StatusCode}, Method: {Method}, Path: {Path}, Message: {Message}",
                traceId, (int)status, context.Request.Method, context.Request.Path.Value ?? string.Empty, ex.Message);
        }
        else
        {
            LogUnexpectedError(logSvc, ex, status, traceId, context);
        }

        if (context.Response.HasStarted) return;

        await ApiClientErrorResponseWriter.WriteAsync(context, status, traceId, clientMessage, clientErrors);
    }

    private static bool IsClientCancellation(HttpContext context, Exception ex)
        => context.RequestAborted.IsCancellationRequested && ex is OperationCanceledException;

    private static bool IsNominalRejection(Exception ex, HttpStatusCode status)
        => ex is KeyNotFoundException || ex is CustomException && (int)status < 500;

    private static void LogUnexpectedError(ILogService logSvc, Exception ex, HttpStatusCode status, string traceId, HttpContext context)
    {
        var callPath = BuildCallPath(ex);
        var location = callPath.Count > 0 ? callPath[^1] : null;

        var logPayload = new ApiErrorResponse
        {
            StatusCode = (int)status,
            TraceId = traceId,
            RequestMethod = context.Request.Method,
            RequestPath = context.Request.Path.Value ?? "",
            Endpoint = context.GetEndpoint()?.DisplayName,
            ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
            Message = ex.Message,
            Location = location,
            CallPath = callPath
        };

        logSvc.LogError(JsonSerializer.Serialize(logPayload,
            new JsonSerializerOptions { PropertyNamingPolicy = null, WriteIndented = true }), ex);
    }

    private static (HttpStatusCode Status, string Message, List<string>? Errors) MapClientOutcome(Exception ex)
    {
        if (ex is CustomException ce)
        {
            var message = !string.IsNullOrWhiteSpace(ce.FrontMessage) ? ce.FrontMessage : "Une erreur est survenue.";
            var errors = ce.ErrorMessages is { Count: > 0 } ? ce.ErrorMessages : null;
            return (ce.StatusCode, message, errors);
        }

        var status = MapStatusCode(ex);
        var msg = status switch
        {
            HttpStatusCode.Unauthorized => "Non autorisé.",
            HttpStatusCode.NotFound => "Ressource introuvable.",
            HttpStatusCode.BadRequest => "Requête invalide.",
            HttpStatusCode.GatewayTimeout => "Le service met trop de temps à répondre.",
            _ => "Une erreur est survenue."
        };
        return (status, msg, null);
    }

    private static HttpStatusCode MapStatusCode(Exception ex)
        => ex switch
        {
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            KeyNotFoundException => HttpStatusCode.NotFound,
            ArgumentException => HttpStatusCode.BadRequest,
            // EmailService construit un MailAddress a partir d'une adresse fournie en amont :
            // malformee, elle levait une FormatException non mappee, donc un 500 alors que le
            // defaut vient de l'entree.
            FormatException => HttpStatusCode.BadRequest,
            TimeoutException => HttpStatusCode.GatewayTimeout,
            _ => HttpStatusCode.InternalServerError
        };

    private static List<ApiErrorFrame> BuildCallPath(Exception ex)
    {
        var frames = new StackTrace(ex, true).GetFrames();
        if (frames is null || frames.Length == 0) return [];

        var selected = frames
            .Select(f =>
            {
                var m = f.GetMethod();
                var t = m?.DeclaringType;
                var ns = t?.Namespace ?? "";
                if (!ns.StartsWith(AppNamespacePrefix, StringComparison.Ordinal)) return null;

                return new ApiErrorFrame
                {
                    Function = t is null || m is null ? "Unknown" : $"{t.FullName}.{m.Name}",
                    File = string.IsNullOrWhiteSpace(f.GetFileName()) ? null : Path.GetFileName(f.GetFileName()),
                    Line = f.GetFileLineNumber() > 0 ? f.GetFileLineNumber() : null
                };
            })
            .Where(x => x is not null)
            .Cast<ApiErrorFrame>()
            .ToList();

        if (selected.Count == 0)
        {
            var top = frames[0];
            var m = top.GetMethod();
            var t = m?.DeclaringType;
            return [new ApiErrorFrame
            {
                Function = t is null || m is null ? "Unknown" : $"{t.FullName}.{m.Name}",
                File = string.IsNullOrWhiteSpace(top.GetFileName()) ? null : Path.GetFileName(top.GetFileName()),
                Line = top.GetFileLineNumber() > 0 ? top.GetFileLineNumber() : null
            }];
        }

        selected.Reverse();
        return selected;
    }
}

public static class ApiClientErrorResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNamingPolicy = null, WriteIndented = true };

    public static Task WriteAsync(
        HttpContext context, HttpStatusCode statusCode, string traceId, string message, List<string>? errors = null)
    {
        var payload = new ApiClientErrorResponse
        {
            StatusCode = (int)statusCode,
            TraceId = traceId,
            Message = message,
            Errors = errors
        };

        context.Response.Clear();
        context.Response.StatusCode = payload.StatusCode;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }
}

public sealed class InvalidModelStateResult : IActionResult
{
    private const string GenericInvalidModelMessage = "Les informations envoyées sont invalides.";

    public Task ExecuteResultAsync(ActionContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        return ApiClientErrorResponseWriter.WriteAsync(
            context.HttpContext, HttpStatusCode.BadRequest, traceId, GenericInvalidModelMessage);
    }
}
