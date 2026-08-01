using System.Net;

namespace ITBees.Ksef.Models;

/// <summary>Thrown when the KSeF API returns a non-success HTTP status or a business-level error status.</summary>
public class KsefApiException : Exception
{
    public HttpStatusCode? HttpStatusCode { get; }
    public int? KsefStatusCode { get; }
    public string? ResponseBody { get; }

    public KsefApiException(string message, HttpStatusCode? httpStatusCode = null, string? responseBody = null,
        int? ksefStatusCode = null)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        KsefStatusCode = ksefStatusCode;
        ResponseBody = responseBody;
    }
}
