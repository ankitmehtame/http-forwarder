using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using http_forwarder_app.Models;
using http_forwarder_app.Core;

namespace http_forwarder_app.Services;

public class FailedRequestStorage : IFailedRequestStorage, IDisposable
{
    private readonly string _storageFile;
    private readonly ReaderWriterLockSlim _lock = new();

    public FailedRequestStorage(IConfiguration configuration)
    {
        _storageFile = configuration["RetryStorage:FilePath"] ?? "failed_requests.json";
    }

    public void Store(FailedRequest request)
    {
        _lock.EnterWriteLock();
        try
        {
            var requests = Load();
            requests.Add(request);
            File.WriteAllText(_storageFile, JsonUtils.Serialize(requests, true));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public List<FailedRequest> GetPendingRequests()
    {
        _lock.EnterReadLock();
        try
        {
            return Load().Where(r => r.NextAttempt <= DateTimeOffset.UtcNow).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public List<FailedRequest> GetAllRequests()
    {
        _lock.EnterReadLock();
        try
        {
            return Load();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Remove(Guid requestId)
    {
        _lock.EnterWriteLock();
        try
        {
            var requests = Load();
            requests.RemoveAll(r => r.Id == requestId);
            File.WriteAllText(_storageFile, JsonUtils.Serialize(requests, true));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private List<FailedRequest> Load()
    {
        if (!File.Exists(_storageFile)) return new();
        var content = File.ReadAllText(_storageFile);
        return JsonUtils.Deserialize<List<FailedRequest>>(content) ?? new();
    }

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
