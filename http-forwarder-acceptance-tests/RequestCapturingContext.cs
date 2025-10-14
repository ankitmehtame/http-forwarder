using System.Collections.Concurrent;

namespace http_forwarder_acceptance_tests
{
    public record class RequestCapturingContext
    {
        public ConcurrentQueue<RequestCapturingData> Requests { get; } = new();
    }

    public record class RequestCapturingData(string RequestUrl, string RequestBody);
}
