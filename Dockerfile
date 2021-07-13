FROM mcr.microsoft.com/dotnet/sdk:3.1 AS build-env
WORKDIR /app
EXPOSE 80
EXPOSE 443

COPY . ./
RUN bash -c 'find .'
RUN dotnet restore

RUN dotnet publish --no-restore -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:3.1
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]