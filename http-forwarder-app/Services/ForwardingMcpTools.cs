using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using http_forwarder_app.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace http_forwarder_app.Services;

public enum ForwardingMcpMethod { GET, POST, PUT, DELETE }

public sealed record ForwardingMcpResult(
    string Kind, int Status, string? Body, string Encoding, IDictionary<string, string> Headers,
    bool Truncated, int BytesRead, int MaxBytes, Guid? RetryId, string? MessageId);

[McpServerToolType]
public sealed class ForwardingMcpTools(ForwardingOrchestrator orchestrator, IHttpContextAccessor accessor, IConfiguration configuration)
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "X-API-Key", "Api-Key", "Host", "Content-Length", "Forwarded", "X-Forwarded-For", "X-Forwarded-Host",
        "X-Forwarded-Proto", "X-Forwarded-Port", "X-Forwarded-Prefix", "X-Original-Host", "X-Original-URL",
        "X-Rewrite-URL", "X-HTTP-Method-Override", "X-Real-IP", "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer",
        "Upgrade", "Proxy-Connection", "Set-Cookie", "Cookie"
    };

    [McpServerTool(Name = "forward_event", UseStructuredContent = true, OutputSchemaType = typeof(ForwardingMcpResult))]
    [Description("Forward a configured event. A 202 result means accepted for retry or remote publication, not delivered.")]
    public async Task<CallToolResult> ForwardEvent(string eventName, ForwardingMcpMethod method, string? body = null,
        IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventName) || eventName.Length > 256 || eventName.Contains('/') || eventName.Contains('\\'))
            return Error("Invalid event name", 400);
        var maxRequestBytes = configuration.GetValue("MCP_MAX_REQUEST_BYTES", 1048576);
        if (Encoding.UTF8.GetByteCount(body ?? "") > maxRequestBytes) return Error("Body exceeds MCP_MAX_REQUEST_BYTES", 400);
        var safeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, "^[!#$%&'*+.^_`|~0-9A-Za-z-]+$") || value is null || value.Contains('\r') || value.Contains('\n'))
                return Error("Invalid forwarding header", 400);
            if (Forbidden.Contains(name) || name.StartsWith("Mcp-", StringComparison.OrdinalIgnoreCase)) continue;
            safeHeaders[name] = value;
        }
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
            using var outcome = await orchestrator.ForwardAsync(method.ToString(), eventName, body, safeHeaders, trustedBase, timeout.Token,
                configuration.GetValue("MCP_MAX_RESPONSE_BYTES", 1048576));
            var status = outcome.StatusCode ?? (outcome.Kind == ForwardingOutcomeKind.NoBody ? 400 : 404);
            var maxBytes = configuration.GetValue("MCP_MAX_RESPONSE_BYTES", 1048576);
            var resultHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? text = null;
            string encoding = "text";
            int count = 0;
            bool truncated = false;
            if (outcome.Response is { } response)
            {
                var nominated = response.Headers.Connection.SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var masked = (configuration.GetMaskedHeadersValue()).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error("Forwarding timed out", 504);
        }
        catch (Exception)
        {
            return Error("Forwarding failed", 500);
        }
    }

    private static CallToolResult Error(string message, int status) => Result(new ForwardingMcpResult("error", status, message, "text", new Dictionary<string, string>(), false, 0, 0, null, null), true);

    private static CallToolResult Result(ForwardingMcpResult result, bool isError)
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
