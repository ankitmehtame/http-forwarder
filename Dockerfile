FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /app
EXPOSE 80
EXPOSE 443

ARG TARGETPLATFORM

# copy csproj and restore as distinct layers
COPY *.sln .
COPY http-forwarder-app/*.csproj ./http-forwarder-app/
RUN if [ "$TARGETPLATFORM" = "linux/amd64" ]; then \
        RID=linux-x64 ; \
         
    elif [ "$TARGETPLATFORM" = "linux/arm/v8" ]; then \
        RID=linux-arm64 ; \
    elif [ "$TARGETPLATFORM" = "linux/arm/v7" ]; then \
        RID=linux-arm ; \
    fi \
    && echo "dotnet restore -r $RID" \
    && dotnet restore -r $RID
# copy everything else and build app
COPY . ./
RUN if [ "$TARGETPLATFORM" = "linux/amd64" ]; then \
        RID=linux-x64 ; \
    elif [ "$TARGETPLATFORM" = "linux/arm/v8" ]; then \
        RID=linux-arm64 ; \
    elif [ "$TARGETPLATFORM" = "linux/arm/v7" ]; then \
        RID=linux-arm ; \
    fi \
    && echo "dotnet publish --no-restore -r $RID -c Release -o out --self-contained false" \
    && dotnet publish --no-restore -r $RID -c Release -o out --self-contained false

# Build runtime image
FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "http-forwarder-app.dll"]