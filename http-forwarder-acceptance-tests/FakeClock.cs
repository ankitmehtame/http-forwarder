using Microsoft.Extensions.Internal;

namespace http_forwarder_acceptance_tests;

public class FakeClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + AdvancedBy;

    public TimeSpan AdvancedBy { get; private set; } = TimeSpan.Zero;

    public void AddTime(TimeSpan duration)
    {
        AdvancedBy += duration;
    }
}