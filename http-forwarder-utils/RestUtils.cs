using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace http_forwarder_app.Utils;

public static class RestUtils
{
    public static bool IsServerError(this HttpResponseMessage response)
    {
        return !response.IsSuccessStatusCode && response.StatusCode.IsServerError();
    }

    public static bool IsServerError(this HttpStatusCode statusCode) =>
            (int)statusCode >= 500 && (int)statusCode <= 599;

    private static readonly HashSet<string> IgnoredHeaders = ["Content-Length", "Host"];

    public static ImmutableSortedDictionary<string, string> GetHeaders(this IHeaderDictionary? requestHeaders)
    {
        Dictionary<string, string> requiredHeaders = [];
        foreach (var requiredHeader in requestHeaders ?? Enumerable.Empty<KeyValuePair<string, StringValues>>())
        {
            if (IgnoredHeaders.Contains(requiredHeader.Key)) continue;
            requiredHeaders.Add(requiredHeader.Key, string.Join("\n", requiredHeader.Value.ToArray()));
        }
        return requiredHeaders.ToImmutableSortedDictionary();
    }

    public static MediaTypeHeaderValue ToMediaTypeHeaderValue(this string? contentTypeHeaderValue)
    {
        if (!string.IsNullOrWhiteSpace(contentTypeHeaderValue) && MediaTypeHeaderValue.TryParse(contentTypeHeaderValue, out var mediaTypeHeaderValue))
        {
            return mediaTypeHeaderValue;
        }

        return new MediaTypeHeaderValue("application/json", Encoding.UTF8.HeaderName);
    }
}
