using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using http_forwarder_app.Utils;

namespace http_forwarder_app.Core
{
    public class RestClient(IHttpClientFactory httpClientFactory, ILogger<RestClient> logger, IConfiguration configuration) : IRestClient
    {
        public async Task<HttpResponseMessage> MakeGetCall(string eventName, string targetUrl, IDictionary<string, string> headers, bool ignoreSslError)
        {
            var client = httpClientFactory.CreateClient(ignoreSslError ? Constants.HTTP_CLIENT_IGNORE_SSL_ERROR : eventName);
            AddHeaders(client, headers);
            logger.LogDebug("Calling GET {url} with headers {headers}", targetUrl, configuration.CreatePrettyDictionary(headers));
            var resp = await client.GetAsync(targetUrl);
            return resp;
        }

        public async Task<HttpResponseMessage> MakePostCall(string eventName, string targetUrl, string? content, IDictionary<string, string> headers, bool ignoreSslError)
        {
            var client = httpClientFactory.CreateClient(ignoreSslError ? Constants.HTTP_CLIENT_IGNORE_SSL_ERROR : eventName);
            AddHeaders(client, headers);
            var contentType = SafeGetContentType(headers) ?? "application/json";
            var mediaTypeHeaderValue = contentType.ToMediaTypeHeaderValue();
            var finalContent = new StringContent(content ?? string.Empty, mediaTypeHeaderValue);
            logger.LogDebug("Calling POST {url} with body {body} and headers {headers}", targetUrl, content ?? string.Empty, configuration.CreatePrettyDictionary(headers));
            var resp = await client.PostAsync(targetUrl, finalContent);
            return resp;
        }

        public async Task<HttpResponseMessage> MakeDeleteCall(string eventName, string targetUrl, IDictionary<string, string> headers, bool ignoreSslError)
        {
            var client = httpClientFactory.CreateClient(ignoreSslError ? Constants.HTTP_CLIENT_IGNORE_SSL_ERROR : eventName);
            AddHeaders(client, headers);
            logger.LogDebug("Calling DELETE {url} with headers {headers}", targetUrl, configuration.CreatePrettyDictionary(headers));
            var resp = await client.DeleteAsync(targetUrl);
            return resp;
        }

        public async Task<HttpResponseMessage> MakePutCall(string eventName, string targetUrl, string? content, IDictionary<string, string> headers, bool ignoreSslError)
        {
            var client = httpClientFactory.CreateClient(ignoreSslError ? Constants.HTTP_CLIENT_IGNORE_SSL_ERROR : eventName);
            AddHeaders(client, headers);
            var contentType = SafeGetContentType(headers) ?? "application/json";
            var mediaTypeHeaderValue = contentType.ToMediaTypeHeaderValue();
            var finalContent = new StringContent(content ?? string.Empty, mediaTypeHeaderValue);
            logger.LogDebug("Calling PUT {url} with body {body} and headers {headers}", targetUrl, content ?? string.Empty, configuration.CreatePrettyDictionary(headers));
            var resp = await client.PutAsync(targetUrl, finalContent);
            return resp;
        }

        private static void AddHeaders(HttpClient httpClient, IDictionary<string, string> headers)
        {
            if (headers.Count == 0) return;
            foreach (var h in headers)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
            }
        }

        private static string? SafeGetContentType(IDictionary<string, string> headers)
        {
            if (headers.TryGetValue("Content-Type", out var contentType) == true)
            {
                return contentType;
            }
            return null;
        }
    }
}
