using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Microsoft.AspNetCore.Http.Features;
using http_forwarder_app.Core;
using http_forwarder_app.Models.Services;
using http_forwarder_app.Models; // For ByteString

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

        setupData.MockPublishingService.Verify(x => x.Publish(projectId, topicId, It.IsAny<ForwardingRequest>()), Times.Once);

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

    private static SetupData Setup(string requestMethod, IDictionary<string, string?> inMemorySettings)
    {
        var mockLogger = new Mock<ILogger<http_forwarder_app.Functions.Function>>();
        var mockPublishingService = new Mock<IPublishingService>();
        var requestPath = $"/forward/{eventName}";

        var content = new { Name = "Jane Doe", Age = 40, City = "London" };
        var jsonContent = JsonUtils.Serialize(content, false);
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
        httpContext.Features.Set(reqFeature);

        httpContext.Features.Set(respFeature);

        var responseBodyFeature = new StreamResponseBodyFeature(responseBodyStream);
        httpContext.Features.Set<IHttpResponseBodyFeature>(responseBodyFeature);

        ForwardingRequest fwdRequest = new(Event: eventName, Method: requestMethod, Content: jsonContent);

        mockPublishingService
            .Setup(ps => ps.Publish(projectId, topicId, fwdRequest))
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