# http-forwarder
Forwards http traffic onto private network

### To build docker image
From the root directory
```
docker build -t http-forwarder-app .
```

### To run with interactively
From the root directory
```
docker run -it --rm -p 5001:443 -p 5000:80 --name http-forwarder-app http-forwarder-app
```
