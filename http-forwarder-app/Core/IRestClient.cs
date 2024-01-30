using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace http_forwarder_app.Core
{
    public interface IRestClient
    {
        Task<HttpResponseMessage> MakeGetCall(string eventName, string targetUrl, IDictionary<string, string> headers, bool ignoreSslError);
        Task<HttpResponseMessage> MakePostCall(string eventName, string targetUrl, string? content, IDictionary<string, string> headers, bool ignoreSslError);
        Task<HttpResponseMessage> MakePutCall(string eventName, string targetUrl, string? content, IDictionary<string, string> headers, bool ignoreSslError);
        Task<HttpResponseMessage> MakeDeleteCall(string eventName, string targetUrl, IDictionary<string, string> headers, bool ignoreSslError);
    }
}