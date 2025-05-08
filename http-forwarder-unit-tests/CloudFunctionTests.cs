using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Google.Cloud.PubSub.V1; // Assuming your Function class uses this directly
using Microsoft.Extensions.Configuration;
using Shouldly;
using Microsoft.AspNetCore.Http.Features;
using http_forwarder_app.Core;
using http_forwarder_app.Models; // For ByteString

namespace http_forwarder_unit_tests;

public class FunctionUnitTests
{
    [Theory]
    [InlineData("xyz, user-registered", "POST")]
    [InlineData("user-registered, xyz", "PUT")]
    [InlineData("xyz,user-registered,", "POST")]
    [InlineData("user-registered*", "PUT")]
    [InlineData("*", "POST")]
    public async Task HandleAsync_ValidRequest_PublishesMessageAndReturnsOk(string allowedEvents, string requestMethod)
    {
        // Arrange
        var mockLogger = new Mock<ILogger<http_forwarder_app.Functions.Function>>();
        var mockPublisherClientFactory = new Mock<http_forwarder_app.Functions.IPublisherClientFactory>();
        var mockPublisherClient = new Mock<PublisherClient>();
        mockPublisherClientFactory.Setup(x => x.Create()).Returns(mockPublisherClient.Object);
        var inMemorySettings = new Dictionary<string, string?> {
            {"ALLOWED_EVENTS", allowedEvents},
            {"GOOGLE_CLOUD_PROJECT_ID", "test-project-id"},
            {"PUBSUB_TOPIC_ID", "test-topic-id"},
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var httpContext = new DefaultHttpContext();

        var content = new { Name = "Jane Doe", Age = 40, City = "London" };
        var jsonContent = JsonUtils.Serialize(content, false);
        var requestBodyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));


        // Set up HttpRequest to simulate a POST request with a path and body
        var eventName = "user-registered";
        var requestPath = $"/forward/{eventName}";

        var reqFeature = httpContext.Features.Get<IHttpRequestFeature>() ?? new HttpRequestFeature();
        reqFeature.Path = requestPath;
        reqFeature.Method = requestMethod;
        reqFeature.Body = requestBodyStream;
        httpContext.Features.Set(reqFeature);

        var responseBodyStream = new MemoryStream();

        var respFeature = httpContext.Features.Get<IHttpResponseFeature>() ?? new HttpResponseFeature();
        httpContext.Features.Set(respFeature);

        var responseBodyFeature = new StreamResponseBodyFeature(responseBodyStream);
        httpContext.Features.Set<IHttpResponseBodyFeature>(responseBodyFeature);

        // Set up the mock PublisherClient's PublishAsync method
        // We don't need it to actually publish, just verify it's called.
        // Return a dummy message ID.
        mockPublisherClient
            .Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .ReturnsAsync("test-message-id-123")
            .Verifiable(); // Verify PublishAsync is called


        var function = new http_forwarder_app.Functions.Function(mockLogger.Object, configuration, mockPublisherClientFactory.Object);


        // Act
        await function.HandleAsync(httpContext);

        // Assert

        respFeature.StatusCode.ShouldBe((int)HttpStatusCode.OK);

        mockPublisherClient.Verify(p => p.PublishAsync(It.IsAny<PubsubMessage>()), Times.Once());

        var capturedMessages = new List<PubsubMessage>();
        mockPublisherClient.Verify(p => p.PublishAsync(Capture.In(capturedMessages)), Times.Once());

        var publishedMessage = capturedMessages.FirstOrDefault();

        publishedMessage.ShouldNotBeNull();

        // Verify the message data payload
        var publishedDataString = System.Text.Encoding.UTF8.GetString(publishedMessage.Data.ToArray());
        var expectedMessage = new ForwardingRequest(Method: requestMethod, Event: eventName, Content: jsonContent);
        publishedDataString.ShouldBe(JsonUtils.Serialize(expectedMessage, false));

        // Verify the message attributes include the eventName
        publishedMessage.Attributes.ShouldNotBeEmpty();
        publishedMessage.Attributes.ShouldContainKeyAndValue("EVENT", eventName);
        publishedMessage.Attributes.ShouldContainKeyAndValue("METHOD", requestMethod);

        // Verify the content of the HTTP response body from the function
        await responseBodyStream.FlushAsync();
        responseBodyStream.Position = 0; // Rewind stream to read from the beginning
        using var reader = new StreamReader(responseBodyStream, System.Text.Encoding.UTF8);
        var responseBody = await reader.ReadToEndAsync();
        responseBody.ShouldContain("Message published successfully");
        responseBody.ShouldContain("Message ID: test-message-id-123");
        responseBody.ShouldContain($"for event {eventName} & method {requestMethod}");
    }

    [Fact]
    public async Task HandleAsync_PostRequest_NotAllowedEvent_ReturnsNok()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<http_forwarder_app.Functions.Function>>();
        var mockPublisherClientFactory = new Mock<http_forwarder_app.Functions.IPublisherClientFactory>();
        var mockPublisherClient = new Mock<PublisherClient>();
        mockPublisherClientFactory.Setup(x => x.Create()).Returns(mockPublisherClient.Object);
        var inMemorySettings = new Dictionary<string, string?> {
            {"ALLOWED_EVENTS", "xyz, not-user-registered"},
            {"GOOGLE_CLOUD_PROJECT_ID", "test-project-id"},
            {"PUBSUB_TOPIC_ID", "test-topic-id"},
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var httpContext = new DefaultHttpContext();

        var content = new { Name = "Jane Doe", Age = 40, City = "London" };
        var jsonContent = JsonUtils.Serialize(content, false);
        var requestBodyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));


        // Set up HttpRequest to simulate a POST request with a path and body
        var eventName = "user-registered";
        var requestPath = $"/{eventName}";

        var reqFeature = httpContext.Features.Get<IHttpRequestFeature>() ?? new HttpRequestFeature();
        reqFeature.Path = requestPath;
        reqFeature.Method = "POST";
        reqFeature.Body = requestBodyStream;
        httpContext.Features.Set(reqFeature);

        var responseBodyStream = new MemoryStream();

        var respFeature = httpContext.Features.Get<IHttpResponseFeature>() ?? new HttpResponseFeature();
        httpContext.Features.Set(respFeature);

        var responseBodyFeature = new StreamResponseBodyFeature(responseBodyStream);
        httpContext.Features.Set<IHttpResponseBodyFeature>(responseBodyFeature);

        // Set up the mock PublisherClient's PublishAsync method
        // We don't need it to actually publish, just verify it's called.
        // Return a dummy message ID.
        mockPublisherClient
            .Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .ReturnsAsync("test-message-id-123")
            .Verifiable(); // Verify PublishAsync is called


        var function = new http_forwarder_app.Functions.Function(mockLogger.Object, configuration, mockPublisherClientFactory.Object);


        // Act
        await function.HandleAsync(httpContext);

        // Assert

        respFeature.StatusCode.ShouldBe((int)HttpStatusCode.NotAcceptable);

        mockPublisherClient.Verify(p => p.PublishAsync(It.IsAny<PubsubMessage>()), Times.Never());

        var capturedMessages = new List<PubsubMessage>();
        mockPublisherClient.Verify(p => p.PublishAsync(Capture.In(capturedMessages)), Times.Never());

        // Verify the content of the HTTP response body from the function
        await responseBodyStream.FlushAsync();
        responseBodyStream.Position = 0; // Rewind stream to read from the beginning
        using var reader = new StreamReader(responseBodyStream, System.Text.Encoding.UTF8);
        var responseBody = await reader.ReadToEndAsync();
        responseBody.ShouldContain($"Not allowed event {eventName}");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task HandleAsync_MethodRequest_ReturnsNok(string requestMethod)
    {
        // Arrange
        var mockLogger = new Mock<ILogger<http_forwarder_app.Functions.Function>>();
        var mockPublisherClientFactory = new Mock<http_forwarder_app.Functions.IPublisherClientFactory>();
        var mockPublisherClient = new Mock<PublisherClient>();
        mockPublisherClientFactory.Setup(x => x.Create()).Returns(mockPublisherClient.Object);
        var inMemorySettings = new Dictionary<string, string?> {
            {"ALLOWED_EVENTS", "xyz, user-registered"},
            {"GOOGLE_CLOUD_PROJECT_ID", "test-project-id"},
            {"PUBSUB_TOPIC_ID", "test-topic-id"},
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var httpContext = new DefaultHttpContext();


        // Set up HttpRequest to simulate a GET request with a path and body
        var eventName = "user-registered";
        var requestPath = $"/{eventName}";

        var reqFeature = httpContext.Features.Get<IHttpRequestFeature>() ?? new HttpRequestFeature();
        reqFeature.Path = requestPath;
        reqFeature.Method = requestMethod;
        httpContext.Features.Set(reqFeature);

        var responseBodyStream = new MemoryStream();

        var respFeature = httpContext.Features.Get<IHttpResponseFeature>() ?? new HttpResponseFeature();
        httpContext.Features.Set(respFeature);

        var responseBodyFeature = new StreamResponseBodyFeature(responseBodyStream);
        httpContext.Features.Set<IHttpResponseBodyFeature>(responseBodyFeature);


        mockPublisherClient
            .Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .ReturnsAsync("test-message-id-123")
            .Verifiable(Times.Never);


        var function = new http_forwarder_app.Functions.Function(mockLogger.Object, configuration, mockPublisherClientFactory.Object);


        // Act
        await function.HandleAsync(httpContext);

        // Assert

        respFeature.StatusCode.ShouldBe((int)HttpStatusCode.MethodNotAllowed);

        mockPublisherClient.Verify(p => p.PublishAsync(It.IsAny<PubsubMessage>()), Times.Never());

        var capturedMessages = new List<PubsubMessage>();
        mockPublisherClient.Verify(p => p.PublishAsync(Capture.In(capturedMessages)), Times.Never());

        // Verify the content of the HTTP response body from the function
        await responseBodyStream.FlushAsync();
        responseBodyStream.Position = 0; // Rewind stream to read from the beginning
        using var reader = new StreamReader(responseBodyStream, System.Text.Encoding.UTF8);
        var responseBody = await reader.ReadToEndAsync();
        responseBody.ShouldContain("Only POST/PUT requests are allowed.");
    }
}