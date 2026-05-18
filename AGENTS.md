# AGENTS.md

## Commands
- Use the root `http-forwarder.slnx`; there is no `.sln` on current mainline.
- CI order is `dotnet restore`, `dotnet build --configuration Release --no-restore`, then `dotnet test --configuration Release --no-build --verbosity normal`.
- Fast full verification: `dotnet test http-forwarder.slnx`.
- Focused project tests: `dotnet test http-forwarder-unit-tests/http-forwarder-unit-tests.csproj` or `dotnet test http-forwarder-acceptance-tests/http-forwarder-acceptance-tests.csproj`.
- Focused single test: `dotnet test http-forwarder-unit-tests/http-forwarder-unit-tests.csproj --filter FullyQualifiedName~ForwardingRuleTests`.
- Build a local Docker image with near-CI version metadata via `./build-local-docker.sh`; plain `docker build .` falls back to `0.0.0-local` because `.git` is excluded from the Docker context.
- `Dockerfile` publishes the ASP.NET Core forwarder app; `Dockerfile.cloudfunction` publishes only `http-forwarder-app-function` for the Cloud Function host and is not used by the current GitHub Actions workflow.

## Project Shape
- Main ASP.NET Core app entrypoint is `http-forwarder-app/Program.cs`; forwarding routes are in `http-forwarder-app/Controllers/ForwardingController.cs` under `/forward/{eventName}` and `/api/forward/{eventName}`.
- Cloud Function entrypoint is `http-forwarder-app-function/Function.cs`; it only accepts `POST`/`PUT` and filters events with `ALLOWED_EVENTS`.
- Shared DTOs/rules live in `http-forwarder-models`; config/path helpers live in `http-forwarder-utils`; Pub/Sub publishing lives in `http-forwarder-cloud`.
- Package versions are centrally managed in `Directory.Packages.props`; keep versions out of individual `.csproj` `PackageReference`s.
- Version stamping uses Nerdbank.GitVersioning from `Directory.Build.props` and `version.json`; Docker builds receive NBGV values from CI build args.

## Runtime Gotchas
- Startup validates `LOCATION_TAG`, positive timeout settings, and Pub/Sub settings when publisher/listener modes are enabled.
- Local config comes from normal ASP.NET Core config (`appsettings.json`, `appsettings.Development.json`, environment variables); there is no repo `.env` loader.
- Default `appsettings.json` keeps `PUBLISHER_ENABLED=false` and `LISTENER_ENABLED=false`; turning either on locally requires real Google credentials plus the matching Pub/Sub project/topic/subscription settings.
- Forwarding rules are loaded from `conf/rules.json`; only rules whose `tags` contain `LOCATION_TAG` are processed locally, and non-matching rules are treated as remote publish candidates.
- Relative rule `targetUrl` values are resolved against the incoming request host, which acceptance tests use to loop back into the in-memory test server.
- Failed 5xx forwards with `retry.allow=true` are persisted under `storage/` or `STORAGE_DIR_PATH`; tests may delete `storage.json` before retry assertions.
- `.vscode/launch.json` is stale (`netcoreapp3.1` path); prefer `dotnet run --project http-forwarder-app/http-forwarder-app.csproj` or the test commands above.

## Tests
- Acceptance tests replace outbound `HttpClient` handlers with the test server and `RequestCapturingHandler`; they should not require real network backends.
- Acceptance tests also stub Pub/Sub via `StubPublisherClientFactory` and replace retry timing with `FakeClock`/`ManualRetryBackgroundService`.
- `appsettings.test.json` enables publisher mode but disables listener and retry background monitoring; update this file when startup validation requirements change.

## Style
- No repo `.editorconfig`, ruleset, or StyleCop config is present; use the existing C# style in touched files.
- `dotnet format http-forwarder.slnx --verify-no-changes --verbosity minimal` is the available formatting check when style risk is high.
- Nullable reference types and implicit usings are enabled across projects.

## CI And Containers
- GitHub Actions uses .NET SDK `10.0`, runs Release build/tests, then builds multi-arch GHCR images with Buildx.
- Main/master image tags are semver plus `latest`; other branches get `rc-` tags.
- `.dockerignore` intentionally excludes `.git`; keep Docker version metadata supplied via build args or `build-local-docker.sh`.
- No separate CI/CD workflow for `Dockerfile.cloudfunction` exists in this repo; if changing it, verify manually with `docker build -f Dockerfile.cloudfunction .`.
