FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy the central package management file first (for better layer caching)
COPY Directory.Packages.props ./
COPY Directory.Build.props ./

# Copy solution and project files
COPY *.slnx ./
COPY http-forwarder-app/*.csproj ./http-forwarder-app/
COPY http-forwarder-models/*.csproj ./http-forwarder-models/
COPY http-forwarder-utils/*.csproj ./http-forwarder-utils/
COPY http-forwarder-cloud/*.csproj ./http-forwarder-cloud/
COPY http-forwarder-unit-tests/*.csproj ./http-forwarder-unit-tests/
COPY http-forwarder-acceptance-tests/*.csproj ./http-forwarder-acceptance-tests/
COPY http-forwarder-app-function/*.csproj ./http-forwarder-app-function/

# Restore packages
RUN dotnet restore http-forwarder.slnx

# Copy everything else and build
COPY . .
RUN dotnet build http-forwarder.slnx -c Release --no-restore

RUN dotnet publish http-forwarder-app/http-forwarder-app.csproj -c Release -o out --no-build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ARG APP_BUILD_ID=local
ARG APP_COMMIT=unknown
ENV APP_BUILD_ID=${APP_BUILD_ID}
ENV APP_COMMIT=${APP_COMMIT}
COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]

ENV PORT=8080
EXPOSE 8080
