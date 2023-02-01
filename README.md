[![Docker Image CI](https://github.com/ankitmehtame/http-forwarder/actions/workflows/docker-image.yml/badge.svg)](https://github.com/ankitmehtame/http-forwarder/actions/workflows/docker-image.yml)
![Docker Image Version (tag latest semver)](https://img.shields.io/docker/v/ankitmehtame/http-forwarder-app/latest?arch=amd64&color=blue&label=%20)
![Docker Image Version (tag latest semver)](https://img.shields.io/docker/v/ankitmehtame/http-forwarder-app/latest?arch=arm64&color=blue&label=%20)
![Docker Image Version (tag latest semver)](https://img.shields.io/docker/v/ankitmehtame/http-forwarder-app/latest?arch=arm&color=blue&label=%20)

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
docker run -it --rm -p 5001:443 -p 5000:80  --name http-forwarder-app http-forwarder-app
```

### To run on server with SSL
Create conf, logs and certs folders on host machine. Place ssl pem cert file (domain-cert.pem) and private key file (domain-private-key.pem) in certs folder. 
```
docker run --init -d --name="http-forwarder" -e "TZ=Asia/Singapore" -e SSL_PORT=5100 -e CERT_PATH=/app/certs/domain-crt.pem -e CERT_KEY_PATH=/app/certs/domain-private-key.pem -v <host_path_conf>:/app/conf -v <host_path_logs>:/app/logs -v <host_path_certs>:/app/certs --network bridge -p 5100:443 -p 5101:80 --restart always http-forwarder-app:0.n
```

### To save image locally
From the root directory
```
docker save -o <local path>\http-forwarder-app_0.n.tar http-forwarder-app:latest http-forwarder-app:0.n
```

### To load image
```
docker load --input <path>\http-forwarder-app_0.n.tar
```
