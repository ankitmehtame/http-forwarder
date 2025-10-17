using System.Collections.Immutable;
using http_forwarder_app.Models;
using Shouldly;
using Xunit;

namespace http_forwarder_unit_tests;

public class ForwardingRequestTests : IDisposable
{
    public ForwardingRequestTests()
    {
        var currentContext = GetType().Name;
        PrettyDictionary.CurrentContext = currentContext;
        PrettyDictionary.SetMaskedKeys([]);
    }

    public void Dispose()
    {
        // Cleanup static state after each test
        PrettyDictionary.ResetMaskedKeys();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Equals_WhenTwoInstancesAreSame_ReturnsTrue()
    {
        // Arrange
        var headers = ImmutableSortedDictionary<string, string>.Empty.Add("X-Test", "true").Add("Content-Type", "application/json");
        var request1 = new ForwardingRequest("GET", "test-event", "content", headers);
        var request2 = new ForwardingRequest("GET", "test-event", "content", headers.Reverse().ToImmutableSortedDictionary());

        // Act
        var result = request1.Equals(request2);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenTwoInstancesAreDifferent_ReturnsFalse()
    {
        // Arrange
        var headers1 = ImmutableSortedDictionary<string, string>.Empty.Add("X-Test", "true");
        var request1 = new ForwardingRequest("GET", "test-event", "content", headers1);

        var headers2 = ImmutableSortedDictionary<string, string>.Empty.Add("X-Test", "false");
        var request2 = new ForwardingRequest("GET", "test-event", "content", headers2);

        // Act
        var result = request1.Equals(request2);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GetHashCode_WhenTwoInstancesAreSame_ReturnsSameHashCode()
    {
        // Arrange
        var headers = ImmutableSortedDictionary<string, string>.Empty
                        .Add("X-Test", "true")
                        .Add("Content-Type", "application/json");
        var request1 = new ForwardingRequest("GET", "test-event", "content", headers);
        var request2 = new ForwardingRequest("GET", "test-event", "content", headers);

        // Act
        var hashCode1 = request1.GetHashCode();
        var hashCode2 = request2.GetHashCode();

        // Assert
        hashCode1.ShouldBe(hashCode2);
    }

    [Fact]
    public void GetHashCode_WhenTwoInstancesAreDifferent_ReturnsDifferentHashCode()
    {
        // Arrange
        var headers1 = ImmutableSortedDictionary<string, string>.Empty
                        .Add("X-Test", "true");
        var request1 = new ForwardingRequest("GET", "test-event", "content", headers1);

        var headers2 = ImmutableSortedDictionary<string, string>.Empty
                        .Add("X-Test", "false");
        var request2 = new ForwardingRequest("GET", "test-event", "content", headers2);

        // Act
        var hashCode1 = request1.GetHashCode();
        var hashCode2 = request2.GetHashCode();

        // Assert
        hashCode1.ShouldNotBe(hashCode2);
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        // Arrange
        var headers = ImmutableSortedDictionary<string, string>.Empty
                        .Add("X-Test", "true")
                        .Add("Content-Type", "application/json");
        var request = new ForwardingRequest("GET", "test-event", "content", headers);

        // Act
        var result = request.ToString();

        // Assert
        result.ShouldBe("Method = GET, Event = test-event, Content = content, RequestHeaders = [{Content-Type=application/json}, {X-Test=true}]");
    }
}
