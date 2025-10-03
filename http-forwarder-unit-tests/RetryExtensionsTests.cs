using System;
using http_forwarder_app.Extensions;
using Shouldly;
using Xunit;

namespace http_forwarder_unit_tests;

public class RetryExtensionsTests
{
    private readonly TimeSpan _baseDelay = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _maxDelay = TimeSpan.FromHours(1);

    [Theory]
    [InlineData(1, 30)]    // 30 * 2^0 = 30
    [InlineData(2, 60)]    // 30 * 2^1 = 60
    [InlineData(3, 120)]   // 30 * 2^2 = 120
    [InlineData(4, 240)]   // 30 * 2^3 = 240
    [InlineData(5, 480)]   // 30 * 2^4 = 480
    [InlineData(6, 960)]   // 30 * 2^5 = 960
    [InlineData(7, 1920)]  // 30 * 2^6 = 1920
    [InlineData(8, 3600)]  // 30 * 2^7 = 3840, capped at 3600
    [InlineData(9, 3600)]  // 30 * 2^8 = 7680, capped at 3600
    [InlineData(10, 3600)] // Stays at max
    public void CalculateExponentialDelay_ShouldCalculateCorrectDelay(int attemptCount, int expectedDelaySeconds)
    {
        // Arrange
        var expectedDelay = TimeSpan.FromSeconds(expectedDelaySeconds);

        // Act
        var actualDelay = attemptCount.CalculateExponentialDelay(_baseDelay, _maxDelay);

        // Assert
        actualDelay.ShouldBe(expectedDelay);
    }
}