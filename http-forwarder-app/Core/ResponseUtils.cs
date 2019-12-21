using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace http_forwarder_app
{
    public static class ResponseUtils
    {
        private const int StreamCopyBufferSize = 81920;

        public static Task CopyHttpResponse(this HttpContext httpContext, HttpResponseMessage callResponseMessage)
        {
            return CopyHttpResponse(httpContext.Response, callResponseMessage, httpContext.RequestAborted);
        }

        // https://stackoverflow.com/questions/44729592/how-to-forward-http-response-to-client/44737128#44737128
        // https://github.com/aspnet/Proxy/blob/master/src/Microsoft.AspNetCore.Proxy/ProxyAdvancedExtensions.cs#L170
        public static async Task CopyHttpResponse(HttpResponse response, HttpResponseMessage callResponseMessage, CancellationToken cancellationToken)
        {
            if (callResponseMessage == null)
            {
                throw new ArgumentNullException(nameof(callResponseMessage));
            }

            response.StatusCode = (int)callResponseMessage.StatusCode;
            foreach (var header in callResponseMessage.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in callResponseMessage.Content.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }

            // SendAsync removes chunking from the response. This removes the header so it doesn't expect a chunked response.
            response.Headers.Remove("transfer-encoding");
            response.Headers.Remove("Set-Cookie");

            using (var responseStream = await callResponseMessage.Content.ReadAsStreamAsync())
            {
                await responseStream.CopyToAsync(response.Body, StreamCopyBufferSize, cancellationToken);
            }
        }
    }
}