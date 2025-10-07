using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Shouldly;

namespace http_forwarder_unit_tests;

public class SerializerTests
{
    const string simplePostJson = """
    {
        "method": "POST",
        "event": "TEST",
        "targetUrl": "http://dummy.restapiexample.com/api/v1/create"
    }
    """;

    const string contentPostJson = """
    {
        "method": "POST",
        "event": "dummy-event",
        "hasContent": false,
        "targetUrl": "https://example.com/api/dummy",
        "content": "{ \"name\":\"dummy-name\", \"type\": \"A\", \"content\": \"1.2.3.4\"}",
        "headers": {
            "Content-Type": "application/json",
            "Accept": "*/*",
            "Accept-Encoding": "gzip, deflate, br"
        },
        "retry": {
            "allow": true,
            "expiry": "12:00:00"
        }
    }
    """;

    const string defaultRetryJson = """
    {
        "method": "POST",
        "event": "dummy-event",
        "hasContent": false,
        "targetUrl": "https://example.com/api/dummy",
        "content": "{ \"name\":\"dummy-name\", \"type\": \"A\", \"content\": \"1.2.3.4\"}",
        "headers": {
            "Content-Type": "application/json",
            "Accept": "*/*",
            "Accept-Encoding": "gzip, deflate, br"
        },
        "retry": {}
    }
    """;

    const string retryExpiryJson = """
    {
        "method": "POST",
        "event": "dummy-event",
        "hasContent": false,
        "targetUrl": "https://example.com/api/dummy",
        "content": "{ \"name\":\"dummy-name\", \"type\": \"A\", \"content\": \"1.2.3.4\"}",
        "headers": {
            "Content-Type": "application/json",
            "Accept": "*/*",
            "Accept-Encoding": "gzip, deflate, br"
        },
        "retry": {
            "expiry": "00:05:00"
        }
    }
    """;

    [Fact]
    public void SerializesSimpleCorrectly()
    {
        var simplePost = JsonUtils.Deserialize<ForwardingRuleDto>(simplePostJson)?.ToForwardingRule();
        simplePost.ShouldNotBeNull();
        simplePost.Method.ShouldBe("POST");
        simplePost.Event.ShouldBe("TEST");
        simplePost.TargetUrl.ShouldBe("http://dummy.restapiexample.com/api/v1/create");
        simplePost.HasContent.ShouldBeTrue();
        simplePost.Headers.ShouldBeEmpty();
        simplePost.Retry.Allow.ShouldBeFalse();
    }

    [Fact]
    public void SerializesWithContentCorrectly()
    {
        var contentPost = JsonUtils.Deserialize<ForwardingRuleDto>(contentPostJson)?.ToForwardingRule();
        contentPost.ShouldNotBeNull();
        contentPost.Method.ShouldBe("POST");
        contentPost.Event.ShouldBe("dummy-event");
        contentPost.TargetUrl.ShouldBe("https://example.com/api/dummy");
        contentPost.HasContent.ShouldBeFalse();
        contentPost.Headers.ShouldNotBeEmpty();
        contentPost.Headers.Count.ShouldBe(3);
        contentPost.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        contentPost.Headers.ShouldContainKeyAndValue("Accept", "*/*");
        contentPost.Headers.ShouldContainKeyAndValue("Accept-Encoding", "gzip, deflate, br");
        contentPost.Retry.Allow.ShouldBeTrue();
        contentPost.Retry.Expiry.ShouldBe(new(hours: 12, minutes: 0, seconds: 0));
    }

    [Fact]
    public void SerializesWithDefaultRetryJsonCorrectly()
    {
        var contentPost = JsonUtils.Deserialize<ForwardingRuleDto>(defaultRetryJson)?.ToForwardingRule();
        contentPost.ShouldNotBeNull();
        contentPost.Retry.Allow.ShouldBeTrue();
        contentPost.Retry.Expiry.ShouldBe(new(hours: 23, minutes: 59, seconds: 59));
    }

    [Fact]
    public void SerializesWithJustRetryExpiryJsonCorrectly()
    {
        var contentPost = JsonUtils.Deserialize<ForwardingRuleDto>(retryExpiryJson)?.ToForwardingRule();
        contentPost.ShouldNotBeNull();
        contentPost.Retry.Allow.ShouldBeTrue();
        contentPost.Retry.Expiry.ShouldBe(new(hours: 0, minutes: 5, seconds: 0));
    }

    [Fact]
    public void DeserializesSerializedContentCorrectly()
    {
        var contentPost = JsonUtils.Deserialize<ForwardingRuleDto>(contentPostJson)?.ToForwardingRule();
        contentPost.ShouldNotBeNull();
        var contentPostSerialized = JsonUtils.Serialize(contentPost.ToDto(), false);
        var contentPostCloned = JsonUtils.Deserialize<ForwardingRuleDto>(contentPostSerialized)?.ToForwardingRule();
        contentPostCloned.ShouldBeEquivalentTo(contentPost);
        // Not same instance
        contentPostCloned.ShouldNotBeSameAs(contentPost);
    }
}