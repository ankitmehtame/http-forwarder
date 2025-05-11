using http_forwarder_app.Cloud;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace http_forwarder_acceptance_tests;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(builder =>
        {
            builder.SetBasePath(Directory.GetCurrentDirectory());
            builder.AddJsonFile("appsettings.test.json");
        });

        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(s =>
        {
            s.AddTransient<HttpMessageHandlerBuilder>(sp => new TestServerHttpMessageHandlerBuilder(Server));
            s.Remove(s.Single(x => x.ImplementationType == typeof(PublisherClientFactory)));
            s.AddSingleton<IPublisherClientFactory, StubPublisherClientFactory>();
        });
    }
}
