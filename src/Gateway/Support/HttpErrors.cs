using System.Net;

namespace Gateway.Support;

public static class HttpErrors
{
    public static async Task<string> ReadBodySafeAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static IResult AsBadGateway(string message, HttpStatusCode upstreamCode)
        => Results.Problem($"{message}. Upstream={(int)upstreamCode}", statusCode: 502);

    public static IResult AsServiceUnavailable(string message)
        => Results.Problem(message, statusCode: 503);
}