using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

        public static readonly string InfoVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? throw new Exception("Assembly Info Version not found");
        public static readonly string AssemblyVersion = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString(3) ?? throw new Exception("Assembly Name not found");

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

            // app.UseHttpsRedirection();

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

            var certFile = Configuration.GetValue<string>("CERT_PATH");
            var certKeyFile = Configuration.GetValue<string>("CERT_KEY_PATH");

            logger.LogInformation("Looking for cert {certFile} and key {certKeyFile}", certFile, certKeyFile);
            logger.LogInformation("Cert file exists? {certFixExists} and key file exists? {certKeyFileExists}", File.Exists(certFile), File.Exists(certKeyFile));
            if (!string.IsNullOrEmpty(certFile) && !string.IsNullOrEmpty(certKeyFile))
            {
                var pfxFile = Configuration.GetValue<string>("ASPNETCORE_Kestrel__Certificates__Default__Path");
                ArgumentException.ThrowIfNullOrWhiteSpace(pfxFile, "ASPNETCORE_Kestrel__Certificates__Default__Path");
                var pfxPwd = Configuration.GetValue<string>("ASPNETCORE_Kestrel__Certificates__Default__Password");
                ArgumentException.ThrowIfNullOrWhiteSpace(pfxPwd, "ASPNETCORE_Kestrel__Certificates__Default__Password");
                GeneratePfx(certFile, certKeyFile, pfxPwd, pfxFile, logger);
            }
            forwardingRulesReader.Init();
        }

        private static void GeneratePfx(string certFile, string keyFile, string pfxPwd, string pfxFile, ILogger logger)
        {
            logger?.LogInformation("Loading ssl cert from {certFile}", certFile);
            var pemCert = GetPemCertificate(certFile, keyFile);
            var pfxBytes = pemCert.Export(X509ContentType.Pfx, pfxPwd);

            logger?.LogInformation("Writing pfx to {pfxFile}", pfxFile);

            if (File.Exists(pfxFile))
            {
                File.Delete(pfxFile);
            }
            File.WriteAllBytes(pfxFile, pfxBytes);
        }

        private static X509Certificate2 GetPemCertificate(string certFile, string certKeyFile)
        {
            var rsa = RSA.Create();
            string pemContents = File.ReadAllText(certKeyFile);
            const string RsaPrivateKeyHeader = "-----BEGIN RSA PRIVATE KEY-----";
            const string PrivateKeyHeader = "-----BEGIN PRIVATE KEY-----";

            var keyFileContents = string.Join("\n", File.ReadAllLines(certKeyFile).Where((l) => !(l.StartsWith("-----") && l.EndsWith("-----"))));
            if (pemContents.StartsWith(PrivateKeyHeader))
            {
                rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(keyFileContents), out var _);
            }
            else if (pemContents.StartsWith(RsaPrivateKeyHeader))
            {
                rsa.ImportRSAPrivateKey(Convert.FromBase64String(keyFileContents), out var _);
            }
            else
            {
                throw new InvalidOperationException($"Expected key file to start with {PrivateKeyHeader} or {RsaPrivateKeyHeader}");
            }

            return new X509Certificate2(certFile).CopyWithPrivateKey(rsa);
        }
    }
}
