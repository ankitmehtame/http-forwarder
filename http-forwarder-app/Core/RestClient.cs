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

        public async Task<HttpResponseMessage> MakeGetCall(string eventName, string targetUrl)
        {
            var client = HttpClientFactory.CreateClient(eventName);
            var resp = await client.GetAsync(targetUrl);
            return resp;
        }

        public async Task<HttpResponseMessage> MakePostCall(string eventName, string targetUrl, string content)
        {
            var client = HttpClientFactory.CreateClient(eventName);
            var resp = await client.PostAsync(targetUrl, new StringContent(content ?? string.Empty));
            return resp;
        }

        public async Task<HttpResponseMessage> MakeDeleteCall(string eventName, string targetUrl)
        {
            var client = HttpClientFactory.CreateClient(eventName);
            var resp = await client.DeleteAsync(targetUrl);
            return resp;
        }

        public async Task<HttpResponseMessage> MakePutCall(string eventName, string targetUrl, string content)
        {
            var client = HttpClientFactory.CreateClient(eventName);
            var resp = await client.PutAsync(targetUrl, new StringContent(content ?? string.Empty));
            return resp;
        }
    }
}