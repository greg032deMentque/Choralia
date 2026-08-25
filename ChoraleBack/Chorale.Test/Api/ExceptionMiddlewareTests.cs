using System.Net;
using System.Text.Json;
using ChoraleBackEnd.Api.Middleware;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Test.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Api;

/// <summary>
/// Traduction exception → reponse HTTP. C'est le contrat back↔front des cas d'erreur : le
/// front branche sur le code (401 → reconnexion, 403 → ecran refus, 404 → introuvable,
/// 409 → conflit) et affiche <c>Message</c> tel quel.
/// </summary>
/// <remarks>
/// Teste a travers <c>InvokeAsync</c> plutot qu'en rendant les mappings internes visibles :
/// le comportement observable est la reponse ecrite, pas la valeur intermediaire. Une
/// exception non mappee ici devient un 500 sur un defaut d'entree — c'est exactement le
/// defaut qu'a corrige l'ajout de <c>FormatException</c>.
/// </remarks>
[TestFixture]
public sealed class ExceptionMiddlewareTests
{
    private FakeLogService _logService = null!;

    [SetUp]
    public void SetUp() => _logService = new FakeLogService();

    [TestCase(typeof(UnauthorizedAccessException), HttpStatusCode.Unauthorized)]
    [TestCase(typeof(KeyNotFoundException), HttpStatusCode.NotFound)]
    [TestCase(typeof(ArgumentException), HttpStatusCode.BadRequest)]
    [TestCase(typeof(FormatException), HttpStatusCode.BadRequest)]
    [TestCase(typeof(TimeoutException), HttpStatusCode.GatewayTimeout)]
    [TestCase(typeof(InvalidOperationException), HttpStatusCode.InternalServerError)]
    public async Task InvokeAsync_FrameworkException_MapsToItsHttpStatus(Type exceptionType, HttpStatusCode expected)
    {
        var context = await RunAsync(() => throw (Exception)Activator.CreateInstance(exceptionType)!);

        Assert.That(context.Response.StatusCode, Is.EqualTo((int)expected));
    }

    [Test]
    public async Task InvokeAsync_CustomException_KeepsItsStatusAndFrontMessage()
    {
        var context = await RunAsync(
            () => throw new CustomException(HttpStatusCode.Conflict, "Cette liste est déjà archivée."));

        var payload = ReadPayload(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.Conflict));
            Assert.That(payload.GetProperty("Message").GetString(), Is.EqualTo("Cette liste est déjà archivée."));
            Assert.That(payload.GetProperty("StatusCode").GetInt32(), Is.EqualTo((int)HttpStatusCode.Conflict));
        });
    }

    [Test]
    public async Task InvokeAsync_UnmappedException_NeverLeaksTheInternalMessageToTheClient()
    {
        const string internalDetail = "Column 'Secret' does not exist on table Users";

        var context = await RunAsync(() => throw new InvalidOperationException(internalDetail));

        var payload = ReadPayload(context);
        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("Message").GetString(), Is.EqualTo("Une erreur est survenue."));
            Assert.That(payload.GetProperty("Message").GetString(), Does.Not.Contain("Secret"));
            Assert.That(_logService.Errors.Single(), Does.Contain(internalDetail),
                "Le detail interne doit rester disponible cote journal, il ne disparait pas.");
        });
    }

    [Test]
    public async Task InvokeAsync_Response_IsJsonAndCarriesATraceId()
    {
        var context = await RunAsync(() => throw new ArgumentException("param invalide"));

        var payload = ReadPayload(context);
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.ContentType, Is.EqualTo("application/json"));
            Assert.That(payload.GetProperty("TraceId").GetString(), Is.Not.Empty);
        });
    }

    [Test]
    public async Task InvokeAsync_NominalRejection_IsLoggedAsWarningNotAsError()
    {
        await RunAsync(() => throw new KeyNotFoundException("SongList introuvable"));

        Assert.Multiple(() =>
        {
            Assert.That(_logService.Warnings, Is.Not.Empty);
            Assert.That(_logService.Errors, Is.Empty,
                "Un 404 metier est un rejet nominal : le journaliser en erreur noie les vraies pannes.");
        });
    }

    [Test]
    public async Task InvokeAsync_UnexpectedError_IsLoggedAsError()
    {
        await RunAsync(() => throw new InvalidOperationException("panne"));

        Assert.Multiple(() =>
        {
            Assert.That(_logService.Errors, Is.Not.Empty);
            Assert.That(_logService.Warnings, Is.Empty);
        });
    }

    [Test]
    public async Task InvokeAsync_ResponseAlreadyStarted_DoesNotAttemptToRewriteIt()
    {
        var context = BuildContext();
        await context.Response.WriteAsync("deja parti");

        var middleware = new ExceptionMiddleware(
            _ => throw new InvalidOperationException("trop tard"),
            BuildScopeFactory());

        Assert.DoesNotThrowAsync(() => middleware.InvokeAsync(context));
    }

    /// <summary>
    /// Le chemin d'appel journalise ne doit contenir que des frames du produit, de la plus
    /// externe a la plus interne.
    /// </summary>
    /// <remarks>
    /// C'est ce test qui a revele que le prefixe de filtrage valait "Choir" alors qu'aucun
    /// namespace du depot ne commence par la : le filtre ne retenait rien et la branche de
    /// repli ne journalisait qu'une seule frame. Sans lui, le defaut restait invisible — un
    /// chemin d'appel tronque ne casse aucun test fonctionnel, il degrade juste le diagnostic
    /// de toutes les 500.
    /// </remarks>
    [Test]
    public async Task InvokeAsync_UnexpectedError_LogsTheApplicationCallPathNotOnlyTheTopFrame()
    {
        await RunAsync(ThrowFromNestedApplicationFrame);

        var payload = ReadLoggedPayload();
        var callPath = payload.GetProperty("CallPath").EnumerateArray().ToList();
        var functions = callPath.Select(f => f.GetProperty("Function").GetString() ?? "").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(callPath, Has.Count.GreaterThan(1),
                "Un seul element signifie que le filtre de namespace n'a rien retenu et que la branche de repli a joue.");
            Assert.That(functions, Has.All.Contains("ChoraleBackEnd"));
            Assert.That(functions.Last(), Does.Contain(nameof(ThrowDeepest)),
                "La derniere frame est le point de levee — c'est elle que reprend `Location`.");
        });
    }

    // ---------- Montage ----------

    private static void ThrowFromNestedApplicationFrame() => ThrowDeepest();

    private static void ThrowDeepest() => throw new InvalidOperationException("panne au fond de la pile");

    private static DefaultHttpContext BuildContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/song-lists/Update";
        return context;
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogService>(_logService);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private async Task<DefaultHttpContext> RunAsync(Action throwing)
    {
        var context = BuildContext();
        var middleware = new ExceptionMiddleware(
            _ =>
            {
                throwing();
                return Task.CompletedTask;
            },
            BuildScopeFactory());

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        return context;
    }

    private static JsonElement ReadPayload(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
    }

    /// <summary>
    /// FakeLogService concatene le payload JSON et le message d'exception avec " :: " :
    /// on ne reprend que la partie JSON.
    /// </summary>
    private JsonElement ReadLoggedPayload()
    {
        var entry = _logService.Errors.Single();
        var json = entry[..entry.LastIndexOf(" :: ", StringComparison.Ordinal)];
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
