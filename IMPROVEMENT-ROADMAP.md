# HTTP-Forwarder - Improvement Roadmap

## Executive Summary

The http-forwarder application is **well-architected and modern**, but has **3 critical bugs** and **missing production features** that should be addressed before enterprise deployment.

**Estimated effort to production-ready:** 1-2 weeks of focused development

---

## Phase 1: Critical Fixes (Must Do - Priority 1)

### Sprint 1b: High-Priority Fixes (Est. 2 days)

#### 1.5 Add Startup Configuration Validation (HIGH)
**Status:** New validation  
**File:** New file or `http-forwarder-utils/ConfigurationUtils.cs`  
**Severity:** HIGH - Configuration issue

**Add Startup Validation:**
```csharp
public static class StartupValidation
{
    public static void ValidateConfiguration(IConfiguration config)
    {
        var errors = new List<string>();
        
        if (string.IsNullOrEmpty(config.GetLocationTag()))
            errors.Add("LOCATION_TAG is required");
        
        if (config.IsPublisherEnabled() && string.IsNullOrEmpty(config.GetCloudProjectId()))
            errors.Add("GOOGLE_CLOUD_PROJECT_ID required when PUBLISHER_ENABLED=true");
        
        // Add more validations...
        
        if (errors.Any())
            throw new InvalidOperationException($"Configuration validation failed: {string.Join("; ", errors)}");
    }
}
```

**Effort:** 30 minutes  
**Testing:** Test with missing required configs

---

## Phase 2: Production Hardening (Should Do - Priority 2)

### Sprint 2a: Observability (Est. 3 days)

#### 2.1 Add Correlation IDs
**File:** New middleware in `http-forwarder-app/Middleware/CorrelationIdMiddleware.cs`

**Benefits:**
- Trace requests across services
- Better logging and debugging
- Required for enterprise observability

**Implementation:**
```csharp
public static void AddCorrelationIdMiddleware(this WebApplication app)
{
    app.Use(async (context, next) =>
    {
        var correlationId = context.Request.Headers
            .FirstOrDefault(h => h.Key == "X-Correlation-ID").Value
            .FirstOrDefault() ?? Guid.NewGuid().ToString();
        
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers.Add("X-Correlation-ID", correlationId);
        
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next();
        }
    });
}
```

**Effort:** 2 hours  
**Testing:** Verify correlation ID in response headers and logs

---

#### 2.2 Add Prometheus Metrics
**File:** New service `http-forwarder-app/Services/MetricsService.cs`

**Metrics to Track:**
- `http_forward_total` - Total requests by method, event, status
- `http_forward_duration_seconds` - Request latency histogram
- `http_retry_total` - Failed requests retried
- `http_retry_success_total` - Successful retries

**Implementation:**
```csharp
var forwardingCounter = new Counter(
    "http_forward_total",
    "Total forwarded requests",
    new[] { "method", "event", "status" }
);

var histogram = new Histogram(
    "http_forward_duration_seconds",
    "Forwarding request duration"
);
```

**Effort:** 3 hours  
**Testing:** Verify metrics in Prometheus format

---

#### 2.3 Add Request Tracing
**File:** Update `ForwardingController.cs` and services

**Add timing and tracing:**
```csharp
using var activity = new Activity("ForwardRequest").Start();
try
{
    var result = await _forwardingService.ProcessPostEvent(...);
    // Log metrics
}
finally
{
    activity?.Stop();
}
```

**Effort:** 2 hours  
**Testing:** Verify timing in logs

---

### Sprint 2b: Security (Est. 2 days)

#### 2.4 Add API Key Authentication
**File:** New middleware `http-forwarder-app/Middleware/ApiKeyMiddleware.cs`

**Recommended Implementation:**
```csharp
public class ApiKeyMiddleware
{
    public async Task InvokeAsync(HttpContext context, IConfiguration config)
    {
        var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        var validKey = config.GetValue<string>("API_KEY");
        
        if (string.IsNullOrEmpty(validKey) || apiKey != validKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }
        
        await _next(context);
    }
}
```

**Configuration:**
```json
{
  "API_KEY": "${API_KEY_SECRET}"  // From environment
}
```

**Effort:** 2 hours  
**Testing:** Test with valid/invalid keys

---

#### 2.5 Add Input Validation
**File:** Update `ForwardingController.cs`

**Validate Event Names:**
```csharp
[HttpPost("{eventName}")]
public async Task Post(
    [RegularExpression(@"^[a-zA-Z0-9_-]{1,255}$", 
        ErrorMessage = "Event name must be alphanumeric with dash/underscore")]
    string eventName)
{
    // ...
}
```

**Effort:** 1 hour  
**Testing:** Test valid and invalid event names

---

### Sprint 2c: Resilience (Est. 2 days)

#### 2.6 Fix Storage Cleanup
**File:** `http-forwarder-app/Services/FailedRequestStorage.cs`

**Add Scheduled Cleanup:**
```csharp
public async Task CleanupExpiredRequestsAsync()
{
    _lock.EnterWriteLock();
    try
    {
        var cutoff = _clock.UtcNow.AddHours(-24);
        var requests = Load()
            .Where(r => r.FirstAttempt > cutoff)
            .ToList();
        
        File.WriteAllText(_storageFile, JsonUtils.Serialize(requests, true));
    }
    finally
    {
        _lock.ExitWriteLock();
    }
}
```

**Register as Hosted Service:**
```csharp
builder.Services.AddHostedService<StorageCleanupService>();
```

**Effort:** 1.5 hours  
**Testing:** Verify old requests are cleaned up

---

#### 2.7 Add Pub/Sub Retry Logic
**File:** `http-forwarder-app/Services/BackgroundListeningService.cs`

**Implement Exponential Backoff:**
```csharp
private async Task StartWithRetryAsync(...)
{
    var retryCount = 0;
    const int maxRetries = 3;
    
    while (retryCount < maxRetries)
    {
        try
        {
            await ListenAsync(cancellationToken);
            break;
        }
        catch (Exception ex)
        {
            retryCount++;
            _logger.LogError(ex, "Pub/Sub listener failed, retry {attempt}/{max}", 
                retryCount, maxRetries);
            
            if (retryCount >= maxRetries) throw;
            
            var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
            await _timeDelayService.DelayAsync(delay);
        }
    }
}
```

**Effort:** 1.5 hours  
**Testing:** Simulate connection failures

---

#### 2.8 Add Graceful Shutdown
**File:** `http-forwarder-app/Program.cs`

**Implement Drain for In-Flight Requests:**
```csharp
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var retryService = app.Services.GetRequiredService<RetryBackgroundService>();

lifetime.ApplicationStopping.Register(async () =>
{
    _logger.LogInformation("Gracefully shutting down...");
    
    // Wait for pending retries (max 30 seconds)
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await retryService.DrainAsync(cts.Token);
    
    _logger.LogInformation("Shutdown complete");
});
```

**Effort:** 2 hours  
**Testing:** Verify no in-flight requests lost on shutdown

---

## Phase 3: Advanced Features (Nice to Have - Priority 3)

### Sprint 3a: Performance (Est. 2 days)

#### 3.1 Add Response Compression
**File:** `http-forwarder-app/Program.cs`

```csharp
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
});
app.UseResponseCompression();
```

**Effort:** 1 hour

---

#### 3.2 Add Response Caching
**File:** `http-forwarder-app/Program.cs` and models

```csharp
builder.Services.AddResponseCaching();
app.UseResponseCaching();

// Add to ForwardingRule
public record class ForwardingRule
{
    public int? CacheDurationSeconds { get; init; }
}
```

**Effort:** 2 hours

---

#### 3.3 Add Rate Limiting
**File:** `http-forwarder-app/Program.cs`

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Request.Headers["X-Api-Key"].ToString() ?? "default",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1000,
                Window = TimeSpan.FromMinutes(1)
            }));
});

app.UseRateLimiter();
```

**Effort:** 2 hours

---

### Sprint 3b: Features (Est. 3 days)

#### 3.4 Rule Hot Reload
**File:** Update `http-forwarder-app/Core/ForwardingRulesReader.cs`

**Watch for Changes:**
```csharp
public void Init()
{
    // ... existing code ...
    
    var watcher = new FileSystemWatcher(_confPath);
    watcher.Filter = "rules.json";
    watcher.Changed += (s, e) =>
    {
        _logger.LogInformation("Rules changed, reloading...");
        SetState();
    };
    watcher.EnableRaisingEvents = true;
}
```

**Effort:** 2 hours

---

#### 3.5 Request Deduplication (Idempotency)
**File:** Update models and services

**Add Idempotency Key:**
```csharp
public record class ForwardingRequest(
    string Method,
    string Event,
    string? Content,
    ImmutableSortedDictionary<string, string> RequestHeaders,
    string IdempotencyKey = ""
);
```

**Include in Retry Headers:**
```csharp
var headers = rule.MergeHeaders(requestHeaders);
if (!string.IsNullOrEmpty(request.IdempotencyKey))
    headers = headers.Add("Idempotency-Key", request.IdempotencyKey);
```

**Effort:** 3 hours

---

#### 3.6 Rule Versioning & Change Tracking
**File:** `conf/rules.json`

**Add Schema:**
```json
{
  "version": "2",
  "lastModified": "2025-05-11T00:00:00Z",
  "modifiedBy": "admin@example.com",
  "changelog": [
    {
      "version": "2",
      "changes": "Added rate limiting",
      "timestamp": "2025-05-11T00:00:00Z"
    }
  ],
  "rules": []
}
```

**Effort:** 2 hours

---

## Implementation Timeline

### Week 1: Critical Fixes + High Priority
```
Day 1:  Bug fixes (1.1-1.3) + Timeout config (1.4)
        └─ 1 hour critical bugs + 1 hour timeout = 2 hours
Day 2:  Configuration validation (1.5) + Regex fix (1.6)
        └─ 1.5 hours
Day 3:  Correlation IDs (2.1) + start Prometheus (2.2)
        └─ 2 hours correlation + 2 hours metrics = 4 hours
Day 4:  Complete Prometheus (2.2) + API Key auth (2.4)
        └─ 1 hour metrics + 2 hours auth = 3 hours
Day 5:  Storage cleanup (2.6) + Testing
        └─ 1.5 hours cleanup + testing = 2.5 hours
```

### Week 2: Resilience + Advanced
```
Day 1:  Pub/Sub retry (2.7) + Graceful shutdown (2.8)
        └─ 1.5 + 2 = 3.5 hours
Day 2:  Hot reload (3.4) + Idempotency (3.5)
        └─ 2 + 3 = 5 hours
Day 3:  Input validation (2.5) + Rate limiting (3.3)
        └─ 1 + 2 = 3 hours
Day 4:  Integration tests + Documentation
        └─ 4 hours
Day 5:  Performance testing + final QA
        └─ 4 hours
```

---

## Testing Strategy

### Phase 1 Testing (Critical Bugs)
- [ ] Unit tests for each fix
- [ ] Regression testing
- [ ] Smoke tests on demo environment

### Phase 2 Testing (Production Hardening)
- [ ] Integration tests end-to-end
- [ ] Load testing with k6 or Apache JMeter
- [ ] Security testing (injection, ReDoS)
- [ ] Resilience testing (failures, timeouts)

### Phase 3 Testing (Advanced Features)
- [ ] Performance benchmarks
- [ ] Memory profiling
- [ ] Cache effectiveness tests
- [ ] Rate limiter acceptance tests

---

## Documentation Updates

### Create/Update:
- [ ] `CRITICAL-BUGS-FIXED.md` - What was fixed and why
- [ ] `DEPLOYMENT-GUIDE.md` - How to deploy with new features
- [ ] `CONFIGURATION.md` - All configuration options
- [ ] `MONITORING.md` - Prometheus metrics and alerts
- [ ] `SECURITY.md` - Authentication, rate limiting, input validation
- [ ] `TROUBLESHOOTING.md` - Common issues and solutions

---

## Definition of Done

For each item to be considered complete:
1. ✅ Code implementation complete
2. ✅ Unit tests written and passing
3. ✅ Integration tests (if applicable) passing
4. ✅ Code review approved
5. ✅ Documentation updated
6. ✅ Manual testing completed
7. ✅ Merged to main branch

---

## Risk Assessment

### High Risk
- Storage cleanup could delete needed requests
  - **Mitigation:** Comprehensive testing, 30-day retention backup

- Graceful shutdown could lose in-flight requests
  - **Mitigation:** Test extensively, implement timeout

### Medium Risk
- Regex timeout could affect functionality
  - **Mitigation:** Validate patterns in rules, test suite

- API Key auth breaking changes
  - **Mitigation:** Backward compatibility flag, gradual rollout

### Low Risk
- Rate limiting false positives
  - **Mitigation:** Configurable limits, monitoring

---

## Success Metrics

After completion of all phases:

1. **Code Quality**
   - 0 critical bugs
   - 80%+ unit test coverage
   - All static analysis checks passing

2. **Performance**
   - P95 latency < 500ms
   - P99 latency < 2s
   - Throughput > 1000 req/sec

3. **Reliability**
   - 99.9% uptime
   - Graceful handling of failures
   - Clean logs with no errors

4. **Security**
   - 0 security issues (static analysis)
   - API authentication required
   - Input validation on all endpoints

5. **Observability**
   - All requests traced
   - Metrics collected and dashboards created
   - Alerting rules configured

---

## Next Steps

1. **Review this roadmap** with team
2. **Prioritize** based on business needs
3. **Assign owners** for each sprint
4. **Create Jira/GitHub issues** for tracking
5. **Schedule sprints** and standups
6. **Begin Week 1** with critical bug fixes

---

## Questions & Notes

For questions about specific items, refer to:
- **HTTP-FORWARDER-ANALYSIS.md** - Detailed technical analysis
- **HTTP-FORWARDER-SUMMARY.txt** - Executive summary
- Original README.md - Feature documentation
