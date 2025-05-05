docker stop karansspl2021/blinkchatbackend
docker rm karansspl2021/blinkchatbackend
docker rmi karansspl2021/blinkchatbackend
docker build -t karansspl2021/blinkchatbackend .
docker push karansspl2021/blinkchatbackend
docker pull karansspl2021/blinkchatbackend
::docker run -d -p 5273:80 --network redis-network -v D:\docker-volume\blinkchat\backend:/app/wwwroot/models -e ASPNETCORE_ENVIRONMENT=development -e LM__licensekey=019B0C0-100235-C295A6-032129-B2D000-000525-049078-406173-9113D7-A2 -e ConnectionStrings__Redis=host.docker.internal:6379 -e ASPNETCORE_URLS=http://+:80 -e ASPNETCORE_HTTPS_PORT=5273 --name blinkchatbackend karanjangid/blinkchatbackend:latest