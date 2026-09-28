using System.Net;
using http_forwarder_app.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;

namespace http_forwarder_unit_tests;

public class RestClientTests
{
    [Fact]
    public async Task RequestsOnSharedClientDoNotLeakHeaders()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        var rest = new RestClient(factory.Object, Mock.Of<ILogger<RestClient>>(), new ConfigurationBuilder().Build());

        using var first = await rest.MakeGetCall("test", "http://example.test/", new Dictionary<string, string> { ["X-Private"] = "first" }, false);
        using var second = await rest.MakeGetCall("test", "http://example.test/", new Dictionary<string, string>(), false);

        handler.RequestHeaders.ShouldBe(new string?[] { "first", null });
    }

    [Fact]
    public async Task MakePostCall_WithoutContentType_ShouldUseDefaultJsonContentType()
    {
        var handler = new CapturingHandler();
        var restClient = CreateRestClient(handler);

        await restClient.MakePostCall("test-event", "http://example.test/api", "{}", new Dictionary<string, string>(), false);

        handler.RequestContentType.ShouldBe("application/json");
    }

    [Fact]
    public async Task MakePostCall_WithInvalidContentType_ShouldUseDefaultJsonContentType()
    {
        var handler = new CapturingHandler();
        var restClient = CreateRestClient(handler);

        await restClient.MakePostCall("test-event", "http://example.test/api", "{}", new Dictionary<string, string>
        {
            ["Content-Type"] = "not a valid content type"
        }, false);

        handler.RequestContentType.ShouldBe("application/json");
    }

    private static RestClient CreateRestClient(HttpMessageHandler handler)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));

        var logger = new Mock<ILogger<RestClient>>();
        var configuration = new ConfigurationBuilder().Build();

        return new RestClient(httpClientFactory.Object, logger.Object, configuration);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestContentType { get; private set; }
        public List<string?> RequestHeaders { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestContentType = request.Content?.Headers.ContentType?.MediaType;
            RequestHeaders.Add(request.Headers.TryGetValues("X-Private", out var values) ? values.Single() : null);
            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
