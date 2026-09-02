# Freebuff Platform — Run & Deploy

## Local Development

### Prerequisites
- Docker Desktop running
- .NET 8 SDK
- Node.js 18+

### Start Infrastructure
```bash
cd docker
docker compose up -d
```
Starts PostgreSQL, Redis, RabbitMQ.

### Start Backend API
```bash
cd src/Freebuff.Platform.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:8080
```

### Start Frontend
```bash
cd frontend
npm run dev
```
Vite serves on port 5173. Proxy forwards `/api` to `http://localhost:8080`.

### Windows Detached (PowerShell)
```powershell
# API
$env:ASPNETCORE_ENVIRONMENT='Development'
Start-Process -FilePath 'dotnet.exe' -ArgumentList 'run','--project','src/Freebuff.Platform.Api','--urls','http://localhost:8080' -WindowStyle Hidden

# Frontend
Start-Process -FilePath 'npm.cmd' -ArgumentList 'run','dev' -WorkingDirectory 'frontend' -WindowStyle Hidden
```

## Production Deployment

### Services
| Service | Provider | URL Pattern |
|---------|----------|-------------|
| Frontend | Vercel | *.vercel.app |
| Backend API | Render | *.onrender.com |
| PostgreSQL | Neon | *.neon.tech |
| Redis | Render/Upstash | varies |
| RabbitMQ | CloudAMQP | *.cloudamqp.com |

### Deployment Order
1. **Neon** — Create PostgreSQL project, get connection string
2. **CloudAMQP** — Create RabbitMQ instance (Little Lemur plan), get AMQP URL
3. **Redis** — Create Render Redis or Upstash, get URL
4. **Render** — Deploy backend API with all env vars (see render.yaml)
5. **Vercel** — Auto-deploys from GitHub, add `VITE_API_URL` env var

### Required Environment Variables (Render)

```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
ConnectionStrings__DefaultConnection=<Neon connection string>
ConnectionStrings__Redis=<Redis URL>
ConnectionStrings__RabbitMQ=<CloudAMQP URL>
Jwt__Key=<64-char random string>
Jwt__Issuer=fms-product
Jwt__Audience=fms-product
Cors__Origins__0=https://fms-product-lakshyas-projects-c97e54f6.vercel.app
Cors__Origins__1=https://fms-product.vercel.app
```

### Required Environment Variables (Vercel)

```
VITE_API_URL=https://fms-product-api.onrender.com
```

### Login Credentials
| Role | Email | Password |
|------|-------|----------|
| SuperAdmin | admin@freebuff.com | Admin@123 |
| CompanyAdmin | admin@demofleet.com | Admin@123 |
