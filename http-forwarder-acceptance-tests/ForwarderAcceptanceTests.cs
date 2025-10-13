using Google.Cloud.PubSub.V1;
using http_forwarder_app.Cloud;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Moq;
using Shouldly;
using System.Collections.Immutable;
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

        var response = await client.PostAsync("/forward/ping-test", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

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

        var response = await client.PostAsync("/forward/cloud-test", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.ShouldBe("""Request will be processed by another system, published successfully with message Id message-id-1""");

        stubPublisherClient.MockPublisherClient.Verify(x => x.PublishAsync(It.IsAny<PubsubMessage>()), Times.Once);

        stubPublisherClient.PublishedMessages.ShouldHaveSingleItem();
        var publishedMsg = stubPublisherClient.PublishedMessages.Single();
        publishedMsg.Attributes.ShouldContainKeyAndValue(FunctionAttributes.EventAttribute, "cloud-test");
        publishedMsg.Attributes.ShouldContainKeyAndValue(FunctionAttributes.MethodAttribute, "POST");
        string messageData = System.Text.Encoding.UTF8.GetString(publishedMsg.Data.ToByteArray());
        var expectedMessage = new ForwardingRequest(
            Method: "POST",
            Event: "cloud-test",
            Content: """{}""",
            RequestHeaders: ImmutableSortedDictionary<string, string>.Empty.Add("Content-Type", "application/json; charset=utf-8"));
        var expectedMessageJson = JsonUtils.Serialize(expectedMessage, false);
        messageData.ShouldBe(expectedMessageJson);
    }

    [Fact]
    public async Task PostFailWithPredefinedContentShouldRetry()
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var storageFile = config.GetStorageFilePath();
        if (File.Exists(storageFile)) File.Delete(storageFile);

        var requestCapturingContext = _factory.Services.GetRequiredService<RequestCapturingContext>();
        while (requestCapturingContext.Requests.TryDequeue(out _)) ;

        var client = _factory.CreateClient();

        var response = await client.PostAsync("/forward/ping-fail", new StringContent("\"{}\""));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.ShouldStartWith("Request ping-fail accepted for retry - ");
        responseText.ShouldContain(" at ");
        requestCapturingContext.Requests.Count.ShouldBe(1);
        requestCapturingContext.Requests.First().RequestBody.ShouldBe("""{"message": "FAIL"}""");

        var retryService = _factory.Services.GetRequiredService<ManualRetryBackgroundService>() ?? throw new NullReferenceException($"Unable to get {nameof(ManualRetryBackgroundService)} from service provider");
        var clock = (FakeClock)_factory.Services.GetRequiredService<ISystemClock>();
        clock.AddTime(TimeSpan.FromSeconds(31));
        await retryService.ProcessPendingRequestsAsync(clock.UtcNow, CancellationToken.None);

        requestCapturingContext.Requests.Count.ShouldBe(2);
        requestCapturingContext.Requests.Last().RequestBody.ShouldBe("""{"message": "FAIL"}""");

        clock.AddTime(TimeSpan.FromSeconds(61));
        await retryService.ProcessPendingRequestsAsync(clock.UtcNow, CancellationToken.None);

        requestCapturingContext.Requests.Count.ShouldBe(3);
        requestCapturingContext.Requests.Last().RequestBody.ShouldBe("""{"message": "FAIL"}""");
    }

    [Fact]
    public async Task PostFailWithProvidedContentShouldRetry()
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var storageFile = config.GetStorageFilePath();
        if (File.Exists(storageFile)) File.Delete(storageFile);

        var requestCapturingContext = _factory.Services.GetRequiredService<RequestCapturingContext>();
        while (requestCapturingContext.Requests.TryDequeue(out _)) ;

        var client = _factory.CreateClient();

        var response = await client.PostAsync("/forward/ping-retry", new StringContent("""{"message": "FAIL"}""", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.ShouldStartWith("Request ping-retry accepted for retry - ");
        responseText.ShouldContain(" at ");
        requestCapturingContext.Requests.Count.ShouldBe(1);
        requestCapturingContext.Requests.First().RequestBody.ShouldBe("""{"message": "FAIL"}""");

        var retryService = _factory.Services.GetRequiredService<ManualRetryBackgroundService>() ?? throw new NullReferenceException($"Unable to get {nameof(ManualRetryBackgroundService)} from service provider");
        var clock = (FakeClock)_factory.Services.GetRequiredService<ISystemClock>();
        clock.AddTime(TimeSpan.FromSeconds(31));
        await retryService.ProcessPendingRequestsAsync(clock.UtcNow, CancellationToken.None);

        requestCapturingContext.Requests.Count.ShouldBe(2);
        requestCapturingContext.Requests.Last().RequestBody.ShouldBe("""{"message": "FAIL"}""");

        clock.AddTime(TimeSpan.FromSeconds(61));
        await retryService.ProcessPendingRequestsAsync(clock.UtcNow, CancellationToken.None);

        requestCapturingContext.Requests.Count.ShouldBe(3);
        requestCapturingContext.Requests.Last().RequestBody.ShouldBe("""{"message": "FAIL"}""");
    }
}
