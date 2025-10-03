[![Build](https://github.com/ankitmehtame/http-forwarder/actions/workflows/docker-image.yml/badge.svg)](https://github.com/ankitmehtame/http-forwarder/actions/workflows/docker-image.yml)
![GHCR Image Version (latest)](https://ghcr-badge.egpl.dev/ankitmehtame/http-forwarder-app/latest_tag?color=%2344cc11&ignore=&label=version&trim=)

# http-forwarder

http-forwarder is a small HTTP proxy/forwarder intended to accept incoming HTTP requests and forward them into a private/internal network or target service. It is packaged to run easily as a Docker container and is useful for simple tunneling or edge forwarding scenarios.

## What this project does
- Listens for inbound HTTP requests on a configurable port.
- Forwards those requests to an internal or private target URL (configurable).
- Preserves request method, path, headers and body by default, with options to add or override headers.
- Exposes a simple health endpoint for liveness checks.
- Runs as a standalone binary or inside a container.

## How it works (high level)
- The forwarder accepts an incoming HTTP connection on its listen port.
- It maps the request to a configured backend (single target or simple routing) and performs an HTTP request to that backend.
- Response from the backend is proxied back to the original client, including status, headers and body.
- Basic logging and timeout handling are applied to avoid hanging requests.

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
- `LISTEN_PORT` (default: 8080) — port the forwarder listens on inside the container.
- `TARGET_URL` — the backend URL to forward requests to (e.g. `http://10.0.0.5:80` or `http://service.local`).
- `TIMEOUT` — request timeout when calling the backend.
- `LOG_LEVEL` — logging verbosity (`info`, `debug`, `error`).
- `ADDITIONAL_HEADERS` — optional headers to inject when forwarding (format depends on implementation).

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

- Check health:
  ```
  curl -i http://localhost:5000/health
  ```

## Sample rules config
```json
[
    {
        "method": "GET",
        "event": "TEST",
        "targetUrl": "https://httpbin.org/get?name=test&value=123",
        "tags": [
            "local",
            "home",
            "cloud"
        ]
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
        "retry": {
            "allow": true,
            "expiry": "23:59:59"
        },
        "tags": [
            "local",
            "home",
            "cloud"
        ]
    }
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
I have multiple instances running. Few on my local homelab (for redundancy), another on Google Cloud as a Cloud Run function. This is so that some of my mobile devices can forward calls to the cloud first, which will simply queue up the requests for an instance to prcoess. Rules can be tagged as running on home or cloud, so only that type of instance will process the request.
For example, I want requests to my Home Assistant to be processed by my local homelab instance, whereas any requests to Telegram (one of my preferred notification service) can be processed in the cloud as well as at home.

## Contributing
- Fixes, improvements and documentation updates are welcome via pull requests.

## License
- See repository for license information.
