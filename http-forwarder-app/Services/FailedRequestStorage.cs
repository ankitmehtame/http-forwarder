using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Internal;
using http_forwarder_app.Models;
using http_forwarder_app.Core;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Services;

public class FailedRequestStorage : IFailedRequestStorage, IDisposable
{
    private readonly string _storageFile;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly IConfiguration _configuration;
    private readonly ISystemClock _clock;

    private int _storageHash = 0;
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;
    private readonly ILogger<FailedRequestStorage> _logger;



    public FailedRequestStorage(IConfiguration configuration, ISystemClock clock, ILogger<FailedRequestStorage> logger)
    {
        _configuration = configuration;
        _clock = clock;
        var storageDir = configuration.GetValidStorageDirPath() ?? throw new ArgumentNullException("Storage dir path cannot be null. Please set env variable " + Constants.STORAGE_DIR_PATH);
        if (!Directory.Exists(storageDir))
        {
            Directory.CreateDirectory(storageDir);
        }
        _storageFile = configuration.GetStorageFilePath();
        _logger = logger;
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
            var request = requests.FirstOrDefault(r => r.Id == requestId);
            requests.RemoveAll(r => r.Id == requestId);
            var newContent = JsonUtils.Serialize(requests, true);
            File.WriteAllText(_storageFile, newContent);
            var newHash = newContent.GetHashCode();
            _storageHash = newHash;
            StorageUpdated(this, EventArgs.Empty);
            if (request != null)
            {
                var archiveFile = _configuration.GetArchiveFilePath(request.Id);
                var requestContent = JsonUtils.Serialize(requests, true);
                _logger.LogInformation("Archiving request {requestId} with event {eventName} to {archiveFile}", requestId, request.Rule.Event, Path.GetFileName(archiveFile));
                File.WriteAllText(archiveFile, requestContent);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void ScheduleCleanup()
    {
        var now = _clock.UtcNow;
        if (now < (_lastCleanup + TimeSpan.FromHours(1))) return;
        RemoveOldArchives();
    }

    private void RemoveOldArchives()
    {
        var archives = _configuration.GetArchiveFilePaths();
        foreach (var archivePath in archives)
        {
            var archiveContent = File.ReadAllText(archivePath);
            try
            {
                var now = _clock.UtcNow;
                var archive = JsonUtils.Deserialize<FailedRequest>(archiveContent)!;
                var expiry = archive.FirstAttempt.Add(Constants.RetryExpiry);
                if (now >= expiry)
                {
                    _logger.LogInformation("Removing archive {archiveFile} as it has expired", Path.GetFileName(archivePath));
                    File.Delete(archivePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Removing archive {archiveFile} as it can't be loaded - {ex}", Path.GetFileName(archivePath), ex);
                File.Delete(archivePath);
            }
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
        Task.Run(ScheduleCleanup);
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
