using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Shouldly;

namespace http_forwarder_unit_tests;

public class SerialzerTests
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
        }
    }
    """;

    [Fact]
    public void SerializesSimpleCorrectly()
    {
        var simplePost = JsonUtils.Deserialize<ForwardingRule>(simplePostJson);
        simplePost.ShouldNotBeNull();
        simplePost.Method.ShouldBe("POST");
        simplePost.Event.ShouldBe("TEST");
        simplePost.TargetUrl.ShouldBe("http://dummy.restapiexample.com/api/v1/create");
        simplePost.HasContent.ShouldBeTrue();
        simplePost.Headers.ShouldBeEmpty();
    }

    [Fact]
    public void SerializesWithContentCorrectly()
    {
        var contentPost = JsonUtils.Deserialize<ForwardingRule>(contentPostJson);
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
    }

    [Fact]
    public void DeserializesSerializedContentCorrectly()
    {
        var contentPost = JsonUtils.Deserialize<ForwardingRule>(contentPostJson);
        var contentPostSerialized = JsonUtils.Serialize(contentPost, false);
        var contentPostCloned = JsonUtils.Deserialize<ForwardingRule>(contentPostSerialized);
        contentPostCloned.ShouldBeEquivalentTo(contentPost);
        // Not same instance
        contentPostCloned.ShouldNotBeSameAs(contentPost);
    }
}
