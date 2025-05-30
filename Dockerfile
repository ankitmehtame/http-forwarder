FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# copy sln, csproj and restore
COPY *.sln ./
COPY http-forwarder-app/. ./http-forwarder-app/
COPY http-forwarder-models/. ./http-forwarder-models/
COPY http-forwarder-utils/. ./http-forwarder-utils/
COPY http-forwarder-cloud/. ./http-forwarder-cloud/
COPY http-forwarder-unit-tests/. ./http-forwarder-unit-tests/
COPY http-forwarder-acceptance-tests/. ./http-forwarder-acceptance-tests/
COPY http-forwarder-app-function/. ./http-forwarder-app-function/

RUN dotnet build http-forwarder.sln -c Release

RUN dotnet publish http-forwarder-app/http-forwarder-app.csproj -c Release -o out --no-build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]

ENV PORT=8080
EXPOSE 8080