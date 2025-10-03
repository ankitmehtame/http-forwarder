using System;
using System.Threading;
using System.Threading.Tasks;
using http_forwarder_app.Models;

namespace http_forwarder_app.Services;

public class TimeDelayService : ITimeDelayService
{
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        return Task.Delay(duration, cancellationToken);
    }
}
