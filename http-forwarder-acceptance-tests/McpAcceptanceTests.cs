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
    private static async Task<(HttpClientTransport Transport, McpClient Client)> Connect(CustomWebApplicationFactory<Program> factory)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-key");
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        }, http);
        var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" });
        return (transport, client);
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
        tools.Count.ShouldBe(1);
        tools[0].Name.ShouldBe("forward_event");
        tools[0].ProtocolTool.InputSchema.GetProperty("properties").GetProperty("method").GetProperty("enum")
            .EnumerateArray().Select(x => x.GetString()).ShouldBe(new[] { "GET", "POST", "PUT", "DELETE" });

        var result = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        {
            ["eventName"] = "ping-test", ["method"] = "GET"
        });
        result.IsError.ShouldBe(false);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().ShouldBe(200);
        result.StructuredContent.Value.GetProperty("body").GetString()!.ShouldContain("Pong");

        var missing = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        {
            ["eventName"] = "unknown", ["method"] = "POST", ["body"] = "raw"
        });
        missing.IsError.ShouldBe(true);
        missing.StructuredContent!.Value.GetProperty("status").GetInt32().ShouldBe(404);
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
        var result = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        {
            ["eventName"] = "cloud-test", ["method"] = "POST", ["body"] = "raw text",
            ["headers"] = new Dictionary<string, string> { ["Authorization"] = "secret", ["mCp-Session-Id"] = "hidden", ["X-Event"] = "safe" }
        });
        result.IsError.ShouldBe(false);
        result.StructuredContent!.Value.GetProperty("kind").GetString().ShouldBe("published");
        result.StructuredContent.Value.GetProperty("status").GetInt32().ShouldBe(202);
        publisher.MockPublisherClient.Verify(x => x.PublishAsync(It.IsAny<PubsubMessage>()), Times.Once);
        var published = publisher.PublishedMessages.Single();
        var text = System.Text.Encoding.UTF8.GetString(published.Data.ToByteArray());
        text.ShouldContain("X-Event");
        text.ShouldNotContain("secret");
        text.ShouldNotContain("test-mcp-key");
        text.ShouldNotContain("Session-Id");
    }

    [Fact]
    public async Task RetryAcceptancePersistsWithoutCredentials()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        var storage = factory.Services.GetRequiredService<IFailedRequestStorage>();
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        var result = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        {
            ["eventName"] = "ping-fail", ["method"] = "POST", ["body"] = "{}",
            ["headers"] = new Dictionary<string, string> { ["Authorization"] = "secret" }
        });
        result.IsError.ShouldBe(false);
        result.StructuredContent!.Value.GetProperty("kind").GetString().ShouldBe("retry_accepted");
        var id = result.StructuredContent.Value.GetProperty("retryId").GetGuid();
        var stored = storage.GetAllRequests().Single(x => x.Id == id);
        stored.RequestHeaders.Keys.ShouldNotContain("Authorization");
        stored.RequestBody.ShouldBe("{}");
        storage.Remove(id);
    }

    [Fact]
    public async Task ConfiguredContentAndRequiredBodyMatchHttpRules()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        var noBody = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        { ["eventName"] = "ping-request", ["method"] = "POST" });
        noBody.IsError.ShouldBe(true);
        noBody.StructuredContent!.Value.GetProperty("status").GetInt32().ShouldBe(400);

        var configured = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        { ["eventName"] = "ping-test", ["method"] = "POST", ["body"] = "ignored" });
        configured.IsError.ShouldBe(false);
        configured.StructuredContent!.Value.GetProperty("body").GetString().ShouldBe("{\"message\":\"message-567\"}");

        var invalid = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        { ["eventName"] = "ping-request", ["method"] = "POST", ["body"] = "not json" });
        invalid.IsError.ShouldBe(true);
        invalid.StructuredContent!.Value.GetProperty("status").GetInt32().ShouldBe(400);
    }

    [Fact]
    public async Task TruncatedSuccessDoesNotBecomeToolError()
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?>
        { ["MCP_MAX_RESPONSE_BYTES"] = "5" });
        var (transport, client) = await Connect(factory);
        await using var ownedTransport = transport;
        await using var ownedClient = client;
        var result = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        { ["eventName"] = "ping-test", ["method"] = "GET" });
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
        var rule = new ForwardingRule("GET", "binary", "http://example.test/");
        forwarder.Setup(x => x.ProcessGetEvent("binary", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
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
        var result = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        { ["eventName"] = "binary", ["method"] = "GET" });
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
        using var factory = new CustomWebApplicationFactory<Program>().WithSettings(new Dictionary<string, string?> { ["MCP_ENABLED"] = "false" });
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
            Endpoint = new Uri("http://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp
        }, http);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" });
        client.SessionId.ShouldBeNull();
        routing.UseSecond = true;
        (await client.ListToolsAsync()).Count.ShouldBe(1);
        var result = await client.CallToolAsync("forward_event", new Dictionary<string, object?>
        { ["eventName"] = "ping-test", ["method"] = "GET" });
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
