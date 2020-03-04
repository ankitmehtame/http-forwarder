using System.IO;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddHttpClient();
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

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            loggerFactory.AddFile("logs/http-forwarder-{Date}.log");

            logger.LogInformation($"Environment is {env.EnvironmentName}");

            var certPath = System.Environment.GetEnvironmentVariable("ASPNETCORE_Kestrel__Certificates__Default__Path");
            logger.LogInformation($"Cert path is {certPath}");
            if (!string.IsNullOrEmpty(certPath))
            {
                var certDir = Path.GetDirectoryName(certPath);
                logger.LogDebug($"Cert dir is {certDir}");
                logger.LogDebug($"Cert dir exists? {Directory.Exists(certDir)}");
                if (File.Exists(certPath))
                {
                    logger.LogInformation($"Cert file exists at {certPath}");
                }
                else
                {
                    logger.LogWarning($"Cert file does not exist at {certPath}");
                    logger.LogDebug($"Files at {certDir} are: {string.Join(", ", Directory.GetFiles(certDir))}");
                }
            }
            
            forwardingRulesReader.Init();
        }
    }
}
