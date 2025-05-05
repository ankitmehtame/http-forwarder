FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
EXPOSE 80

# copy sln, csproj and restore
COPY *.sln ./
COPY http-forwarder-app/*.csproj ./http-forwarder-app/
COPY http-forwarder-models/*.csproj ./http-forwarder-models/
COPY http-forwarder-utils/*.csproj ./http-forwarder-utils/
COPY http-forwarder-unit-tests/*.csproj ./http-forwarder-unit-tests/
COPY http-forwarder-app-function/*.csproj ./http-forwarder-unit-tests/

RUN dotnet build http-forwarder.sln -c Release

RUN dotnet publish http-forwarder-app/http-forwarder-app.csproj -c Release -o out --no-build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]