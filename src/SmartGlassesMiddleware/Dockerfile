# ── Stage 1: Build the React SPA ─────────────────────────────────────────────
# Uses a lightweight Node image. Vite is configured (outDir: '../wwwroot') to
# write the production bundle one directory above the client folder, so the
# output lands at /app/wwwroot/ inside this stage.
FROM node:25-alpine AS frontend-build
# Upgrade all Alpine packages to pick up any patched CVEs
RUN apk upgrade --no-cache
WORKDIR /app/client

COPY src/SmartGlassesMiddleware/client/package*.json ./
RUN npm ci --prefer-offline

COPY src/SmartGlassesMiddleware/client/ ./
RUN npm run build
# Output is now at /app/wwwroot/ (../wwwroot relative to WORKDIR)

# ── Stage 2: Build the .NET API ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0.102 AS build
WORKDIR /src

# Cache-bust: force layer rebuild to pick up ProcessController single-constructor fix
ARG CACHE_BUST=2026-04-05

# Restore dependencies as a distinct cached layer
COPY ["src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj", "src/SmartGlassesMiddleware/"]
RUN dotnet restore "src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj"

# Copy all source files
COPY . .

# Overlay the React build output onto the source wwwroot directory.
# This preserves admin.html, monitoring.html etc. that already live in wwwroot,
# while adding/replacing the SPA entry-point (index.html) and assets/.
COPY --from=frontend-build /app/wwwroot/ src/SmartGlassesMiddleware/wwwroot/

RUN dotnet publish "src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj" \
    -c Release -o /app/publish

# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl fonts-liberation \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "SmartGlassesMiddleware.dll"]
