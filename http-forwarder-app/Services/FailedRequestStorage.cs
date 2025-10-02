using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using http_forwarder_app.Models;
using http_forwarder_app.Core;
using Google.Protobuf.WellKnownTypes;

namespace http_forwarder_app.Services;

public class FailedRequestStorage : IFailedRequestStorage, IDisposable
{
    private readonly string _storageFile;
    private readonly ReaderWriterLockSlim _lock = new();

    public FailedRequestStorage(IConfiguration configuration)
    {
        var storageDir = configuration.GetStorageDirPath() ?? throw new ArgumentNullException("Storage dir path cannot be null. Please set it at RetryStorage:DirPath");
        if (!Directory.Exists(storageDir))
        {
            Directory.CreateDirectory(storageDir);
        }
        _storageFile = configuration.GetStorageFilePath();
    }

    public void Store(FailedRequest request)
    {
        _lock.EnterWriteLock();
        try
        {
            var requests = Load();
            var index = requests.FindIndex(r => r.Id == request.Id);
            if (index >= 0)
            {
                requests[index] = request;
                var lastIndex = index;
                do
                {
                    lastIndex = requests.FindLastIndex(r => r.Id == request.Id);
                    if (lastIndex > index)
                    {
                        requests.RemoveAt(lastIndex);
                    }
                } while (lastIndex > index);
            }
            else
            {
                requests.Add(request);
            }
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
        if (!File.Exists(_storageFile))
        {
            var newContent = new List<FailedRequest>();
            File.WriteAllText(_storageFile, JsonUtils.Serialize(newContent, true));
            return newContent;
        }
        var content = File.ReadAllText(_storageFile);
        return JsonUtils.Deserialize<List<FailedRequest>>(content) ?? new();
    }

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
