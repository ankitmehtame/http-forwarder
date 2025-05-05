using Shouldly;

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
}
