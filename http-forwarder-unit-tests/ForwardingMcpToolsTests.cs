using System.Text.Json;
using http_forwarder_app.Models;
using http_forwarder_app.Services;
using Shouldly;

namespace http_forwarder_unit_tests;

public class ForwardingMcpToolsTests
{
    [Fact]
    public void CatalogDependsOnLoadedRules()
    {
        var first = new AppState
        {
            Rules = [new ForwardingRule("GET", "one", "/one") { Mcp = new("read_one", "Read one") },
                new ForwardingRule("GET", "private", "/private")]
        };
        var second = new AppState
        {
            Rules = [new ForwardingRule("GET", "two", "/two") { Mcp = new("read_two", "Read two") }]
        };

        ForwardingMcpTools.ValidateRules(first);
        ForwardingMcpTools.ValidateRules(second);
        ForwardingMcpTools.ListTools(first).Tools.Select(x => x.Name).ShouldBe(["read_one"]);
        ForwardingMcpTools.ListTools(second).Tools.Select(x => x.Name).ShouldBe(["read_two"]);
    }

    [Fact]
    public void DuplicateNamesAreRejectedAtStartup()
    {
        var state = new AppState
        {
            Rules = [new ForwardingRule("GET", "one", "/one") { Mcp = new("same_tool", "Read one") }],
            RemoteRules = [new ForwardingRule("POST", "two", "/two")
            { HasContent = false, Mcp = new("same_tool", "Send two") }]
        };

        Should.Throw<InvalidOperationException>(() => ForwardingMcpTools.ValidateRules(state))
            .Message.ShouldContain("Duplicate MCP tool name");
    }

    [Fact]
    public void ToolCannotAdvertiseShadowedRule()
    {
        var state = new AppState
        {
            Rules = [new ForwardingRule("GET", "same", "/actual"),
                new ForwardingRule("GET", "same", "/shadowed") { Mcp = new("read_shadowed", "Never selected") }]
        };
        Should.Throw<InvalidOperationException>(() => ForwardingMcpTools.ValidateRules(state))
            .Message.ShouldContain("does not match the selected forwarding rule");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"type\":\"string\"}")]
    public void NonObjectBodySchemaIsRejected(string schema)
    {
        var state = new AppState
        {
            Rules = [new ForwardingRule("POST", "one", "/one")
            { Mcp = new("send_one", "Send one", JsonDocument.Parse(schema).RootElement.Clone()) }]
        };
        Should.Throw<InvalidOperationException>(() => ForwardingMcpTools.ValidateRules(state))
            .Message.ShouldContain("top-level type object");
    }

    [Fact]
    public void FixedContentRequiresNoCallerBodyAtStartup()
    {
        var state = new AppState
        {
            Rules = [new ForwardingRule("POST", "one", "/one")
            { Content = "{\"message\":\"predefined\"}", Mcp = new("send_one", "Send one") }]
        };
        Should.Throw<InvalidOperationException>(() => ForwardingMcpTools.ValidateRules(state))
            .Message.ShouldContain("hasContent=false");
    }

    [Fact]
    public void RemoteGetCannotBeAdvertisedRegardlessOfLocationTag()
    {
        var state = new AppState
        {
            RemoteRules = [new ForwardingRule("GET", "remote", "/remote")
            { Mcp = new("remote_get", "Read remote") }]
        };
        Should.Throw<InvalidOperationException>(() => ForwardingMcpTools.ValidateRules(state))
            .Message.ShouldContain("Remote GET");
    }
}
