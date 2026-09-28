using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using http_forwarder_app.Core;
using http_forwarder_app.Models;
using Json.Schema;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace http_forwarder_app.Services;

public sealed record ForwardingMcpResult(
    string Kind, int Status, string? Body, string Encoding, IDictionary<string, string> Headers,
    bool Truncated, int BytesRead, int MaxBytes, Guid? RetryId, string? MessageId);

public sealed class ForwardingMcpToolExecutor(ForwardingOrchestrator orchestrator, IHttpContextAccessor accessor, IConfiguration configuration)
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "X-API-Key", "Api-Key", "Host", "Content-Length", "Forwarded", "X-Forwarded-For", "X-Forwarded-Host",
        "X-Forwarded-Proto", "X-Forwarded-Port", "X-Forwarded-Prefix", "X-Original-Host", "X-Original-URL",
        "X-Rewrite-URL", "X-HTTP-Method-Override", "X-Real-IP", "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer",
        "Upgrade", "Proxy-Connection", "Set-Cookie", "Cookie"
    };

    public async Task<CallToolResult> InvokeAsync(ForwardingRule rule, string? body, CancellationToken cancellationToken)
    {
        var maxRequestBytes = configuration.GetValue("MCP_MAX_REQUEST_BYTES", 1048576);
        if (Encoding.UTF8.GetByteCount(body ?? "") > maxRequestBytes) return Error("Body exceeds MCP_MAX_REQUEST_BYTES", 400);
        var context = accessor.HttpContext!;
        var hosts = (configuration["MCP_ALLOWED_HOSTS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (hosts.Length > 0 && !hosts.Contains(context.Request.Host.Value, StringComparer.OrdinalIgnoreCase)) return Error("Host is not allowed", 400);
        var trustedBase = configuration["MCP_BASE_URL"];
        if (string.IsNullOrWhiteSpace(trustedBase))
        {
            if (!hosts.Contains(context.Request.Host.Value, StringComparer.OrdinalIgnoreCase)) return Error("Host is not allowed", 400);
            trustedBase = $"{context.Request.Scheme}://{context.Request.Host}";
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.RequestAborted);
        timeout.CancelAfter(configuration.GetOutboundHttpTimeout());
        try
        {
            var effectiveBody = rule.Mcp?.BodySchema is null && rule.Content is not null ? rule.Content : body;
            using var outcome = await orchestrator.ForwardAsync(rule.Method, rule.Event, effectiveBody, new Dictionary<string, string>(), trustedBase, timeout.Token,
                configuration.GetValue("MCP_MAX_RESPONSE_BYTES", 1048576));
            var status = outcome.StatusCode ?? (outcome.Kind == ForwardingOutcomeKind.NoBody ? 400 : 404);
            var maxBytes = configuration.GetValue("MCP_MAX_RESPONSE_BYTES", 1048576);
            var resultHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? text = null;
            var encoding = "text";
            var count = 0;
            var truncated = false;
            if (outcome.Response is { } response)
            {
                var nominated = response.Headers.Connection.SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var masked = configuration.GetMaskedHeadersValue().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var header in response.Headers.Concat(response.Content.Headers))
                    if (!Forbidden.Contains(header.Key) && !nominated.Contains(header.Key) && !masked.Contains(header.Key))
                        resultHeaders[header.Key] = string.Join(", ", header.Value);
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                var buffer = new byte[maxBytes + 1];
                while (count < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token);
                    if (read == 0) break;
                    count += read;
                }
                truncated = count > maxBytes;
                count = Math.Min(count, maxBytes);
                var media = response.Content.Headers.ContentType?.MediaType ?? "text/plain";
                var textual = media.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || media.Contains("json", StringComparison.OrdinalIgnoreCase) || media.Contains("xml", StringComparison.OrdinalIgnoreCase);
                encoding = textual ? "text" : "base64";
                text = textual ? Encoding.UTF8.GetString(buffer, 0, count) : Convert.ToBase64String(buffer, 0, count);
            }
            else text = outcome.Kind == ForwardingOutcomeKind.NoBody ? "Body not found" : "Rule not found";
            if (outcome.Kind == ForwardingOutcomeKind.RemoteRule && status == 500) text = "Publishing failed";
            var kind = outcome.RetryId is not null ? "retry_accepted" : outcome.MessageId is not null ? "published" : outcome.Kind.ToString().ToLowerInvariant();
            return Result(new ForwardingMcpResult(kind, status, text, encoding, resultHeaders, truncated, count, maxBytes, outcome.RetryId, outcome.MessageId), status >= 400);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Error("Forwarding timed out", 504); }
        catch (Exception) { return Error("Forwarding failed", 500); }
    }

    private static CallToolResult Error(string message, int status) => Result(new ForwardingMcpResult("error", status, message, "text", new Dictionary<string, string>(), false, 0, 0, null, null), true);
    internal static CallToolResult Result(ForwardingMcpResult result, bool isError)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Content = [new TextContentBlock { Text = json }]
        };
    }
}

public static class ForwardingMcpTools
{
    private static readonly JsonElement EmptyInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    });
    private static readonly JsonElement ResultSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string" },
            "status": { "type": "integer" },
            "body": { "type": ["string", "null"] },
            "encoding": { "type": "string", "enum": ["text", "base64"] },
            "headers": { "type": "object", "additionalProperties": { "type": "string" } },
            "truncated": { "type": "boolean" },
            "bytesRead": { "type": "integer" },
            "maxBytes": { "type": "integer" },
            "retryId": { "type": ["string", "null"] },
            "messageId": { "type": ["string", "null"] }
          },
          "required": ["kind", "status", "body", "encoding", "headers", "truncated", "bytesRead", "maxBytes", "retryId", "messageId"]
        }
        """).RootElement.Clone();

    public static void ValidateRules(AppState state)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in state.Rules.Concat(state.RemoteRules).Where(r => r.Mcp is not null))
        {
            var metadata = rule.Mcp!;
            if (string.IsNullOrWhiteSpace(metadata.ToolName) || !Regex.IsMatch(metadata.ToolName, "^[a-zA-Z0-9_-]{1,64}$"))
                throw new InvalidOperationException($"Invalid MCP tool name for event '{rule.Event}'");
            if (string.IsNullOrWhiteSpace(metadata.Description))
                throw new InvalidOperationException($"MCP description is required for tool '{metadata.ToolName}'");
            if (!names.Add(metadata.ToolName))
                throw new InvalidOperationException($"Duplicate MCP tool name '{metadata.ToolName}'");
            if (rule.Method is not ("POST" or "PUT" or "GET" or "DELETE"))
                throw new InvalidOperationException($"Unsupported MCP method for tool '{metadata.ToolName}'");
            var selectedRule = state.Rules.FirstOrDefault(r => r.Method == rule.Method && string.Equals(r.Event, rule.Event, StringComparison.OrdinalIgnoreCase))
                ?? state.RemoteRules.FirstOrDefault(r => r.Method == rule.Method && string.Equals(r.Event, rule.Event, StringComparison.OrdinalIgnoreCase));
            if (!ReferenceEquals(rule, selectedRule))
                throw new InvalidOperationException($"MCP tool '{metadata.ToolName}' does not match the selected forwarding rule for {rule.Method} {rule.Event}");
            if (rule.Method is "GET" or "DELETE")
            {
                if (metadata.BodySchema is not null)
                    throw new InvalidOperationException($"Tool '{metadata.ToolName}' cannot define bodySchema for {rule.Method}");
                if (state.RemoteRules.Contains(rule))
                    throw new InvalidOperationException($"Remote {rule.Method} rule '{metadata.ToolName}' cannot be exposed through MCP");
            }
            else if (metadata.BodySchema is null && rule.HasContent)
                throw new InvalidOperationException($"POST/PUT tool '{metadata.ToolName}' without bodySchema requires hasContent=false");
            if (metadata.BodySchema is { } schema)
            {
                if (rule.Method is not ("POST" or "PUT") || !rule.HasContent || rule.Content is not null)
                    throw new InvalidOperationException($"Tool '{metadata.ToolName}' bodySchema requires a POST/PUT rule without fixed content");
                if (schema.ValueKind != JsonValueKind.Object ||
                    !schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object")
                    throw new InvalidOperationException($"Tool '{metadata.ToolName}' bodySchema must declare top-level type object");
                try
                {
                    var jsonSchema = JsonSchema.FromText(schema.GetRawText());
                    _ = jsonSchema.Evaluate(JsonSerializer.SerializeToElement(new { })).IsValid;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Invalid bodySchema for MCP tool '{metadata.ToolName}'", ex);
                }
            }
        }
    }

    public static ListToolsResult ListTools(AppState state) => new()
    {
        Tools = GetRules(state).Select(rule =>
        {
            var schema = rule.Mcp!.BodySchema?.Clone() ?? EmptyInputSchema;
            return new Tool { Name = rule.Mcp.ToolName, Description = rule.Mcp.Description, InputSchema = schema, OutputSchema = ResultSchema };
        }).ToList()
    };

    public static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
    {
        var state = context.Services!.GetRequiredService<AppState>();
        var rule = GetRules(state).FirstOrDefault(x => x.Mcp!.ToolName == context.Params.Name);
        if (rule is null) return Error($"Unknown tool '{context.Params.Name}'", 404);
        var args = context.Params?.Arguments ?? new Dictionary<string, JsonElement>();
        string? body = null;
        if (rule.Mcp!.BodySchema is { } schema)
        {
            var validationSchema = JsonSchema.FromText(schema.GetRawText());
            var value = JsonSerializer.SerializeToElement(args);
            var errors = validationSchema.Evaluate(value);
            if (!errors.IsValid) return Error("Arguments do not match the configured body schema", 400);
            body = value.GetRawText();
        }
        else if (args.Count != 0) return Error("This tool does not accept arguments", 400);

        var tools = context.Services!.GetRequiredService<ForwardingMcpToolExecutor>();
        return await tools.InvokeAsync(rule, body, cancellationToken);
    }

    private static ForwardingRule[] GetRules(AppState state) => state.Rules
        .Concat(state.RemoteRules.Where(r => r.Method is "POST" or "PUT"))
        .Where(r => r.Mcp is not null)
        .OrderBy(r => r.Mcp!.ToolName, StringComparer.Ordinal).ToArray();

    private static CallToolResult Error(string message, int status)
    {
        var result = new ForwardingMcpResult("error", status, message, "text", new Dictionary<string, string>(), false, 0, 0, null, null);
        return ForwardingMcpToolExecutor.Result(result, true);
    }
}
