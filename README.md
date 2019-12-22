# http-forwarder
Forwards http traffic onto private network

### To build docker image
From the root directory
```
docker build -t http-forwarder-app .
```

### To run interactively
From the root directory
```
docker run -it --rm -p 5001:443 -p 5000:80 --name http-forwarder-app http-forwarder-app
```

### To save image locally
From the root directory
```
docker save -o <local path>\http-forwarder-app_v0.n.tar http-forwarder-app:v0.n
```