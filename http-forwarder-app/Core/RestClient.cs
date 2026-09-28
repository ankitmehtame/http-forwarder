using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using http_forwarder_app.Utils;

namespace http_forwarder_app.Core;

public class RestClient(IHttpClientFactory httpClientFactory, ILogger<RestClient> logger, IConfiguration configuration) : IRestClient
{
    public Task<HttpResponseMessage> MakeGetCall(string eventName, string targetUrl, IDictionary<string, string> headers, bool ignoreSslError) =>
        Send(eventName, targetUrl, HttpMethod.Get, null, headers, ignoreSslError);

    public Task<HttpResponseMessage> MakePostCall(string eventName, string targetUrl, string? content, IDictionary<string, string> headers, bool ignoreSslError) =>
        Send(eventName, targetUrl, HttpMethod.Post, content, headers, ignoreSslError);

    public Task<HttpResponseMessage> MakePutCall(string eventName, string targetUrl, string? content, IDictionary<string, string> headers, bool ignoreSslError) =>
        Send(eventName, targetUrl, HttpMethod.Put, content, headers, ignoreSslError);

    public Task<HttpResponseMessage> MakeDeleteCall(string eventName, string targetUrl, IDictionary<string, string> headers, bool ignoreSslError) =>
        Send(eventName, targetUrl, HttpMethod.Delete, null, headers, ignoreSslError);

    private async Task<HttpResponseMessage> Send(string eventName, string targetUrl, HttpMethod method, string? content, IDictionary<string, string> headers, bool ignoreSslError)
    {
        var client = httpClientFactory.CreateClient(ignoreSslError ? Constants.HTTP_CLIENT_IGNORE_SSL_ERROR : eventName);
        using var request = new HttpRequestMessage(method, targetUrl);
        if (method == HttpMethod.Post || method == HttpMethod.Put)
        {
            var contentType = headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? "application/json";
            request.Content = new StringContent(content ?? string.Empty, contentType.ToMediaTypeHeaderValue());
        }
        foreach (var header in headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && request.Content is not null) continue;
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        logger.LogDebug("Calling {method} {url} with headers {headers}", method, targetUrl, configuration.CreatePrettyDictionary(headers));
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }
}
