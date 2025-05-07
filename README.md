[![Build](https://github.com/ankitmehtame/http-forwarder/actions/workflows/docker-image.yml/badge.svg)](https://github.com/ankitmehtame/http-forwarder/actions/workflows/docker-image.yml)
![GHCR Image Version (latest)](https://ghcr-badge.egpl.dev/ankitmehtame/http-forwarder-app/latest_tag?color=%2344cc11&ignore=&label=version&trim=)

# http-forwarder
Forwards http traffic onto private network

### To build docker image
From the root directory
```
docker build -t http-forwarder-app:latest -t http-forwarder-app:0.n .
```

### To run interactively
From the root directory
```
docker run -it --rm -p 5000:8080 --name http-forwarder-app http-forwarder-app
```
