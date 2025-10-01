namespace http_forwarder_app.Models;

public interface IFailedRequestStorage
{
    void Store(FailedRequest request);
    List<FailedRequest> GetPendingRequests();
    List<FailedRequest> GetAllRequests();
    void Remove(Guid requestId);
}
