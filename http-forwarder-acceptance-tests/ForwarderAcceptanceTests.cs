using Google.Cloud.PubSub.V1;
using http_forwarder_app.Cloud;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Moq;
using Shouldly;
using System.Net;

namespace http_forwarder_acceptance_tests;

public class ForwarderAcceptanceTests(CustomWebApplicationFactory<Program> factory) : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly CustomWebApplicationFactory<Program> _factory = factory;

    [Fact]
    public async Task GetPingShouldReturnPong()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/forward/ping-test");

        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldBe("""{"message":"Pong"}""");
    }

    [Fact]
    public async Task PostPingShouldReturnPong()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/forward/ping-test", new StringContent("{}"));

        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.ShouldBe("""{"message":"message-567"}""");
    }

    [Fact]
    public async Task PostCloudShouldPublish()
    {
        var stubPublisherClient = (StubPublisherClientFactory)_factory.Services.GetRequiredService<IPublisherClientFactory>();
        stubPublisherClient.Reset();
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/forward/cloud-test", new StringContent("{}"));

        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.ShouldBe("""Request will be processed by another system, published successfully with message Id message-id-1""");

        stubPublisherClient.MockPublisherClient.Verify(x => x.PublishAsync(It.IsAny<PubsubMessage>()), Times.Once);

        stubPublisherClient.PublishedMessages.ShouldHaveSingleItem();
        var publishedMsg = stubPublisherClient.PublishedMessages.Single();
        publishedMsg.Attributes.ShouldContainKeyAndValue(FunctionAttributes.EventAttribute, "cloud-test");
        publishedMsg.Attributes.ShouldContainKeyAndValue(FunctionAttributes.MethodAttribute, "POST");
        string messageData = System.Text.Encoding.UTF8.GetString(publishedMsg.Data.ToByteArray());
        var expectedMessage = new ForwardingRequest(Method: "POST", Event: "cloud-test", Content: """{}""");
        var expectedMessageJson = JsonUtils.Serialize(expectedMessage, false);
        messageData.ShouldBe(expectedMessageJson);
    }

    [Fact]
    public async Task PostFailShouldReturnAcceptedForRetry()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/forward/ping-fail", new StringContent("""{}"""));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.ShouldStartWith("Request accepted for retry - ");

        var retryService = _factory.Services.GetRequiredService<ManualRetryBackgroundService>() ?? throw new NullReferenceException($"Unable to get {nameof(ManualRetryBackgroundService)} from service provider");
        var clock = (FakeClock)_factory.Services.GetRequiredService<ISystemClock>();
        clock.AddTime(TimeSpan.FromMinutes(1));
        await retryService.ProcessPendingRequestsAsync(clock.UtcNow, CancellationToken.None);


    }
}
