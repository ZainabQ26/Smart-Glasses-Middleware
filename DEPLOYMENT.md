# Deployment Guide

This guide covers deploying the Smart Glasses Middleware to production environments.

## Pre-Deployment Checklist

- [ ] Update `appsettings.Production.json` with production values
- [ ] Set up production PostgreSQL database
- [ ] Set up production Redis cache
- [ ] Set up production MinIO storage
- [ ] Set up production Ollama instance
- [ ] Configure CORS allowed origins
- [ ] Set up SSL certificates
- [ ] Configure logging and monitoring
- [ ] Set up backups for database and MinIO

## Production Configuration

### 1. Database Setup

Create the production database:

```sql
CREATE DATABASE smartglasses;
CREATE USER appuser WITH PASSWORD 'your-secure-password';
GRANT ALL PRIVILEGES ON DATABASE smartglasses TO appuser;
```

Update connection string in `appsettings.Production.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=your-db-host;Database=smartglasses;Username=appuser;Password=your-secure-password;SSL Mode=Require"
  }
}
```

### 2. Redis Configuration

For production, consider using Redis with authentication:

```bash
# In redis.conf
requirepass your-redis-password
```

Update connection string:
```json
{
  "ConnectionStrings": {
    "Redis": "your-redis-host:6379,password=your-redis-password,ssl=true"
  }
}
```

### 3. MinIO Configuration

Set up MinIO with proper credentials:

```json
{
  "MinIO": {
    "Endpoint": "minio.your-domain.com:9000",
    "AccessKey": "your-access-key",
    "SecretKey": "your-secret-key",
    "BucketName": "smart-glasses-media"
  }
}
```

### 4. CORS Configuration

Restrict CORS to your domains:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://your-app.com",
      "https://www.your-app.com"
    ]
  }
}
```

## Deployment Options

### Option 1: Docker Deployment

1. Create a Dockerfile:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj", "SmartGlassesMiddleware/"]
RUN dotnet restore "SmartGlassesMiddleware/SmartGlassesMiddleware.csproj"
COPY src/SmartGlassesMiddleware/. SmartGlassesMiddleware/
WORKDIR "/src/SmartGlassesMiddleware"
RUN dotnet build "SmartGlassesMiddleware.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SmartGlassesMiddleware.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmartGlassesMiddleware.dll"]
```

2. Build and run:

```bash
docker build -t smart-glasses-middleware .
docker run -d -p 80:80 -p 443:443 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  smart-glasses-middleware
```

### Option 2: Linux Server Deployment

1. Publish the application:

```bash
dotnet publish -c Release -o ./publish
```

2. Copy to server:

```bash
scp -r ./publish user@your-server:/var/www/smartglasses
```

3. Create systemd service (`/etc/systemd/system/smartglasses.service`):

```ini
[Unit]
Description=Smart Glasses Middleware
After=network.target

[Service]
WorkingDirectory=/var/www/smartglasses
ExecStart=/usr/bin/dotnet /var/www/smartglasses/SmartGlassesMiddleware.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=smartglasses
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

4. Enable and start the service:

```bash
sudo systemctl enable smartglasses
sudo systemctl start smartglasses
sudo systemctl status smartglasses
```

### Option 3: Cloud Deployment (Azure)

1. Install Azure CLI and login:

```bash
az login
```

2. Create resources:

```bash
# Create resource group
az group create --name smartglasses-rg --location eastus

# Create App Service plan
az appservice plan create --name smartglasses-plan --resource-group smartglasses-rg --sku B1 --is-linux

# Create web app
az webapp create --resource-group smartglasses-rg --plan smartglasses-plan --name smartglasses-api --runtime "DOTNETCORE:10.0"
```

3. Deploy:

```bash
dotnet publish -c Release -o ./publish
cd publish
zip -r ../deploy.zip .
cd ..

az webapp deployment source config-zip --resource-group smartglasses-rg --name smartglasses-api --src deploy.zip
```

## SSL/TLS Configuration

### Using Let's Encrypt (Linux)

```bash
sudo apt-get install certbot
sudo certbot certonly --standalone -d api.your-domain.com
```

Update your reverse proxy (nginx) configuration:

```nginx
server {
    listen 443 ssl;
    server_name api.your-domain.com;

    ssl_certificate /etc/letsencrypt/live/api.your-domain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.your-domain.com/privkey.pem;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## Monitoring and Logging

### Application Insights (Azure)

Add to `appsettings.Production.json`:

```json
{
  "ApplicationInsights": {
    "InstrumentationKey": "your-instrumentation-key"
  }
}
```

### Serilog (File and Console Logging)

Add NuGet package:
```bash
dotnet add package Serilog.AspNetCore
```

Configure in Program.cs:
```csharp
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));
```

## Health Checks

The application automatically includes health checks. Access at:
- `/health` - Basic health check

## Backup Strategy

### PostgreSQL Backup

```bash
# Daily backup script
#!/bin/bash
BACKUP_DIR="/backups/postgres"
DATE=$(date +%Y%m%d_%H%M%S)
pg_dump -h your-db-host -U appuser smartglasses > $BACKUP_DIR/smartglasses_$DATE.sql
```

### MinIO Backup

Use MinIO's built-in replication or `mc mirror`:

```bash
mc mirror minio-source/smart-glasses-media minio-backup/smart-glasses-media
```

## Security Best Practices

1. **Use Environment Variables** for sensitive data instead of config files
2. **Enable HTTPS** for all production traffic
3. **Restrict CORS** to specific domains
4. **Use Strong Passwords** for all services
5. **Keep Dependencies Updated** regularly
6. **Enable Firewall Rules** to restrict access to services
7. **Regular Security Audits** using tools like CodeQL
8. **Rate Limiting** to prevent abuse

## Performance Tuning

### Redis Configuration
```
maxmemory 2gb
maxmemory-policy allkeys-lru
```

### PostgreSQL Configuration
```
shared_buffers = 256MB
effective_cache_size = 1GB
maintenance_work_mem = 64MB
checkpoint_completion_target = 0.9
```

### Application Pool Settings (IIS)
- Maximum worker processes: 4
- Queue length: 1000
- Idle timeout: 20 minutes

## Troubleshooting

### Check Application Logs
```bash
sudo journalctl -u smartglasses -f
```

### Check Database Connection
```bash
psql -h your-db-host -U appuser -d smartglasses
```

### Check Redis Connection
```bash
redis-cli -h your-redis-host -a your-password ping
```

### Check MinIO Connection
```bash
mc alias set myminio http://your-minio-host:9000 access-key secret-key
mc ls myminio
```

## Rollback Procedure

If deployment fails:

1. Stop the service:
```bash
sudo systemctl stop smartglasses
```

2. Restore previous version:
```bash
cp -r /var/www/smartglasses.backup/* /var/www/smartglasses/
```

3. Restart the service:
```bash
sudo systemctl start smartglasses
```

## Support and Maintenance

- Monitor logs daily
- Review performance metrics weekly
- Update dependencies monthly
- Backup database daily
- Test disaster recovery quarterly
