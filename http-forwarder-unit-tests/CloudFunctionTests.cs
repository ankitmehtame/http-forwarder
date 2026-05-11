using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Microsoft.AspNetCore.Http.Features;
using http_forwarder_app.Models.Services;
using http_forwarder_app.Models;
using http_forwarder_app.Utils;
using System.Collections.Immutable;

namespace http_forwarder_unit_tests;

public class FunctionUnitTests
{
    const string projectId = "test-project-id";
    const string topicId = "test-topic-id";
    const string eventName = "user-registered";

    [Theory]
    [InlineData("xyz, user-registered", "POST")]
    [InlineData("user-registered, xyz", "PUT")]
    [InlineData("xyz,user-registered,", "POST")]
    [InlineData("user-registered*", "PUT")]
    [InlineData("*", "POST")]
    public async Task HandleAsync_ValidRequest_PublishesMessageAndReturnsOk(string allowedEvents, string requestMethod)
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?> {
            {"ALLOWED_EVENTS", allowedEvents},
            {"GOOGLE_CLOUD_PROJECT_ID", projectId},
            {"PUBSUB_TOPIC_ID", topicId},
        };

        var setupData = Setup(requestMethod: requestMethod, inMemorySettings: inMemorySettings);

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.OK);

        setupData.MockPublishingService.Verify(x => x.Publish(projectId, topicId, It.Is<ForwardingRequest>(r => r.Method == requestMethod && r.Event == eventName)), Times.Once);

        var responseBody = await GetResponseContent(setupData);
        responseBody.ShouldContain("Message published successfully");
        responseBody.ShouldContain("Message ID: test-message-id-123");
        responseBody.ShouldContain($"for event {eventName} & method {requestMethod}");
    }

    [Fact]
    public async Task HandleAsync_PostRequest_NotAllowedEvent_ReturnsNok()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?> {
            {"ALLOWED_EVENTS", "xyz, not-user-registered"},
            {"GOOGLE_CLOUD_PROJECT_ID", projectId},
            {"PUBSUB_TOPIC_ID", topicId},
        };
        const string requestMethod = "POST";
        var setupData = Setup(requestMethod: requestMethod, inMemorySettings: inMemorySettings);

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.NotAcceptable);
        setupData.MockPublishingService.Verify(p => p.Publish(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ForwardingRequest>()), Times.Never());

        // Verify the content of the HTTP response body from the function
        var responseBody = await GetResponseContent(setupData);
        responseBody.ShouldContain($"Not allowed event {eventName}");
    }

    [Fact]
    public async Task HandleAsync_InvalidAllowedEventRegex_ReturnsNok()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?> {
            {"ALLOWED_EVENTS", "["},
            {"GOOGLE_CLOUD_PROJECT_ID", projectId},
            {"PUBSUB_TOPIC_ID", topicId},
        };
        const string requestMethod = "POST";
        var setupData = Setup(requestMethod: requestMethod, inMemorySettings: inMemorySettings);

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.NotAcceptable);
        setupData.MockPublishingService.Verify(p => p.Publish(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ForwardingRequest>()), Times.Never());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task HandleAsync_MethodRequest_ReturnsNok(string requestMethod)
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?> {
            {"ALLOWED_EVENTS", "xyz, user-registered"},
            {"GOOGLE_CLOUD_PROJECT_ID", "test-project-id"},
            {"PUBSUB_TOPIC_ID", "test-topic-id"},
        };
        var setupData = Setup(requestMethod: requestMethod, inMemorySettings: inMemorySettings);

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.MethodNotAllowed);
        setupData.MockPublishingService.Verify(p => p.Publish(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ForwardingRequest>()), Times.Never);

        // Verify the content of the HTTP response body from the function
        var responseBody = await GetResponseContent(setupData);
        responseBody.ShouldContain("Not allowed");
    }

    [Fact]
    public void Equate_ForwardingRequests()
    {
        var headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "X-Test", "true" } };

        static ForwardingRequest CreateReq(ImmutableSortedDictionary<string, string> reqHeaders)
        {
            return new(
            Method: "POST",
            Event: "ev1",
            Content: "content1",
            RequestHeaders: reqHeaders);
        }

        var req1 = CreateReq(headers.ToImmutableSortedDictionary());
        var req2 = CreateReq(headers.Reverse().ToImmutableSortedDictionary());
        var req3 = CreateReq(headers.ToImmutableSortedDictionary());

        req3.ShouldBeEquivalentTo(req1);
        req2.ShouldBeEquivalentTo(req1);
        req3.ShouldNotBeSameAs(req1);
        req2.ShouldNotBeSameAs(req1);
        (req3 == req1).ShouldBeTrue();
        (req2 == req1).ShouldBeTrue();
    }

    private static SetupData Setup(string requestMethod, IDictionary<string, string?> inMemorySettings)
    {
        var mockLogger = new Mock<ILogger<http_forwarder_app.Functions.Function>>();
        var mockPublishingService = new Mock<IPublishingService>();
        var requestPath = $"/forward/{eventName}";

        var content = new { Name = "Jane Doe", Age = 40, City = "London" };
        var jsonContent = JsonUtils.Serialize(content, false);
        var headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "X-Test", "true" } };
        var requestBodyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var httpContext = new DefaultHttpContext();
        var responseBodyStream = new MemoryStream();
        var respFeature = httpContext.Features.Get<IHttpResponseFeature>() ?? new HttpResponseFeature();

        var reqFeature = httpContext.Features.Get<IHttpRequestFeature>() ?? new HttpRequestFeature();
        reqFeature.Path = requestPath;
        reqFeature.Method = requestMethod;
        reqFeature.Body = requestBodyStream;
        reqFeature.Headers = new HeaderDictionary();
        headers.ToList().ForEach(x => reqFeature.Headers.Append(x.Key, x.Value));
        httpContext.Features.Set(reqFeature);

        httpContext.Features.Set(respFeature);

        var responseBodyFeature = new StreamResponseBodyFeature(responseBodyStream);
        httpContext.Features.Set<IHttpResponseBodyFeature>(responseBodyFeature);

        ForwardingRequest fwdRequest = new(
            Event: eventName,
            Method: requestMethod,
            Content: jsonContent,
            RequestHeaders: headers.ToImmutableSortedDictionary());

        mockPublishingService
            .Setup(ps => ps.Publish(projectId, topicId, It.Is<ForwardingRequest>(r => r == fwdRequest)))
            .ReturnsAsync(new RemoteRulePublishSuccessResult("test-message-id-123"))
            .Verifiable();

        return new(MockLogger: mockLogger, MockPublishingService: mockPublishingService, HttpContext: httpContext, RespFeature: respFeature,
            ResponseBodyStream: responseBodyStream, Configuration: configuration);
    }

    private record class SetupData(Mock<ILogger<http_forwarder_app.Functions.Function>> MockLogger, Mock<IPublishingService> MockPublishingService,
        DefaultHttpContext HttpContext, IHttpResponseFeature RespFeature, MemoryStream ResponseBodyStream, IConfiguration Configuration);

    private static async Task<string> GetResponseContent(SetupData setupData)
    {
        await setupData.ResponseBodyStream.FlushAsync();
        setupData.ResponseBodyStream.Position = 0; // Rewind stream to read from the beginning
        using var reader = new StreamReader(setupData.ResponseBodyStream, System.Text.Encoding.UTF8);
        var responseBody = await reader.ReadToEndAsync();
        return responseBody;
    }
}
