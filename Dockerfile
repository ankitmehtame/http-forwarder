ARG RELEASE_ARCH=linux-x64
ARG IMAGE_VARIANT=

FROM mcr.microsoft.com/dotnet/sdk:3.1 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# copy csproj and restore as distinct layers
COPY *.sln .
COPY http-forwarder-app/*.csproj ./http-forwarder-app/
RUN dotnet restore -r ${RELEASE_ARCH}

# copy everything else and build app
COPY . ./
RUN dotnet publish --no-restore -r ${RELEASE_ARCH} -c Release -o out --self-contained false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:3.1${IMAGE_VARIANT}
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]