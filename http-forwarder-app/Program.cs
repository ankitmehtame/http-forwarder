using System.Threading.RateLimiting;
using System.Security.Cryptography;
using System.Text;
using http_forwarder_app;
using http_forwarder_app.Cloud;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Models.Services;
using http_forwarder_app.Services;
using Microsoft.Extensions.Internal;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.AspNetCore;

var newArgs = args.ToList();
AddEnvironmentVariables(newArgs, new Dictionary<string, string> { { "VERSION", VersionUtils.InfoVersion } });

var builder = WebApplication.CreateBuilder(newArgs.ToArray());
builder.Logging.AddConsole();

builder.Services.AddControllers(options =>
{
    // Insert raw body formatter at the beginning so [FromBody] object parameters bind even when Content-Type
    // is missing/unexpected. This keeps backward compatibility for non-JSON clients.
    options.InputFormatters.Insert(0, new http_forwarder_app.Formatters.RawRequestBodyFormatter());
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithListToolsHandler((ctx, _) => ValueTask.FromResult(ForwardingMcpTools.ListTools(ctx.Services!.GetRequiredService<AppState>())))
    .WithCallToolHandler(ForwardingMcpTools.CallToolAsync);
var outboundHttpTimeout = builder.Configuration.GetOutboundHttpTimeout();
builder.Services.ConfigureHttpClientDefaults(httpClientBuilder => httpClientBuilder.ConfigureHttpClient(client => client.Timeout = outboundHttpTimeout));
builder.Services.AddHttpClient(Constants.HTTP_CLIENT_IGNORE_SSL_ERROR).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ClientCertificateOptions = ClientCertificateOption.Manual,
    ServerCertificateCustomValidationCallback =
        (httpRequestMessage, cert, cetChain, policyErrors) =>
        {
            return true;
        }
});
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo
{
    Version = VersionUtils.AssemblyVersion,
    Title = "http forwarder app",
    Description = VersionUtils.DisplayVersion
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var window = context.HttpContext.Request.Path.StartsWithSegments("/mcp")
            ? builder.Configuration.GetValue("MCP_RATE_LIMIT_WINDOW_SECONDS", 60)
            : (int)builder.Configuration.GetRateLimitWindow().TotalSeconds;
        context.HttpContext.Response.Headers.RetryAfter = window.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsync("Rate limit exceeded", cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (context.Request.Path.StartsWithSegments("/mcp")) return RateLimitPartition.GetNoLimiter("mcp");
        if (!builder.Configuration.IsRateLimitingEnabled())
        {
            return RateLimitPartition.GetNoLimiter("disabled");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            GetClientIp(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetRateLimitPerWindow(),
                Window = builder.Configuration.GetRateLimitWindow(),
                AutoReplenishment = true,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
    options.AddPolicy("mcp", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Items["McpIdentity"] as string ?? "unauthenticated",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("MCP_RATE_LIMIT_PER_WINDOW", 60),
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("MCP_RATE_LIMIT_WINDOW_SECONDS", 60)),
            AutoReplenishment = true,
            QueueLimit = 0
        }));
});
builder.Services.AddSingleton<IRestClient, RestClient>();
builder.Services.AddSingleton<AppState, AppState>();
builder.Services.AddSingleton<ForwardingRulesReader>();
builder.Services.AddSingleton<IForwardingService, ForwardingService>();
builder.Services.AddSingleton<ForwardingOrchestrator>();
builder.Services.AddSingleton<ForwardingMcpToolExecutor>();
builder.Services.AddSingleton<ForwardingMcpSchemaCache>();
builder.Services.AddSingleton<IPublisherClientFactory, PublisherClientFactory>();
builder.Services.AddSingleton<IPublishingService, PublishingService>();
builder.Services.AddSingleton<CloudMessageHandlerFactory>();
builder.Services.AddSingleton<RemoteRulePublishingService>();
builder.Services.AddSingleton<IFailedRequestStorage, FailedRequestStorage>();
builder.Services.AddHostedService<RetryBackgroundService>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<ITimeDelayService, TimeDelayService>();
builder.Services.AddHostedService<BackgroundListeningService>();

var app = builder.Build();
app.Configuration.ValidateStartupConfiguration();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Forwarder");
    c.RoutePrefix = string.Empty;
    c.InjectStylesheet("/swagger-custom.css");
});

app.UseRouting();

if (app.Configuration.GetValue<bool>("MCP_ENABLED"))
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/mcp")) { await next(); return; }
        var allowedHosts = (app.Configuration["MCP_ALLOWED_HOSTS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (allowedHosts.Length > 0 && !allowedHosts.Contains(context.Request.Host.Value, StringComparer.OrdinalIgnoreCase))
        { context.Response.StatusCode = 400; return; }
        var origin = context.Request.Headers.Origin.ToString();
        var allowedOrigins = (app.Configuration["MCP_ALLOWED_ORIGINS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!string.IsNullOrEmpty(origin) && !allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        { context.Response.StatusCode = 403; return; }
        var header = context.Request.Headers.Authorization.ToString();
        var key = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : "";
        var credentials = (app.Configuration["MCP_ALLOWED_API_KEYS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var matched = credentials.Select((credential, index) => (credential, index))
            .Where(item => CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(Encoding.UTF8.GetBytes(item.credential))))
            .Select(item => (int?)item.index).FirstOrDefault();
        if (matched is null || key.Length == 0) { context.Response.StatusCode = 401; context.Response.Headers.WWWAuthenticate = "Bearer"; return; }
        context.Items["McpIdentity"] = matched.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var maxRequestBytes = app.Configuration.GetValue("MCP_MAX_REQUEST_BYTES", 1048576);
        if (context.Request.ContentLength > maxRequestBytes)
        { context.Response.StatusCode = 413; return; }
        using var boundedBody = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxRequestBytes - (int)boundedBody.Length + 1)), context.RequestAborted)) > 0)
        {
            boundedBody.Write(buffer, 0, read);
            if (boundedBody.Length > maxRequestBytes) { context.Response.StatusCode = 413; return; }
        }
        boundedBody.Position = 0;
        context.Request.Body = boundedBody;
        await next();
    });
}

app.UseRateLimiter();

app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next();
});

app.UseAuthorization();

app.MapControllers();
if (app.Configuration.GetValue<bool>("MCP_ENABLED")) app.MapMcp("/mcp").RequireRateLimiting("mcp");

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddFile("logs/http-forwarder-{Date}.log", LogLevel.Debug);

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Environment is {environmentName}, location is {locationTag}, starting up at {time}",
     app.Environment.EnvironmentName, app.Configuration.GetLocationTag(), DateTimeOffset.Now.ToString("o"));

logger.LogInformation("Info version is {InfoVersion}, build is {BuildId}, commit is {Commit}", VersionUtils.InfoVersion, VersionUtils.BuildId, VersionUtils.Commit);
logger.LogDebug("TZ is {TZ}", TimeZoneInfo.Local.DisplayName);

var forwardingRulesReader = app.Services.GetRequiredService<ForwardingRulesReader>();
forwardingRulesReader.Init();
if (app.Configuration.GetValue<bool>("MCP_ENABLED"))
    app.Services.GetRequiredService<ForwardingMcpSchemaCache>().Initialize(app.Services.GetRequiredService<AppState>());
app.Run();

static void AddEnvironmentVariables(IList<string> existingArgsList, IDictionary<string, string> additionalEnvVars)
{
    foreach (var pair in additionalEnvVars)
    {
        Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        existingArgsList.Add("--" + pair.Key);
        existingArgsList.Add(pair.Value);
    }
}

static string GetClientIp(HttpContext context)
{
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedFor))
    {
        return forwardedFor.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "unknown";
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

public partial class Program { }
