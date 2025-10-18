namespace http_forwarder_app.Utils;

public static class TaskExtensions
{
    public static async Task IgnoreCancellation(this Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        { }
    }
}