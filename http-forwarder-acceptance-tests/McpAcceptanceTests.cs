using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Cloud.PubSub.V1;
using http_forwarder_app.Cloud;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Moq;
using ModelContextProtocol.Client;
using Shouldly;

namespace http_forwarder_acceptance_tests;

public class McpAcceptanceTests
{
    private static async Task<(HttpClientTransport Transport, McpClient Client)> Connect(CustomWebApplicationFactory<Program> factory,
        string protocolVersion = "2025-11-25")
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        }, http);
        var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion });
        return (transport, client);
    }

    [Fact]
    public async Task PinnedJuly2026ProtocolAcceptsNewClientAndRejectsHandshake()
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        {
            ["MCP_PROTOCOL_VERSION"] = "2026-07-28"
        });
        var (transport, client) = await Connect(factory, "2026-07-28");
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        client.NegotiatedProtocolVersion.ShouldBe("2026-07-28");
        (await client.ListToolsAsync()).Select(x => x.Name).ShouldContain("ping_test");
        var result = await client.CallToolAsync("ping_test", new Dictionary<string, object?>());
        result.IsError.ShouldBe(false);

        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"old-client","version":"1.0"}}}
                """, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");
        using var rejected = await http.SendAsync(request);
        (await rejected.Content.ReadAsStringAsync()).ShouldContain("error");
    }

    [Fact]
    public async Task PinnedHandshakeProtocolRejectsJuly2026Client()
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        {
            ["MCP_PROTOCOL_VERSION"] = "2025-11-25"
        });
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        client.NegotiatedProtocolVersion.ShouldBe("2025-11-25");

        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        await using var latestTransport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        }, http);
        await Should.ThrowAsync<Exception>(async () =>
        {
            await using var latestClient = await McpClient.CreateAsync(latestTransport,
                new McpClientOptions { ProtocolVersion = "2026-07-28" });
        });
    }

    [Fact]
    public async Task DiscoversAndInvokesConfiguredForwarding()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        client.SessionId.ShouldBeNull();
        var tools = await client.ListToolsAsync();
        tools.Select(x => x.Name).ShouldContain("ping_test");
        tools.Select(x => x.Name).ShouldContain("ping_request");
        tools.Select(x => x.Name).ShouldNotContain("forward_event");
        tools.Select(x => x.Name).ShouldNotContain("TEST");
        tools.Select(x => x.Name).ShouldContain("cloud_test");
        tools.Single(x => x.Name == "ping_test_post").ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        tools.Single(x => x.Name == "ping_request").ProtocolTool.InputSchema.GetProperty("required")
            .EnumerateArray().Select(x => x.GetString()).ShouldContain("message");
        tools.Single(x => x.Name == "ping_request").ProtocolTool.InputSchema.GetProperty("properties")
            .GetProperty("message").GetProperty("description").GetString().ShouldBe("Message to send.");

        var result = await client.CallToolAsync("ping_test", new Dictionary<string, object?>());
        result.IsError.ShouldBe(false);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().ShouldBe(200);
        result.StructuredContent.Value.GetProperty("body").GetString()!.ShouldContain("Pong");

    }

    [Fact]
    public async Task PublishesRemoteRuleWithoutTransportCredentials()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        var publisher = (StubPublisherClientFactory)factory.Services.GetRequiredService<IPublisherClientFactory>();
        publisher.Reset();
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        var result = await client.CallToolAsync("cloud_test", new Dictionary<string, object?>());
        result.IsError.ShouldBe(false);
        result.StructuredContent!.Value.GetProperty("kind").GetString().ShouldBe("published");
        result.StructuredContent.Value.GetProperty("status").GetInt32().ShouldBe(202);
        publisher.MockPublisherClient.Verify(x => x.PublishAsync(It.IsAny<PubsubMessage>()), Times.Once);
        var published = publisher.PublishedMessages.Single();
        var text = System.Text.Encoding.UTF8.GetString(published.Data.ToByteArray());
        text.ShouldContain("cloud-message-567");
        text.ShouldNotContain("secret");
        text.ShouldNotContain("test-mcp-key");
        text.ShouldNotContain("Session-Id");
    }

    [Theory]
    [InlineData("false", null, 406)]
    [InlineData("true", "", 500)]
    public async Task RemoteToolReportsPublishingFailures(string publisherEnabled, string? cloudTopic, int expectedStatus)
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        {
            ["PUBLISHER_ENABLED"] = publisherEnabled,
            ["PUBSUB_TOPIC_ID_CLOUD"] = cloudTopic,
            ["PUBSUB_TOPIC_ID"] = "unused-test-topic"
        });
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        var result = await client.CallToolAsync("cloud_test", new Dictionary<string, object?>());
        result.IsError.ShouldBe(true);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().ShouldBe(expectedStatus);
    }

    [Fact]
    public async Task RetryAcceptancePersistsWithoutCredentials()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        var storage = factory.Services.GetRequiredService<IFailedRequestStorage>();
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        var result = await client.CallToolAsync("ping_retry", new Dictionary<string, object?> { ["message"] = "FAIL" });
        result.IsError.ShouldBe(false);
        result.StructuredContent!.Value.GetProperty("kind").GetString().ShouldBe("retry_accepted");
        var id = result.StructuredContent.Value.GetProperty("retryId").GetGuid();
        var stored = storage.GetAllRequests().Single(x => x.Id == id);
        stored.RequestHeaders.Keys.ShouldNotContain("Authorization");
        stored.RequestBody.ShouldBe("{\"message\":\"FAIL\"}");
        storage.Remove(id);
    }

    [Fact]
    public async Task ConfiguredContentAndRequiredBodyMatchHttpRules()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        var noBody = await client.CallToolAsync("ping_request", new Dictionary<string, object?>());
        noBody.IsError.ShouldBe(true);
        noBody.StructuredContent!.Value.GetProperty("status").GetInt32().ShouldBe(400);

        var configured = await client.CallToolAsync("ping_test_post", new Dictionary<string, object?>());
        configured.IsError.ShouldBe(false);
        configured.StructuredContent!.Value.GetProperty("body").GetString().ShouldBe("{\"message\":\"message-567\"}");

        var valid = await client.CallToolAsync("ping_request", new Dictionary<string, object?> { ["message"] = "hello" });
        valid.IsError.ShouldBe(false);
    }

    [Fact]
    public async Task InvalidArgumentsCannotTriggerForwarding()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        var captures = factory.Services.GetRequiredService<RequestCapturingContext>();
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;

        var missing = await client.CallToolAsync("ping_request", new Dictionary<string, object?>());
        var wrongType = await client.CallToolAsync("ping_request", new Dictionary<string, object?> { ["message"] = 123 });
        var extra = await client.CallToolAsync("ping_test_post", new Dictionary<string, object?> { ["Authorization"] = "secret" });
        missing.IsError.ShouldBe(true);
        wrongType.IsError.ShouldBe(true);
        extra.IsError.ShouldBe(true);
        captures.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task TruncatedSuccessDoesNotBecomeToolError()
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        { ["MCP_MAX_RESPONSE_BYTES"] = "5" });
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        var result = await client.CallToolAsync("ping_test", new Dictionary<string, object?>());
        result.IsError.ShouldBe(false);
        var content = result.StructuredContent!.Value;
        content.GetProperty("status").GetInt32().ShouldBe(200);
        content.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        content.GetProperty("bytesRead").GetInt32().ShouldBe(5);
        content.GetProperty("maxBytes").GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task BinaryResponseIsBase64AndSensitiveHeadersAreOmitted()
    {
        var forwarder = new Mock<IForwardingService>();
        var rule = new ForwardingRule("GET", "ping-test", "http://example.test/");
        forwarder.Setup(x => x.ProcessGetEvent("ping-test", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(new byte[] { 0, 255, 12 }) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response.Headers.Add("X-Public", "visible");
                response.Headers.Add("X-Secret", "hidden");
                response.Headers.TryAddWithoutValidation("Set-Cookie", "session=private");
                return new HttpResponseRuleResult(response, rule);
            });
        using var root = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        { ["MASKED_HEADERS"] = "X-Secret" });
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IForwardingService>();
            services.AddSingleton(forwarder.Object);
        }));
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        { Endpoint = new Uri("http://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" });
        var result = await client.CallToolAsync("ping_test", new Dictionary<string, object?>());
        var content = result.StructuredContent!.Value;
        content.GetProperty("encoding").GetString().ShouldBe("base64");
        content.GetProperty("body").GetString().ShouldBe("AP8M");
        var headers = content.GetProperty("headers");
        headers.GetProperty("X-Public").GetString().ShouldBe("visible");
        headers.TryGetProperty("X-Secret", out _).ShouldBeFalse();
        headers.TryGetProperty("Set-Cookie", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task DisabledEndpointReturnsNotFound()
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        {
            ["MCP_ENABLED"] = "false"
        });
        using var http = factory.CreateClient();
        (await http.PostAsync("/mcp", new StringContent("{}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RateLimitUsesCredentialRatherThanForwardedIp()
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        { ["MCP_RATE_LIMIT_PER_WINDOW"] = "1" });
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}";
        using var first = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        first.Headers.Add("X-Forwarded-For", "192.0.2.1");
        first.Headers.Accept.ParseAdd("application/json");
        first.Headers.Accept.ParseAdd("text/event-stream");
        (await http.SendAsync(first)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var second = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        second.Headers.Add("X-Forwarded-For", "192.0.2.2");
        var rejected = await http.SendAsync(second);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task HandshakeOnOneInstanceAndInvokeOnAnother()
    {
        using var a = new CustomWebApplicationFactory<Program>();
        using var b = new CustomWebApplicationFactory<Program>();
        using var routing = new InstanceRoutingHandler(a.Server.CreateHandler(), b.Server.CreateHandler());
        using var http = new HttpClient(routing);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        }, http);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" });
        client.SessionId.ShouldBeNull();
        routing.UseSecond = true;
        (await client.ListToolsAsync()).Select(x => x.Name).ShouldContain("ping_test");
        var result = await client.CallToolAsync("ping_test", new Dictionary<string, object?>());
        result.IsError.ShouldBe(false);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().ShouldBe(200);
    }

    private sealed class InstanceRoutingHandler(HttpMessageHandler first, HttpMessageHandler second) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _first = new(first);
        private readonly HttpMessageInvoker _second = new(second);
        public bool UseSecond { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            (UseSecond ? _second : _first).SendAsync(request, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _first.Dispose(); _second.Dispose(); }
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task RequiresCredentialsAndRejectsOrigin()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        { Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", System.Text.Encoding.UTF8, "application/json") };
        (await http.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var originRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
        originRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        originRequest.Headers.Add("Origin", "https://untrusted.example");
        (await http.SendAsync(originRequest)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OversizeRequestIsRejectedBeforeToolExecution()
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        { ["MCP_MAX_REQUEST_BYTES"] = "64" });
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        { Content = new StringContent(new string('x', 65), System.Text.Encoding.UTF8, "application/json") };
        (await http.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        factory.Services.GetRequiredService<RequestCapturingContext>().Requests.ShouldBeEmpty();
    }
}
