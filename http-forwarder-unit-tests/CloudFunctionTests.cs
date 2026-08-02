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
using http_forwarder_app;

namespace http_forwarder_unit_tests;

public class FunctionUnitTests
{
    const string projectId = "test-project-id";
    const string topicId = "test-topic-id";
    const string eventName = "user-registered";
    const string validApiKey = "test-api-key";

    [Theory]
    [InlineData("xyz, user-registered", "POST")]
    [InlineData("user-registered, xyz", "PUT")]
    [InlineData("xyz,user-registered,", "POST")]
    [InlineData("user-registered*", "PUT")]
    [InlineData("*", "POST")]
    public async Task HandleAsync_ValidRequest_PublishesMessageAndReturnsOk(string allowedEvents, string requestMethod)
    {
        // Arrange
        var inMemorySettings = CreateDefaultSettings(allowedEvents);

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
        var inMemorySettings = CreateDefaultSettings("xyz, not-user-registered");
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
        var inMemorySettings = CreateDefaultSettings("[");
        const string requestMethod = "POST";
        var setupData = Setup(requestMethod: requestMethod, inMemorySettings: inMemorySettings);

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.NotAcceptable);
        setupData.MockPublishingService.Verify(p => p.Publish(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ForwardingRequest>()), Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenRateLimitExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        var inMemorySettings = CreateDefaultSettings("user-registered");
        inMemorySettings["RATE_LIMITING_ENABLED"] = "true";
        inMemorySettings["RATE_LIMIT_PER_WINDOW"] = "1";
        inMemorySettings["RATE_LIMIT_WINDOW_SECONDS"] = "60";
        const string requestMethod = "POST";
        const string clientIp = "198.51.100.77";
        var firstSetupData = Setup(requestMethod: requestMethod, inMemorySettings: inMemorySettings, clientIp: clientIp);
        var secondSetupData = Setup(requestMethod: requestMethod, inMemorySettings: inMemorySettings, clientIp: clientIp);

        var function = new http_forwarder_app.Functions.Function(firstSetupData.MockLogger.Object, firstSetupData.Configuration, firstSetupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(firstSetupData.HttpContext);
        await function.HandleAsync(secondSetupData.HttpContext);

        // Assert
        firstSetupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.OK);
        secondSetupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.TooManyRequests);
        secondSetupData.HttpContext.Response.Headers.RetryAfter.ToString().ShouldBe("60");
        firstSetupData.MockPublishingService.Verify(x => x.Publish(projectId, topicId, It.IsAny<ForwardingRequest>()), Times.Once);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task HandleAsync_MethodRequest_ReturnsNok(string requestMethod)
    {
        // Arrange
        var inMemorySettings = CreateDefaultSettings("xyz, user-registered");
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
    public async Task HandleAsync_MissingApiKey_ReturnsUnauthorized()
    {
        // Arrange
        var inMemorySettings = CreateDefaultSettings("user-registered");
        var setupData = Setup(requestMethod: "POST", inMemorySettings: inMemorySettings, apiKeyHeader: null, apiKeyQuery: null);

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.Unauthorized);
        setupData.MockPublishingService.Verify(p => p.Publish(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ForwardingRequest>()), Times.Never);
        var responseBody = await GetResponseContent(setupData);
        responseBody.ShouldContain("Unauthorized");
    }

    [Fact]
    public async Task HandleAsync_InvalidApiKey_ReturnsUnauthorized()
    {
        // Arrange
        var inMemorySettings = CreateDefaultSettings("user-registered");
        var setupData = Setup(requestMethod: "POST", inMemorySettings: inMemorySettings, apiKeyHeader: "wrong-key");

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.Unauthorized);
        setupData.MockPublishingService.Verify(p => p.Publish(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ForwardingRequest>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidApiKeyInQuery_PublishesMessageAndReturnsOk()
    {
        // Arrange
        var inMemorySettings = CreateDefaultSettings("user-registered");
        var setupData = Setup(requestMethod: "POST", inMemorySettings: inMemorySettings, apiKeyHeader: null, apiKeyQuery: validApiKey);

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.OK);
        setupData.MockPublishingService.Verify(x => x.Publish(projectId, topicId, It.IsAny<ForwardingRequest>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_BothApiKeysProvided_OneValid_PublishesWithoutApiKeyHeader()
    {
        // Arrange
        var inMemorySettings = CreateDefaultSettings("user-registered");
        var setupData = Setup(
            requestMethod: "POST",
            inMemorySettings: inMemorySettings,
            apiKeyHeader: validApiKey,
            apiKeyQuery: "other-key");

        var function = new http_forwarder_app.Functions.Function(setupData.MockLogger.Object, setupData.Configuration, setupData.MockPublishingService.Object);

        // Act
        await function.HandleAsync(setupData.HttpContext);

        // Assert
        setupData.RespFeature.StatusCode.ShouldBe((int)HttpStatusCode.OK);
        setupData.MockPublishingService.Verify(
            x => x.Publish(
                projectId,
                topicId,
                It.Is<ForwardingRequest>(r =>
                    r.Event == eventName
                    && !r.RequestHeaders.Keys.Any(k => string.Equals(k, "X-API-Key", StringComparison.OrdinalIgnoreCase)))),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,")]
    public void Constructor_WhenAllowedApiKeysMissingOrEmpty_Throws(string? allowedApiKeys)
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"ALLOWED_EVENTS", "user-registered"},
            {"GOOGLE_CLOUD_PROJECT_ID", projectId},
            {"PUBSUB_TOPIC_ID", topicId},
            {"ALLOWED_API_KEYS", allowedApiKeys},
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        var mockLogger = new Mock<ILogger<http_forwarder_app.Functions.Function>>();
        var mockPublishingService = new Mock<IPublishingService>();

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() =>
            new http_forwarder_app.Functions.Function(mockLogger.Object, configuration, mockPublishingService.Object));
        ex.Message.ShouldContain(Constants.ALLOWED_API_KEYS);
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

    private static Dictionary<string, string?> CreateDefaultSettings(string allowedEvents)
    {
        return new Dictionary<string, string?>
        {
            {"ALLOWED_EVENTS", allowedEvents},
            {"GOOGLE_CLOUD_PROJECT_ID", projectId},
            {"PUBSUB_TOPIC_ID", topicId},
            {"ALLOWED_API_KEYS", validApiKey},
        };
    }

    private static SetupData Setup(
        string requestMethod,
        IDictionary<string, string?> inMemorySettings,
        string? clientIp = null,
        string? apiKeyHeader = validApiKey,
        string? apiKeyQuery = null)
    {
        var mockLogger = new Mock<ILogger<http_forwarder_app.Functions.Function>>();
        var mockPublishingService = new Mock<IPublishingService>();
        var requestPath = $"/forward/{eventName}";

        var content = new { Name = "Jane Doe", Age = 40, City = "London" };
        var jsonContent = JsonUtils.Serialize(content, false);
        var headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "X-Test", "true" } };
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            headers["X-Forwarded-For"] = clientIp;
        }
        if (!string.IsNullOrEmpty(apiKeyHeader))
        {
            headers["X-API-Key"] = apiKeyHeader;
        }
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
        if (!string.IsNullOrEmpty(apiKeyQuery))
        {
            reqFeature.QueryString = $"?apiKey={Uri.EscapeDataString(apiKeyQuery)}";
        }
        httpContext.Features.Set(reqFeature);

        httpContext.Features.Set(respFeature);

        var responseBodyFeature = new StreamResponseBodyFeature(responseBodyStream);
        httpContext.Features.Set<IHttpResponseBodyFeature>(responseBodyFeature);

        // Published payload must not include the API key header
        var publishedHeaders = headers
            .Where(kvp => !string.Equals(kvp.Key, "X-API-Key", StringComparison.OrdinalIgnoreCase))
            .ToImmutableSortedDictionary();

        ForwardingRequest fwdRequest = new(
            Event: eventName,
            Method: requestMethod,
            Content: jsonContent,
            RequestHeaders: publishedHeaders);

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
