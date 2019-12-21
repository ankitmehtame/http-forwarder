using System.Net.Http;
using System.Threading.Tasks;

namespace http_forwarder_app.Core
{
    public interface IRestClient
    {
        Task<HttpResponseMessage> MakeGetCall(string eventName, string targetUrl);
        Task<HttpResponseMessage> MakePostCall(string eventName, string targetUrl, string content);
        Task<HttpResponseMessage> MakePutCall(string eventName, string targetUrl, string content);
        Task<HttpResponseMessage> MakeDeleteCall(string eventName, string targetUrl);
    }
}