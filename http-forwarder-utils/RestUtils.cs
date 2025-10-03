using System.Net;

namespace http_forwarder_app.Core;

public static class RestUtils
{
    public static bool IsServerError(this HttpResponseMessage response)
    {
        return !response.IsSuccessStatusCode && response.StatusCode.IsServerError();
    }

    public static bool IsServerError(this HttpStatusCode statusCode) =>
            (int)statusCode >= 500 && (int)statusCode <= 599;
}