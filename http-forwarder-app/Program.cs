using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var initBuilder = new ConfigurationBuilder().AddCommandLine(args).AddEnvironmentVariables();
            var initConfig = initBuilder.Build();
            var sslPort = initConfig.GetValue<string>("SSL_PORT");
            var newArgs = args.ToList();
            {
                if (!string.IsNullOrEmpty(sslPort))
                {
                    System.Console.WriteLine("Setting new env variables");
                    var newAdditions = new Dictionary<string, string>
                    {
                        { "ASPNETCORE_HTTPS_PORT", sslPort },
                        { "ASPNETCORE_URLS", "https://+;http://+" },
                        { "ASPNETCORE_Kestrel__Certificates__Default__Password", Guid.NewGuid().ToString() },
                        { "ASPNETCORE_Kestrel__Certificates__Default__Path", Path.Combine(Path.GetTempPath(), "aspnetapp.pfx") }
                    };
                    foreach(var pair in newAdditions)
                    {
                        Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                        newArgs.Add("--" + pair.Key);
                        newArgs.Add(pair.Value);
                    }
                }
            };
            CreateHostBuilder(newArgs.ToArray()).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureLogging(l =>
                {
                    l.AddConsole();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
        }
    }
}
