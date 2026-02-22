using System;
using System.Net;

namespace UserTables.Client.Transport;

public sealed class UserTablesApiException(string message, HttpStatusCode statusCode, string? responseBody) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ResponseBody { get; } = responseBody;
}