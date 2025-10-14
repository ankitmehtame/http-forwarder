using System.Collections.Immutable;
using http_forwarder_app.Models;
using Shouldly;
using Xunit.Repeat;

namespace http_forwarder_unit_tests;

public class ForwardingRuleTests
{
    [Fact]
    [Repeat(10)]
    public void ToString_WithNoOptionalParameters_FormatsCorrectly()
    {
        // Arrange
        var rule = new ForwardingRule(
            method: "GET",
            @event: "test-event",
            targetUrl: "http://example.com"
        );

        // Act
        var result = rule.ToString();

        // Assert
        result.ShouldBe("Method = GET, Event = test-event, TargetUrl = http://example.com, HasContent = True, Content = , IgnoreSslError = False, Headers = [], Tags = [], Retry = { Allow = False, Expiry = 23:59:59 }");
    }

    [Fact]
    [Repeat(10)]
    public void ToString_WithHeadersAndTags_FormatsCorrectly()
    {
        // Arrange
        var rule = new ForwardingRule(
            method: "POST",
            @event: "complex-event",
            targetUrl: "http://api.example.com")
        {
            Headers = new Dictionary<string, string> { { "X-Test", "true" }, { "Content-Type", "application/json" } }.ToImmutableDictionary(),
            Tags = ["local", "test"]
        };

        // Act
        var result = rule.ToString();

        // Assert
        rule.Headers.ShouldNotBeEmpty();
        rule.Headers.Count.ShouldBe(2);
        result.ShouldStartWith("Method = POST, Event = complex-event, TargetUrl = http://api.example.com, HasContent = True, Content = , IgnoreSslError = False, ");
        result.ShouldContain("Headers = [Content-Type=application/json, X-Test=true]", customMessage: result);
        result.ShouldContain("Tags = [local, test]", customMessage: result);
        result.ShouldEndWith(", Retry = { Allow = False, Expiry = 23:59:59 }");
    }

    [Fact]
    [Repeat(10)]
    public void ToString_WithRetryEnabled_FormatsCorrectly()
    {
        // Arrange
        var rule = new ForwardingRule(
            method: "PUT",
            @event: "retry-event",
            targetUrl: "http://another.example.com")
        {
            Retry = RuleRetry.AllowedDefault
        };

        // Act
        var result = rule.ToString();

        // Assert
        result.ShouldBe("Method = PUT, Event = retry-event, TargetUrl = http://another.example.com, HasContent = True, Content = , IgnoreSslError = False, Headers = [], Tags = [], Retry = { Allow = True, Expiry = 23:59:59 }");
    }

    [Fact]
    [Repeat(10)]
    public void Minimal_ToString_WithTags_FormatsCorrectly()
    {
        // Arrange
        var rule = new ForwardingRule(
            method: "POST",
            @event: "complex-event",
            targetUrl: "http://api.example.com")
        {
            Headers = new Dictionary<string, string> { { "X-Test", "true" }, { "Content-Type", "application/json" } }.ToImmutableDictionary(),
            Tags = ["local", "test"]
        }.ToMinimal();

        // Act
        var result = rule.ToString();

        // Assert
        result.ShouldBe("Method = POST, Event = complex-event, Tags = [local, test]");
    }



    [Fact]
    [Repeat(10)]
    public void MinimalEnumerable_ToString_FormatsCorrectly()
    {
        // Arrange
        var rule1 = new ForwardingRule(
            method: "POST",
            @event: "complex-event",
            targetUrl: "http://api.example.com")
        {
            Headers = new Dictionary<string, string> { { "X-Test", "true" }, { "Content-Type", "application/json" } }.ToImmutableDictionary(),
            Tags = ["local", "test"]
        };
        var rule2 = new ForwardingRule(
            method: "GET",
            @event: "complex-get-event",
            targetUrl: "http://api2.example.com")
        {
            Headers = new Dictionary<string, string> { { "X-Test2", "false" } }.ToImmutableDictionary(),
            Tags = ["local", "cloud"]
        };
        IEnumerable<ForwardingRule> rules = [rule1, rule2];

        // Act
        var result = rules.PrintMinimal();

        // Assert
        result.ShouldBe("[{Method = POST, Event = complex-event, Tags = [local, test]}, {Method = GET, Event = complex-get-event, Tags = [cloud, local]}]");
    }

    [Fact]
    public void MergeHeaders_WhenDestinationHeadersAreEmpty_ReturnsSourceHeaders()
    {
        // Arrange
        var rule = new ForwardingRule("GET", "test", "http://a.com")
        {
            Headers = new Dictionary<string, string> { { "X-Source", "source" } }.ToImmutableDictionary()
        };
        Dictionary<string, string> requestHeaders = [];

        // Act
        var merged = rule.MergeHeaders(requestHeaders);

        // Assert
        merged.ShouldBe(rule.Headers);
    }

    [Fact]
    public void MergeHeaders_WhenSourceHeadersAreEmpty_ReturnsDestinationHeaders()
    {
        // Arrange
        var rule = new ForwardingRule("GET", "test", "http://a.com");
        var requestHeaders = new Dictionary<string, string> { { "X-Request", "request" } };

        // Act
        var merged = rule.MergeHeaders(requestHeaders);

        // Assert
        merged.ShouldBe(requestHeaders.ToImmutableDictionary());
    }

    [Fact]
    public void MergeHeaders_WhenBothHeadersAreNonEmpty_MergesHeadersCorrectly()
    {
        // Arrange
        var rule = new ForwardingRule("GET", "test", "http://a.com")
        {
            Headers = new Dictionary<string, string> { { "X-Source", "source" } }.ToImmutableDictionary()
        };
        var requestHeaders = new Dictionary<string, string> { { "X-Request", "request" } };

        // Act
        var merged = rule.MergeHeaders(requestHeaders);

        // Assert
        var expected = new Dictionary<string, string>
        {
            { "X-Source", "source" },
            { "X-Request", "request" }
        }.ToImmutableDictionary();
        merged.ShouldBe(expected);
    }

    [Fact]
    public void MergeHeaders_WithOverlappingHeaders_SourceOverwritesDestination()
    {
        // Arrange
        var rule = new ForwardingRule("GET", "test", "http://a.com")
        {
            Headers = new Dictionary<string, string> { { "X-Common", "source" } }.ToImmutableDictionary()
        };
        var requestHeaders = new Dictionary<string, string> { { "X-Common", "request" }, { "X-Request", "request" } };

        // Act
        var merged = rule.MergeHeaders(requestHeaders);

        // Assert
        var expected = new Dictionary<string, string>
        {
            { "X-Common", "request" },
            { "X-Request", "request" }
        }.ToImmutableDictionary();
        merged.ShouldBe(expected);
    }

    [Fact]
    public void MergeHeaders_WithContentTypeAndNoContent_ExcludesContentType()
    {
        // Arrange
        var rule = new ForwardingRule("GET", "test", "http://a.com")
        {
            HasContent = false,
            Headers = new Dictionary<string, string> { { "X-Source", "source" }, { "Content-Type", "text/plain" } }.ToImmutableDictionary()
        };
        var requestHeaders = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "X-Request", "request" } };

        // Act
        var merged = rule.MergeHeaders(requestHeaders);

        // Assert
        var expected = new Dictionary<string, string>
        {
            { "X-Source", "source" },
            { "X-Request", "request" },
            { "Content-Type", "text/plain" }
        }.ToImmutableDictionary();
        merged.ShouldBe(expected);
    }

    [Fact]
    public void MergeHeaders_WithContentTypeAndContent_IncludesContentType()
    {
        // Arrange
        var rule = new ForwardingRule("POST", "test", "http://a.com")
        {
            HasContent = true,
            Headers = new Dictionary<string, string> { { "X-Source", "source" }, { "Content-Type", "text/plain" } }.ToImmutableDictionary()
        };
        var requestHeaders = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "X-Request", "request" } };

        // Act
        var merged = rule.MergeHeaders(requestHeaders);

        // Assert
        var expected = new Dictionary<string, string>
        {
            { "X-Source", "source" },
            { "Content-Type", "application/json" },
            { "X-Request", "request" }
        }.ToImmutableDictionary();
        merged.ShouldBe(expected);
    }
}