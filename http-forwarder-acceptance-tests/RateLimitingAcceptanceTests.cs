using http_forwarder_app;
using System.Net;
using Shouldly;

namespace http_forwarder_acceptance_tests;

public class RateLimitingAcceptanceTests
{
    [Fact]
    public async Task RateLimiting_WhenLimitExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        {
            { Constants.RATE_LIMITING_ENABLED, "true" },
            { Constants.RATE_LIMIT_PER_WINDOW, "1" },
            { Constants.RATE_LIMIT_WINDOW_SECONDS, "60" }
        });
        var client = factory.CreateClient();
        const string clientIp = "203.0.113.10";

        // Act
        var first = await SendPing(client, clientIp);
        var second = await SendPing(client, clientIp);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        second.Headers.RetryAfter.ShouldNotBeNull();
        second.Headers.RetryAfter.Delta.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task RateLimiting_WhenDifferentClientIps_EachGetsOwnLimit()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        {
            { Constants.RATE_LIMITING_ENABLED, "true" },
            { Constants.RATE_LIMIT_PER_WINDOW, "1" },
            { Constants.RATE_LIMIT_WINDOW_SECONDS, "60" }
        });
        var client = factory.CreateClient();

        // Act
        var firstClient = await SendPing(client, "203.0.113.11");
        var secondClient = await SendPing(client, "203.0.113.12");

        // Assert
        firstClient.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondClient.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> SendPing(HttpClient client, string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/ping");
        request.Headers.Add("X-Forwarded-For", clientIp);
        return client.SendAsync(request);
    }
}
