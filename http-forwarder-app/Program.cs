using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(l =>
                {
                    l.AddConsole();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    var port = webBuilder.GetSetting("ASPNETCORE_HTTPS_PORT");
                    var certFile = webBuilder.GetSetting("CERT_PATH");
                    var certKeyFile = webBuilder.GetSetting("CERT_KEY_PATH");
                    webBuilder
                        .ConfigureKestrel((so) =>
                        {
                            if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(certFile) && !string.IsNullOrEmpty(certKeyFile))
                            {
                                var portNum = int.Parse(port);
                                so.ListenAnyIP(portNum, (lo) => lo.UseHttps(GetCertificate(certFile, certKeyFile)));
                            }
                        })
                        .UseStartup<Startup>();
                });

        private static X509Certificate2 GetCertificate(string certFile, string certKeyFile)
        {
            var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(File.ReadAllBytes(certKeyFile), out var bytesRead);
            return new X509Certificate2(certFile).CopyWithPrivateKey(rsa);
        }
    }
}
