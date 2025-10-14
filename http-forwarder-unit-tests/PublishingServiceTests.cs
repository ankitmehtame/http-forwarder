using Microsoft.Extensions.Logging;
using Moq;
using Google.Cloud.PubSub.V1;
using Shouldly;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Cloud;
using System.Collections.Immutable;
using System.Data.Common;

namespace http_forwarder_unit_tests;

public class PublishingServiceTests
{
    [Fact]
    public async Task ShouldPublishMessageAndAttributes()
    {
        var mockLogger = new Mock<ILogger<PublishingService>>();
        var mockPublisherClientFactory = new Mock<IPublisherClientFactory>();
        var mockPublisherClient = new Mock<PublisherClient>();

        var service = new PublishingService(mockLogger.Object, mockPublisherClientFactory.Object);

        const string projectId = "test-project-id";
        const string topicId = "test-topic-id";
        const string eventName = "TEST-EVENT";
        const string requestMethod = "POST";
        var content = new { Name = "Jane Doe", Age = 40, City = "London" };
        var jsonContent = JsonUtils.Serialize(content, false);
        var requestHeaders = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "X-Test", "true" } };
        ForwardingRequest fwdRequest = new(Event: eventName, Method: requestMethod, Content: jsonContent, RequestHeaders: requestHeaders.ToImmutableSortedDictionary());

        mockPublisherClientFactory.Setup(x => x.GetOrCreate(projectId, topicId)).Returns(mockPublisherClient.Object);

        mockPublisherClient
            .Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .ReturnsAsync("test-message-id-123")
            .Verifiable();

        await service.Publish(projectId, topicId, fwdRequest);

        mockPublisherClient.Verify(p => p.PublishAsync(It.IsAny<PubsubMessage>()), Times.Once());

        var capturedMessages = new List<PubsubMessage>();
        mockPublisherClient.Verify(p => p.PublishAsync(Capture.In(capturedMessages)), Times.Once());

        var publishedMessage = capturedMessages.FirstOrDefault();

        publishedMessage.ShouldNotBeNull();

        // Verify the message data payload
        var publishedDataString = System.Text.Encoding.UTF8.GetString(publishedMessage.Data.ToArray());
        var expectedMessage = new ForwardingRequest(Method: requestMethod, Event: eventName, Content: jsonContent, RequestHeaders: requestHeaders.ToImmutableSortedDictionary());
        publishedDataString.ShouldBe(JsonUtils.Serialize(expectedMessage, false));

        // Verify the message attributes include the eventName
        publishedMessage.Attributes.ShouldNotBeEmpty();
        publishedMessage.Attributes.ShouldContainKeyAndValue("EVENT", eventName);
        publishedMessage.Attributes.ShouldContainKeyAndValue("METHOD", requestMethod);
    }
}