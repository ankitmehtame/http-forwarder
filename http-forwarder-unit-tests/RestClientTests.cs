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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestContentType = request.Content?.Headers.ContentType?.MediaType;
            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
