using Google.Cloud.Functions.Hosting;
using http_forwarder_app.Cloud;
using http_forwarder_app.Models.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace http_forwarder_app.Functions;

public class Startup : FunctionsStartup
{
    private const string OriginPolicy = "AllowAnyOrigin";

    public override void ConfigureServices(WebHostBuilderContext context, IServiceCollection services)
    {
        IConfiguration configuration = context.Configuration;

        services.AddCors(o => o.AddPolicy(OriginPolicy, b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        services.AddSingleton<IPublisherClientFactory, PublisherClientFactory>();
        services.AddSingleton<IPublishingService, PublishingService>();

        base.ConfigureServices(context, services);
    }

    public override void Configure(WebHostBuilderContext context, IApplicationBuilder app)
    {
        app.UseCors(OriginPolicy); // Apply the CORS policy

        base.Configure(context, app);
    }
}