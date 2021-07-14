FROM mcr.microsoft.com/dotnet/sdk:3.1 AS build-env
WORKDIR /app
EXPOSE 80
EXPOSE 443

# copy csproj and restore as distinct layers
COPY *.sln .
COPY http-forwarder-app/*.csproj ./http-forwarder-app/
RUN dotnet restore -r linux-x64

# copy everything else and build app
COPY . ./
RUN dotnet publish --no-restore -r linux-x64 -c Release -o out --self-contained false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:3.1
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]