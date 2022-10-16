using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace http_forwarder_app.Core
{
    public class RestClient: IRestClient
    {
        public RestClient(IHttpClientFactory httpClientFactory)
        {
            HttpClientFactory = httpClientFactory;
        }

        private IHttpClientFactory HttpClientFactory { get; }

        public async Task<HttpResponseMessage> MakeGetCall(string eventName, string targetUrl, IDictionary<string, string> headers)
        {
            var client = HttpClientFactory.CreateClient(eventName);
            AddHeaders(client, headers);
            var resp = await client.GetAsync(targetUrl);
            return resp;
        }

        public async Task<HttpResponseMessage> MakePostCall(string eventName, string targetUrl, string content, IDictionary<string, string> headers)
        {
            var client = HttpClientFactory.CreateClient(eventName);
            AddHeaders(client, headers);
            var resp = await client.PostAsync(targetUrl, new StringContent(content ?? string.Empty));
            return resp;
        }

        public async Task<HttpResponseMessage> MakeDeleteCall(string eventName, string targetUrl, IDictionary<string, string> headers)
        {
            var client = HttpClientFactory.CreateClient(eventName);
            AddHeaders(client, headers);
            var resp = await client.DeleteAsync(targetUrl);
            return resp;
        }

        public async Task<HttpResponseMessage> MakePutCall(string eventName, string targetUrl, string content, IDictionary<string, string> headers)
        {
            var client = HttpClientFactory.CreateClient(eventName);
            AddHeaders(client, headers);
            var resp = await client.PutAsync(targetUrl, new StringContent(content ?? string.Empty));
            return resp;
        }

        private void AddHeaders(HttpClient httpClient, IDictionary<string, string> headers)
        {
            if (headers != null && headers.Count > 0)
            {
                foreach(var h in headers)
                {
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
                }
            }
        }
    }
}