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
- Runs as a standalone binary or inside a container.
- Includes Swagger UI at the root URL for API documentation.

## How it works (high level)
- The forwarder accepts an incoming HTTP connection on its listen port.
- It matches the request against JSON-based forwarding rules (`conf/rules.json`) using the event name and HTTP method, then performs an HTTP request to the configured backend.
- Response from the backend is proxied back to the original client, including status, headers and body.
- Rules can be tagged with location tags (e.g., "home", "cloud") so only matching instances process specific requests.
- Requests that match rules tagged for other locations can be published to Google Cloud Pub/Sub for remote processing.
- Failed requests (HTTP 5xx) with retry enabled are stored and retried automatically with exponential backoff.
- Basic logging and timeout handling are applied to avoid hanging requests.
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
