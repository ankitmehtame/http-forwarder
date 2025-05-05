using System.Text.Json;
using Google.Cloud.Functions.Framework;
using Google.Cloud.PubSub.V1;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace http_forwarder_app.Functions;

public class Function : IHttpFunction
{
    private readonly ILogger<Function> _logger;
    private readonly PublisherClient _publisher;
    private readonly string _projectId;
    private readonly string _topicId;

    // Define environment variable names
    private const string ProjectIdEnvVar = "GOOGLE_CLOUD_PROJECT_ID";
    private const string TopicIdEnvVar = "PUBSUB_TOPIC_ID";

    public Function(ILogger<Function> logger, IConfiguration configuration)
    {
        _logger = logger;

        // Read Project ID and Topic ID from environment variables
        _projectId = configuration.GetValue<string?>(ProjectIdEnvVar) ?? string.Empty;
        _topicId = configuration.GetValue<string>(TopicIdEnvVar) ?? string.Empty;

        if (string.IsNullOrEmpty(_projectId))
        {
            _logger.LogError($"Environment variable '{ProjectIdEnvVar}' is not set.");
            throw new InvalidOperationException($"Environment variable '{ProjectIdEnvVar}' is not set.");
        }
        if (string.IsNullOrEmpty(_topicId))
        {
            _logger.LogError($"Environment variable '{TopicIdEnvVar}' is not set.");
            throw new InvalidOperationException($"Environment variable '{TopicIdEnvVar}' is not set.");
        }

        try
        {
            TopicName topicName = TopicName.FromProjectTopic(_projectId, _topicId);
            _publisher = PublisherClient.Create(topicName);
            _logger.LogInformation($"Pub/Sub PublisherClient created for topic: {topicName.ToString()}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create Pub/Sub PublisherClient: {ex.Message}");
            throw;
        }
    }

    public async Task HandleAsync(HttpContext context)
    {
        _logger.LogInformation("Received HTTP {requestMethod} request", context.Request.Method);

        // Ensure it's a POST/PUT request
        if (context.Request.Method != "POST" && context.Request.Method != "PUT")
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            await context.Response.WriteAsync("Only POST/PUT requests are allowed.");
            return;
        }

        string requestBody;
        using (var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8))
        {
            requestBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrEmpty(requestBody))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Request body is empty.");
            return;
        }

        ForwardingRule? rule;
        try
        {
            rule = JsonUtils.Deserialize<ForwardingRule>(requestBody);
            if (rule == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync($"Invalid JSON format for {nameof(ForwardingRule)} object");
                return;
            }
            _logger.LogInformation("Received rule: {rule}", rule);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError("JSON deserialization error: {errorMessage}", jsonEx.Message);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Invalid JSON format: {jsonEx.Message}");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError("An error occurred during deserialization: {errorMessage}", ex.Message);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("An internal error occurred during processing.");
            return;
        }

        try
        {
            string messageData = JsonUtils.Serialize(rule, false);
            var messageBytes = System.Text.Encoding.UTF8.GetBytes(messageData);

            string messageId = await _publisher.PublishAsync(new PubsubMessage
            {
                Data = Google.Protobuf.ByteString.CopyFrom(messageBytes),
            });

            _logger.LogInformation("Message published to Pub/Sub with ID: {messageId}", messageId);

            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync($"Message published successfully. Message ID: {messageId}");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to publish message to Pub/Sub: {errorMessage}", ex.Message);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync($"Failed to publish message: {ex.Message}");
        }
    }
}