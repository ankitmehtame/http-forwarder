namespace http_forwarder_app.Models;

public interface ITimeDelayService
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}
