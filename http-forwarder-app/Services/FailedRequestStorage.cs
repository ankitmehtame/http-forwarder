using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using http_forwarder_app.Models;
using http_forwarder_app.Core;

namespace http_forwarder_app.Services;

public class FailedRequestStorage : IFailedRequestStorage, IDisposable
{
    private readonly string _storageFile;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ISystemClock _clock;

    private int _storageHash = 0;

    public FailedRequestStorage(IConfiguration configuration, ISystemClock clock)
    {
        _clock = clock;
        var storageDir = configuration.GetValidStorageDirPath() ?? throw new ArgumentNullException("Storage dir path cannot be null. Please set env variable " + Constants.STORAGE_DIR_PATH);
        if (!Directory.Exists(storageDir))
        {
            Directory.CreateDirectory(storageDir);
        }
        _storageFile = configuration.GetStorageFilePath();
    }

    public int StorageHash => _storageHash;
    public event EventHandler StorageUpdated = delegate { };

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
            var newContent = JsonUtils.Serialize(requests, true);
            File.WriteAllText(_storageFile, newContent);
            var newHash = newContent.GetHashCode();
            _storageHash = newHash;
            StorageUpdated(this, EventArgs.Empty);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public List<FailedRequest> GetRequestsDue(DateTimeOffset? asOf = null)
    {
        _lock.EnterReadLock();
        try
        {
            return Load().Where(r => r.NextAttempt <= (asOf ?? _clock.UtcNow)).ToList();
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
            var newContent = JsonUtils.Serialize(requests, true);
            File.WriteAllText(_storageFile, newContent);
            var newHash = newContent.GetHashCode();
            _storageHash = newHash;
            StorageUpdated(this, EventArgs.Empty);
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
        _storageHash = content.GetHashCode();
        return JsonUtils.Deserialize<List<FailedRequest>>(content) ?? [];
    }

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
