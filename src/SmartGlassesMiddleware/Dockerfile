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
    -c Release -o /app/publish -r linux-x64 --self-contained false \
    /p:PublishReadyToRun=false

# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl fonts-liberation tesseract-ocr tesseract-ocr-eng libtesseract-dev libleptonica-dev \
    && rm -rf /var/lib/apt/lists/* \
    && ln -sf /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /usr/lib/x86_64-linux-gnu/libtesseract50.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/liblept.so.5 /usr/lib/x86_64-linux-gnu/libleptonica-1.82.0.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/liblept.so.5 /usr/lib/x86_64-linux-gnu/liblept.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /usr/lib/x86_64-linux-gnu/libtesseract.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so \
    && ldconfig \
    && mkdir -p /app/x64 \
    && ln -sf /usr/lib/x86_64-linux-gnu/libdl.so.2 /app/libdl.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /app/x64/libtesseract50.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/libtesseract.so.5 /app/x64/libtesseract.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/liblept.so.5 /app/x64/libleptonica-1.82.0.so \
    && ln -sf /usr/lib/x86_64-linux-gnu/liblept.so.5 /app/x64/liblept.so
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "SmartGlassesMiddleware.dll"]
