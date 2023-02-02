ARG TARGETARCH
FROM mcr.microsoft.com/dotnet/sdk:3.1 AS base

FROM base as base-amd64
ENV RELEASE_ARCH "linux-x64"
ENV RUNTIME_IMAGE "mcr.microsoft.com/dotnet/aspnet:3.1"

FROM base as base-arm64
ENV RELEASE_ARCH "linux-arm64"
ENV RUNTIME_IMAGE "mcr.microsoft.com/dotnet/aspnet:3.1-buster-slim-arm64v8"

FROM base as base-arm
ENV RELEASE_ARCH "linux-arm"
ENV RUNTIME_IMAGE "mcr.microsoft.com/dotnet/aspnet:3.1-buster-slim-arm32v7"

FROM base-$TARGETARCH AS build-env
WORKDIR /app
EXPOSE 80
EXPOSE 443

# copy csproj and restore as distinct layers
FROM base-$TARGETARCH AS build-env
COPY *.sln .
COPY http-forwarder-app/*.csproj ./http-forwarder-app/
RUN dotnet restore -r $RELEASE_ARCH


# copy everything else and build app
FROM base-$TARGETARCH AS build-env
COPY . ./
RUN dotnet publish --no-restore -r $RELEASE_ARCH -c Release -o out --self-contained false

# Build runtime image
FROM base-$TARGETARCH AS build-env
FROM $RUNTIME_IMAGE
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]