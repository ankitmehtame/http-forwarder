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

    [Fact]
    public void RateLimiting_WithoutConfiguration_ReturnsEnabledDefaults()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        // Act / Assert
        configuration.IsRateLimitingEnabled().ShouldBeTrue();
        configuration.GetRateLimitPerWindow().ShouldBe(Constants.RateLimitPerWindowDefault);
        configuration.GetRateLimitWindow().ShouldBe(Constants.RateLimitWindowDefault);
    }

    [Fact]
    public void RateLimiting_WithConfiguration_ReturnsConfiguredValues()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.RATE_LIMITING_ENABLED, "false" },
            { Constants.RATE_LIMIT_PER_WINDOW, "10" },
            { Constants.RATE_LIMIT_WINDOW_SECONDS, "30" }
        });

        // Act / Assert
        configuration.IsRateLimitingEnabled().ShouldBeFalse();
        configuration.GetRateLimitPerWindow().ShouldBe(10);
        configuration.GetRateLimitWindow().ShouldBe(TimeSpan.FromSeconds(30));
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

    [Fact]
    public void ValidateStartupConfiguration_WithoutLocationTag_ShouldThrow()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var exception = Should.Throw<InvalidOperationException>(() => configuration.ValidateStartupConfiguration());

        exception.Message.ShouldContain(Constants.LOCATION_TAG);
    }

    [Fact]
    public void ValidateStartupConfiguration_WithInvalidTimeouts_ShouldThrow()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.LOCATION_TAG, "local" },
            { Constants.OUTBOUND_HTTP_TIMEOUT_SECONDS, "0" },
            { Constants.REGEX_MATCH_TIMEOUT_MILLISECONDS, "invalid" }
        });

        var exception = Should.Throw<InvalidOperationException>(() => configuration.ValidateStartupConfiguration());

        exception.Message.ShouldContain(Constants.OUTBOUND_HTTP_TIMEOUT_SECONDS);
        exception.Message.ShouldContain(Constants.REGEX_MATCH_TIMEOUT_MILLISECONDS);
    }

    [Fact]
    public void ValidateStartupConfiguration_WithInvalidRateLimitSettings_ShouldThrow()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.LOCATION_TAG, "local" },
            { Constants.RATE_LIMIT_PER_WINDOW, "0" },
            { Constants.RATE_LIMIT_WINDOW_SECONDS, "invalid" }
        });

        var exception = Should.Throw<InvalidOperationException>(() => configuration.ValidateStartupConfiguration());

        exception.Message.ShouldContain(Constants.RATE_LIMIT_PER_WINDOW);
        exception.Message.ShouldContain(Constants.RATE_LIMIT_WINDOW_SECONDS);
    }

    [Fact]
    public void ValidateStartupConfiguration_WithPublisherEnabledMissingProjectAndTopic_ShouldThrow()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.LOCATION_TAG, "local" },
            { Constants.PUBLISHER_ENABLED, "true" }
        });

        var exception = Should.Throw<InvalidOperationException>(() => configuration.ValidateStartupConfiguration());

        exception.Message.ShouldContain(Constants.CLOUD_PROJECT_ID);
        exception.Message.ShouldContain(Constants.TOPIC_ID_PREFIX);
    }

    [Fact]
    public void ValidateStartupConfiguration_WithListenerEnabledMissingSubscriptions_ShouldThrow()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.LOCATION_TAG, "local" },
            { Constants.CLOUD_PROJECT_ID, "test-project-id" },
            { Constants.LISTENER_ENABLED, "true" }
        });

        var exception = Should.Throw<InvalidOperationException>(() => configuration.ValidateStartupConfiguration());

        exception.Message.ShouldContain(Constants.GENERIC_SUBSCRIPTION_ID);
        exception.Message.ShouldContain(Constants.SUBSCRIPTION_ID_PREFIX + "LOCAL");
    }

    [Fact]
    public void ValidateStartupConfiguration_WithValidPublisherConfiguration_ShouldNotThrow()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            { Constants.LOCATION_TAG, "local" },
            { Constants.PUBLISHER_ENABLED, "true" },
            { Constants.CLOUD_PROJECT_ID, "test-project-id" },
            { Constants.TOPIC_ID_PREFIX + "CLOUD", "test-topic-id" }
        });

        Should.NotThrow(() => configuration.ValidateStartupConfiguration());
    }

    private static IConfiguration BuildConfiguration(IDictionary<string, string?> settings)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }
}
