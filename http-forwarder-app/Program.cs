using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using http_forwarder_app;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

var newArgs = args.ToList();
AddEnvironmentVariables(newArgs, new Dictionary<string, string> { { "VERSION", VersionUtils.InfoVersion } });

var builder = WebApplication.CreateBuilder(newArgs.ToArray());
builder.Logging.AddConsole();

builder.Services.AddControllers();
builder.Services.AddHttpClient().AddHttpClient(Constants.HTTP_CLIENT_IGNORE_SSL_ERROR).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
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
    Description = "v" + VersionUtils.InfoVersion
}));
builder.Services.AddSingleton<IRestClient, RestClient>();
builder.Services.AddSingleton<AppState, AppState>();
builder.Services.AddSingleton<ForwardingRulesReader, ForwardingRulesReader>();

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

app.UseAuthorization();

app.MapControllers();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddFile("logs/http-forwarder-{Date}.log", LogLevel.Debug);

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Environment is {environmentName}", app.Environment.EnvironmentName);

logger.LogInformation("Info version is {InfoVersion}", VersionUtils.InfoVersion);

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