using System.Linq;
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

            var rules = forwardingRulesReader.Read();
            appState.Rules = rules?.ToArray() ?? System.Array.Empty<ForwardingRule>();
        }
    }
}
