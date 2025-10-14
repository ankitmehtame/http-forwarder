
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace http_forwarder_acceptance_tests
{
    public class RequestCapturingHandler : DelegatingHandler
    {
        private readonly RequestCapturingContext _context;

        public RequestCapturingHandler(RequestCapturingContext context)
        {
            _context = context;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                _context.Requests.Enqueue(new(request.RequestUri!.ToString(), body));
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
