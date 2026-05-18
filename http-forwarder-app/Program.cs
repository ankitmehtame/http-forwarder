using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.RateLimiting;
using http_forwarder_app;
using http_forwarder_app.Cloud;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using http_forwarder_app.Models.Services;
using http_forwarder_app.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;

var newArgs = args.ToList();
AddEnvironmentVariables(newArgs, new Dictionary<string, string> { { "VERSION", VersionUtils.InfoVersion } });

var builder = WebApplication.CreateBuilder(newArgs.ToArray());
builder.Logging.AddConsole();
builder.Configuration.ValidateStartupConfiguration();

builder.Services.AddControllers(options =>
{
    // Insert raw body formatter at the beginning so [FromBody] object parameters bind even when Content-Type
    // is missing/unexpected. This keeps backward compatibility for non-JSON clients.
    options.InputFormatters.Insert(0, new http_forwarder_app.Formatters.RawRequestBodyFormatter());
});
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
        context.HttpContext.Response.Headers.RetryAfter = ((int)builder.Configuration.GetRateLimitWindow().TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsync("Rate limit exceeded", cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
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
});
builder.Services.AddSingleton<IRestClient, RestClient>();
builder.Services.AddSingleton<AppState, AppState>();
builder.Services.AddSingleton<ForwardingRulesReader>();
builder.Services.AddSingleton<IForwardingService, ForwardingService>();
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

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Forwarder");
    c.RoutePrefix = string.Empty;
});

app.UseRouting();

app.UseRateLimiter();

app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next();
});

app.UseAuthorization();

app.MapControllers();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddFile("logs/http-forwarder-{Date}.log", LogLevel.Debug);

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Environment is {environmentName}, location is {locationTag}, starting up at {time}",
     app.Environment.EnvironmentName, app.Configuration.GetLocationTag(), DateTimeOffset.Now.ToString("o"));

logger.LogInformation("Info version is {InfoVersion}, build is {BuildId}, commit is {Commit}", VersionUtils.InfoVersion, VersionUtils.BuildId, VersionUtils.Commit);
logger.LogDebug("TZ is {TZ}", TimeZoneInfo.Local.DisplayName);

var forwardingRulesReader = app.Services.GetRequiredService<ForwardingRulesReader>();
forwardingRulesReader.Init();
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
