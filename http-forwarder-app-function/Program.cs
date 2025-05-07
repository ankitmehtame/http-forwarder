using Google.Cloud.Functions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;

namespace http_forwarder_app.Functions;

public class Program
{
    public static Task Main(string[] args)
    {
        return CreateHostBuilder(args).Build().RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrelForFunctionsFramework();
            })
            .ConfigureServices((context, services) =>
            {
                IConfiguration configuration = context.Configuration;
                services.AddSingleton(sp =>
                {
                    string? projectId = configuration.GetValue<string>("GOOGLE_CLOUD_PROJECT_ID");
                    string? topicId = configuration.GetValue<string>("PUBSUB_TOPIC_ID");

                    if (string.IsNullOrEmpty(projectId))
                    {
                        throw new InvalidOperationException("Environment variable 'GOOGLE_CLOUD_PROJECT_ID' is not set for Pub/Sub client registration.");
                    }
                    if (string.IsNullOrEmpty(topicId))
                    {
                        throw new InvalidOperationException("Environment variable 'PUBSUB_TOPIC_ID' is not set for Pub/Sub client registration.");
                    }

                    TopicName topicName = TopicName.FromProjectTopic(projectId, topicId);
                    return PublisherClient.Create(topicName);
                });
            });
}