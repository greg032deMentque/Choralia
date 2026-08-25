using System.Net;

namespace ChoraleBackEnd.Common.Exceptions;

public sealed class CustomException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string FrontMessage { get; set; }
    public List<string> ErrorMessages { get; }

    public CustomException(HttpStatusCode statusCode, string frontMessage, List<string>? errors = null)
        : base(frontMessage)
    {
        StatusCode = statusCode;
        FrontMessage = frontMessage;
        ErrorMessages = errors ?? [];
    }

    public CustomException(string internalMessage, string frontMessage,
        HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(internalMessage)
    {
        StatusCode = statusCode;
        FrontMessage = frontMessage;
        ErrorMessages = [];
    }
}
