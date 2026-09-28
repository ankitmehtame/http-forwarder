[![Build](https://github.com/ankitmehtame/http-forwarder/actions/workflows/docker-image.yml/badge.svg?branch=main)](https://github.com/ankitmehtame/http-forwarder/actions/workflows/docker-image.yml)
![GHCR Image Version (latest)](https://ghcr-badge.egpl.dev/ankitmehtame/http-forwarder-app/latest_tag?color=%2344cc11&ignore=&label=version&trim=)

# http-forwarder

http-forwarder is a small HTTP proxy/forwarder intended to accept incoming HTTP requests and forward them into a private/internal network or target service. It is packaged to run easily as a Docker container and is useful for simple tunneling or edge forwarding scenarios.

## What this project does
- Listens for inbound HTTP requests on a configurable port.
- Uses JSON-based forwarding rules (`conf/rules.json`) to route requests to internal or private target URLs.
- Preserves request method, path, headers and body by default, with options to add or override headers per rule.
- Exposes API endpoints for forwarding (`/forward/{eventName}`) and a ping endpoint (`/api/ping`) for liveness checks.
- Supports remote rule publishing to Google Cloud Pub/sub for multi-instance setups.
- Applies per-client-IP rate limiting by default to protect forwarding endpoints from bursts.
- Runs as a standalone binary or inside a container.
- Includes Swagger UI at the root URL for API documentation.

## How it works (high level)
- The forwarder accepts an incoming HTTP connection on its listen port.
- It matches the request against JSON-based forwarding rules (`conf/rules.json`) using the event name and HTTP method, then performs an HTTP request to the configured backend.
- Response from the backend is proxied back to the original client, including status, headers and body.
- Rules can be tagged with location tags (e.g., "home", "cloud") so only matching instances process specific requests.
- Requests that match rules tagged for other locations can be published to Google Cloud Pub/Sub for remote processing.
- Failed requests (HTTP 5xx) with retry enabled are stored and retried automatically with exponential backoff.
- Basic logging, timeout handling, and rate limiting are applied to avoid hanging or excessive requests.
- Swagger/OpenAPI documentation is automatically generated and available at the root URL.

## Build (Docker)
From the repository root:
```
docker build -t http-forwarder-app:latest -t http-forwarder-app:0.n .
```

## Run (Docker, interactive)
```
docker run -it --rm -p 5000:8080 --name http-forwarder-app http-forwarder-app
```

## Common configuration

The application is configured through environment variables. Most routing is done through JSON rule files (`conf/rules.json`).

- `LOCATION_TAG` — **Required**. Tag used to filter which rules this instance should process (e.g., "home", "cloud"). Rules are tagged via the `tags` array in the JSON config.
- `GOOGLE_CLOUD_PROJECT_ID` — Google Cloud project ID for Pub/Sub publishing (used when `PUBLISHER_ENABLED=true`).
- `PUBLISHER_ENABLED` — If `true`, rules not matching this instance's tag are published to Pub/Sub for other instances to process.
- `PUBSUB_TOPIC_ID` / `PUBSUB_TOPIC_ID_<NAME>` — Pub/Sub topic IDs for remote publishing.
- `PUBSUB_SUBSCRIPTION_ID` / `PUBSUB_SUBSCRIPTION_ID_<NAME>` — Pub/Sub subscription IDs.
- `MASKED_HEADERS` — Comma-separated header keys to mask in logs.
- `PORT` — HTTP port to listen on (default: `8080`, also used for the container).
- `STORAGE_DIR_PATH` — Path for temporary storage of failed requests (default: `storage`).
- `RETRY_POLICY_MAX_CONCURRENCY` — Max concurrent retry attempts (default: `4`)
- `RETRY_BACKGROUND_MONITORING_ENABLED` — Enable retry monitoring (default: `true`).
- `RATE_LIMITING_ENABLED` — Enable per-client-IP rate limiting (default: `true`).
- `RATE_LIMIT_PER_WINDOW` — Number of requests allowed for each client IP in one window (default: `60`).
- `RATE_LIMIT_WINDOW_SECONDS` — Rate limit window size in seconds (default: `60`).
- `ALLOWED_API_KEYS` — **Required for the Cloud Function**. Comma-separated list of accepted API keys. Callers must send a matching key via the `X-API-Key` header or `apiKey` query parameter; otherwise the function returns `401 Unauthorized`. The API key is not republished to Pub/Sub.

Rate limiting uses the first IP in `X-Forwarded-For` when present, then falls back to the connection remote IP. Requests over the configured limit return `429 Too Many Requests` with a `Retry-After` header. This is not an allowlist or authentication mechanism; it only limits request volume.

## MCP endpoint

The ASP.NET Core app can expose `/mcp` using stateless MCP Streamable HTTP. It is off by default. Configure the following on every instance and terminate TLS at the deployment boundary:

| Setting | Meaning |
| --- | --- |
| `MCP_ENABLED` | Set `true` to map `/mcp`; otherwise requests return 404. |
| `MCP_ALLOWED_API_KEYS` | Required when enabled. Comma-separated dedicated bearer tokens, separate from Cloud Function keys. |
| `MCP_PROTOCOL_VERSION` | Optional single protocol revision to accept. Supports `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25`, or `2026-07-28`. If unset, the SDK negotiates its supported revisions. Pinning `2026-07-28` rejects older `initialize` handshakes; that revision uses `server/discover` and per-request metadata instead. |
| `MCP_BASE_URL` | Optional trusted absolute HTTP(S) base for relative rule targets. Set this to the internal forwarder address when possible. |
| `MCP_ALLOWED_HOSTS` | Required when no base URL is configured. Comma-separated exact request hosts including ports if present, such as `forwarder.example:443`. The fallback base uses the validated incoming scheme and host. Configure your reverse proxy to validate Host and set the trusted scheme; the app does not trust `X-Forwarded-Host` or `X-Forwarded-Proto`. |
| `MCP_ALLOWED_ORIGINS` | Comma-separated allowed Origin values. Requests with an Origin outside this list are rejected; requests without Origin are allowed. |
| `MCP_MAX_REQUEST_BYTES`, `MCP_MAX_RESPONSE_BYTES` | Request and returned-body byte limits, both default to 1,048,576. Must be positive and at most 16,777,216. |
| `MCP_RATE_LIMIT_PER_WINDOW`, `MCP_RATE_LIMIT_WINDOW_SECONDS` | Per-token, per-instance fixed-window quota, defaults to 60 requests per 60 seconds. Both must be positive. Rejections return 429 with `Retry-After`. |

Every MCP request, including initialization and tool discovery, needs `Authorization: Bearer <token>`. A client using the 2025-11-25 handshake can send:

```sh
curl -i https://forwarder.example/mcp \
  -H 'Authorization: Bearer YOUR_MCP_TOKEN' \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"example","version":"1.0"}}}'

curl -i https://forwarder.example/mcp \
  -H 'Authorization: Bearer YOUR_MCP_TOKEN' \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"ping_test","arguments":{}}}'
```

This curl example uses a handshake revision. With `MCP_PROTOCOL_VERSION=2026-07-28`, use a client that supports `server/discover` and per-request metadata instead; the server rejects `initialize`. Pinning an older revision rejects 2026-07-28 clients.

MCP exposes one tool per forwarding rule that explicitly opts in with an `mcp` object in `conf/rules.json`. Rules without this metadata remain available to the HTTP API but are not advertised over MCP. Each tool has a stable, unique `toolName` and description. For example, `ping_test` maps to the configured GET rule, while `ping_request` accepts a JSON object with a required string `message`:

```json
{
  "method": "POST",
  "event": "ping-request",
  "targetUrl": "/api/ping",
  "hasContent": true,
  "headers": { "Content-Type": "application/json" },
  "bodySchema": {
    "type": "object",
    "properties": { "message": { "type": "string" } },
    "required": ["message"]
  },
  "mcp": {
    "toolName": "ping_request",
    "description": "Send a ping message."
  }
}
```

`bodySchema` belongs to the forwarding rule, independent of its optional `mcp` metadata. MCP uses schema properties as tool arguments, serializes them as the request's JSON body, and validates them when the tool is called. The HTTP forwarding API does not yet validate bodies against this schema. Omitting `bodySchema` creates a zero-argument tool. For POST/PUT, set `hasContent: false`; the rule can supply fixed `content` or send an empty body. MCP never accepts a caller-supplied destination URL. Rule changes are loaded at application startup, so restart every instance after editing rules. Keep tool names, schemas and rule behavior equivalent across instances. Configured rule credentials still apply. Credentials from the MCP transport are never forwarded.

The tool returns a JSON text block and `structuredContent` with `kind`, `status`, `body`, `encoding`, `headers`, `truncated`, `bytesRead`, `maxBytes`, `retryId`, and `messageId`. `encoding` is `text` for text, JSON and XML media types (decoded as UTF-8), and `base64` for other types. A body larger than the cap is cut to `maxBytes`; `truncated` records this without changing the forwarding status. Hop-by-hop headers, `Connection`-nominated names, cookies, credentials and `MASKED_HEADERS` are omitted from results. Missing rules and content, publishing failures, and downstream 4xx/5xx responses set `isError`; 2xx and 3xx do not. `kind=retry_accepted` or `kind=published` with status 202 means accepted for later work, not delivery.

### Keepalived VIP

Deploy equivalent rules, `LOCATION_TAG`, credentials, hosts and MCP settings across nodes. Stateless MCP has no session affinity or session ID: discovery and invocation may reach different healthy instances. Failover does not move in-flight TCP connections or requests. A response can disappear after a downstream side effect completed; client retries can duplicate delivery. Use downstream idempotency keys where available. There is no exactly-once guarantee.

Failed-request storage is a local JSON file with process-local locking. Pending retries do not follow the VIP. Sharing that file between processes does not coordinate retry ownership; a distributed retry store needs a separate design.

## Docker volumes configuration
```yaml
    - './httpforwarder/conf:/app/conf:ro'
    - './httpforwarder/storage:/app/storage:rw'
    - './logs/httpforwarder:/app/logs'
    - './httpforwarder/.secrets:/app/.secrets:ro'
```
conf folder is for configuration - forwarding rules live here
storage folder is for temporary storage - up to 24 hours, after which any failed requests will be deleted

## Usage examples
- Forward a request to the configured target:
  ```
  curl -i http://localhost:5000/forward/event-name
  ```

- Check liveness (ping endpoint):
  ```
  curl -i http://localhost:5000/api/ping
  ```

- View API documentation:
  Visit http://localhost:5000/swagger in your browser

## Sample rules config
Rules are stored in `conf/rules.json`. Each rule defines how to forward a request matching a method and event name.

```json
[
    {
        "method": "GET",
        "event": "TEST",
        "targetUrl": "https://httpbin.org/get?name=test&value=123",
        "tags": ["local", "home", "cloud"]
    },
    {
        "method": "POST",
        "event": "TEST",
        "targetUrl": "https://httpbin.org/post",
        "content": "{\"name\": \"Test Person\", \"age\": 99}",
        "headers": {
            "Content-Type": "application/json"
        },
        "hasContent": false,
        "ignoreSslError": false,
        "ignoredRequestHeaders": ["X-Forwarded-For"],
        "retry": {
            "allow": true,
            "expiry": "23:59:59"
        },
        "tags": ["local", "home", "cloud"]
    }
]
```
Credit to https://httpbin.org for offering an internet based service to test REST functions.

## Retry Mechanism for Failed Requests
The application includes a robust retry mechanism for requests that fail due to server-side errors (i.e., HTTP 5xx status codes).

Enabling Retries: To enable retries for a specific rule, add the `"retry": { }` object to your rule definition, as shown in the sample above. Default is false, if there is no retry section in the rule.

How it Works: When a forwarded request fails with a server error, it is saved to the storage volume. A background service will automatically retry the request using an exponential backoff strategy.

Expiry: Failed requests will be retried for up to 24 hours by default. You can customize this by setting the expiry property (e.g., "expiry": "12:00:00" for 12 hours), however it is limited to a max of 24 hours. If a request cannot be successfully forwarded within this period, it will be discarded.

Success/Failure: If a retry attempt is successful, the request is removed from the queue. If it fails with a client error (HTTP 4xx), it is also removed, as these errors are not typically resolved by retrying.

## Notes and troubleshooting
- Ensure the container/network can reach the private target (firewalls, VPNs, and Docker network settings can block access).
- Use logs to diagnose header transformations or backend errors.
- If you need multiple backend targets or complex routing, consider using a dedicated reverse proxy (nginx, Traefik) or an API gateway.

## How I use this application
I have multiple instances running: some on my local homelab (for redundancy) and others on Google Cloud as Cloud Run functions. This setup allows mobile devices to forward calls to the cloud, which then queues the requests for an available instance to process based on location tags.

Rules can be tagged with location tags (e.g., "home", "cloud"), ensuring only matching instances process specific requests. For example:
- Home Assistant requests are processed by my local homelab instances.
- Telegram notifications can be processed by both local homelab and cloud instances for redundancy.

Rules that don't match this instance's location tag are published to Google Cloud Pub/Sub, allowing other instances to pick them up.

## Contributing
- Fixes, improvements and documentation updates are welcome via pull requests.

## License
- See repository for license information.
