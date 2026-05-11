using http_forwarder_app;
using http_forwarder_app.Core;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace http_forwarder_unit_tests;

public class ConfigurationExtensionsTests
{
    [Fact]
    public void GetOutboundHttpTimeout_WithoutConfiguration_ReturnsDefault()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        // Act
        var timeout = configuration.GetOutboundHttpTimeout();

        // Assert
        timeout.ShouldBe(Constants.OutboundHttpTimeoutDefault);
    }

    [Fact]
    public void GetOutboundHttpTimeout_WithPositiveConfiguration_ReturnsConfiguredValue()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.OUTBOUND_HTTP_TIMEOUT_SECONDS, "15" }
        });

        // Act
        var timeout = configuration.GetOutboundHttpTimeout();

        // Assert
        timeout.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void GetRegexMatchTimeout_WithPositiveConfiguration_ReturnsConfiguredValue()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.REGEX_MATCH_TIMEOUT_MILLISECONDS, "250" }
        });

        // Act
        var timeout = configuration.GetRegexMatchTimeout();

        // Assert
        timeout.ShouldBe(TimeSpan.FromMilliseconds(250));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void GetOutboundHttpTimeout_WithNonPositiveConfiguration_ReturnsDefault(string value)
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.OUTBOUND_HTTP_TIMEOUT_SECONDS, value }
        });

        // Act
        var timeout = configuration.GetOutboundHttpTimeout();

        // Assert
        timeout.ShouldBe(Constants.OutboundHttpTimeoutDefault);
    }

    private static IConfiguration BuildConfiguration(IDictionary<string, string?> settings)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }
}
