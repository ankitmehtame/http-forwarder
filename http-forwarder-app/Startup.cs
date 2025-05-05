using System.Net.Http;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace http_forwarder_app
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public static readonly string InfoVersion = VersionUtils.InfoVersion;
        public static readonly string AssemblyVersion = VersionUtils.AssemblyVersion;

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddHttpClient().AddHttpClient(Constants.HTTP_CLIENT_IGNORE_SSL_ERROR).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback =
                (httpRequestMessage, cert, cetChain, policyErrors) =>
                {
                    return true;
                }
            });
            services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo
            {
                Version = AssemblyVersion,
                Title = "http forwarder app",
                Description = "v" + InfoVersion
            }));
            services.AddSingleton<IRestClient, RestClient>();
            services.AddSingleton<AppState, AppState>();
            services.AddSingleton<ForwardingRulesReader, ForwardingRulesReader>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory, ILogger<Startup> logger, ForwardingRulesReader forwardingRulesReader, AppState appState)
        {
            if (env.IsDevelopment())
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

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            loggerFactory.AddFile("logs/http-forwarder-{Date}.log", LogLevel.Debug);

            logger.LogInformation("Environment is {environmentName}", env.EnvironmentName);

            logger.LogInformation("Info version is {InfoVersion}", InfoVersion);

            forwardingRulesReader.Init();
        }
    }
}
