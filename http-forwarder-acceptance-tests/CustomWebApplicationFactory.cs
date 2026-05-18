using http_forwarder_app.Cloud;
using http_forwarder_app.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Internal;

namespace http_forwarder_acceptance_tests;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    private IDictionary<string, string?> _settings = new Dictionary<string, string?>();

    public CustomWebApplicationFactory<TProgram> WithSettings(IDictionary<string, string?> settings)
    {
        _settings = settings;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(builder =>
        {
            builder.SetBasePath(Directory.GetCurrentDirectory());
            builder.AddJsonFile("appsettings.test.json");
            builder.AddInMemoryCollection(_settings);
        });

        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(s =>
        {
            s.Remove(s.Single(x => x.ServiceType == typeof(ISystemClock)));
            s.AddSingleton<ISystemClock, FakeClock>();
            s.AddSingleton<RequestCapturingContext>();
            s.AddTransient<RequestCapturingHandler>();
            s.ConfigureHttpClientDefaults(b =>
            {
                b.ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
                b.AddHttpMessageHandler<RequestCapturingHandler>();
            });
            s.Remove(s.Single(x => x.ImplementationType == typeof(PublisherClientFactory)));
            s.AddSingleton<IPublisherClientFactory, StubPublisherClientFactory>();
            s.Remove(s.Single(x => x.ImplementationType == typeof(RetryBackgroundService)));
            s.AddSingleton<ManualRetryBackgroundService>();
            s.AddHostedService<ManualRetryBackgroundService>();
        });
    }
}
