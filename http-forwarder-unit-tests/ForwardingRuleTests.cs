using http_forwarder_app.Models;
using Shouldly;

namespace http_forwarder_unit_tests;

public class ForwardingRuleTests
{
    [Fact]
    public void ToString_WithNoOptionalParameters_FormatsCorrectly()
    {
        // Arrange
        var rule = new ForwardingRule(
            Method: "GET",
            Event: "test-event",
            TargetUrl: "http://example.com"
        );

        // Act
        var result = rule.ToString();

        // Assert
        result.ShouldBe("Method = GET, Event = test-event, TargetUrl = http://example.com, HasContent = True, Content = , IgnoreSslError = False, Headers = [], Tags = [], Retry = { Allow = False, Expiry = 23:59:59 }");
    }

    [Fact]
    public void ToString_WithHeadersAndTags_FormatsCorrectly()
    {
        // Arrange
        var rule = new ForwardingRule(
            Method: "POST",
            Event: "complex-event",
            TargetUrl: "http://api.example.com",
            Headers: new Dictionary<string, string> { { "X-Test", "true" }, { "Content-Type", "application/json" } },
            Tags: new HashSet<string> { "local", "test" }
        );

        // Act
        var result = rule.ToString();

        // Assert
        result.ShouldStartWith("Method = POST, Event = complex-event, TargetUrl = http://api.example.com, HasContent = True, Content = , IgnoreSslError = False, ");
        result.ShouldContain("Headers = [X-Test=true, Content-Type=application/json]");
        result.ShouldContain("Tags = [local, test]");
        result.ShouldEndWith(", Retry = { Allow = False, Expiry = 23:59:59 }");
    }

    [Fact]
    public void ToString_WithRetryEnabled_FormatsCorrectly()
    {
        // Arrange
        var rule = new ForwardingRule(
            Method: "PUT",
            Event: "retry-event",
            TargetUrl: "http://another.example.com",
            Retry: RuleRetry.AllowedDefault
        );

        // Act
        var result = rule.ToString();

        // Assert
        result.ShouldBe("Method = PUT, Event = retry-event, TargetUrl = http://another.example.com, HasContent = True, Content = , IgnoreSslError = False, Headers = [], Tags = [], Retry = { Allow = True, Expiry = 23:59:59 }");
    }
}