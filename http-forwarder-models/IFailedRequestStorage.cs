namespace http_forwarder_app.Models;

public interface IFailedRequestStorage
{
    int StorageHash { get; }
    void Store(FailedRequest request);
    List<FailedRequest> GetRequestsDue(DateTimeOffset? asOf = null);
    List<FailedRequest> GetAllRequests();
    void Remove(Guid requestId);
}
