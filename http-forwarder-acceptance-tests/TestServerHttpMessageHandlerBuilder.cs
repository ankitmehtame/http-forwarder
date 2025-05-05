using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Http;

namespace http_forwarder_acceptance_tests;

public class TestServerHttpMessageHandlerBuilder(TestServer testServer) : HttpMessageHandlerBuilder
{
    private readonly TestServer _testServer = testServer;

    public override IList<DelegatingHandler> AdditionalHandlers => [];

    public override string? Name { get => nameof(TestServerHttpMessageHandlerBuilder); set { } }
    public override HttpMessageHandler PrimaryHandler { get => _testServer.CreateHandler(); set { } }

    public override HttpMessageHandler Build()
    {
        return _testServer.CreateHandler();
    }
}
