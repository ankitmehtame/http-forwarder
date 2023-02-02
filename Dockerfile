ARG RELEASE_ARCH="linux-x64"
ARG RUNTIME_IMAGE="mcr.microsoft.com/dotnet/aspnet:3.1"

FROM mcr.microsoft.com/dotnet/sdk:3.1 AS base
RUN date \
  && if [ "$TARGETARCH" = "arm64" ]; then RELEASE_ARCH="linux-arm64"; fi \
  && if [ "$TARGETARCH" = "arm" ]; then RELEASE_ARCH="linux-arm"; fi \
  && if [ "$TARGETARCH" = "arm64" ]; then RUNTIME_IMAGE="mcr.microsoft.com/dotnet/aspnet:3.1-buster-slim-arm64v8"; fi \
  && if [ "$TARGETARCH" = "arm" ]; then RUNTIME_IMAGE="mcr.microsoft.com/dotnet/aspnet:3.1-buster-slim-arm32v7"; fi \
  && echo "TARGETARCH=$TARGETARCH; RELEASE_ARCH=$RELEASE_ARCH; RUNTIME_IMAGE=$RUNTIME_IMAGE"

FROM base AS build-env
WORKDIR /app
EXPOSE 80
EXPOSE 443

# copy csproj and restore as distinct layers
COPY *.sln .
COPY http-forwarder-app/*.csproj ./http-forwarder-app/
RUN echo "dotnet restore -r ${RELEASE_ARCH}" \
  && dotnet restore -r ${RELEASE_ARCH}


# copy everything else and build app
COPY . ./
RUN echo "dotnet publish --no-restore -r ${RELEASE_ARCH} -c Release -o out --self-contained false" \
  && dotnet publish --no-restore -r ${RELEASE_ARCH} -c Release -o out --self-contained false

# Build runtime image
FROM ${RUNTIME_IMAGE}
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]