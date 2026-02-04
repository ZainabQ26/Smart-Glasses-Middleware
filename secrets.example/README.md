# DEPRECATED: secrets/ Directory

⚠️ **This directory and the Docker secrets approach has been deprecated.**

## New Configuration Method

All configuration is now managed via a `.env` file in the root directory. This simplifies setup and management.

### Migration Steps

If you were using the old secrets approach:

1. Copy the example environment file:
   ```bash
   cp .env.example .env
   ```

2. Transfer your secret values from `secrets/` files to the corresponding `.env` variables:
   - `secrets/postgres_password` → `POSTGRES_PASSWORD` in `.env`
   - `secrets/minio_access_key` → `MINIO_ACCESS_KEY` in `.env`
   - `secrets/minio_secret_key` → `MINIO_SECRET_KEY` in `.env`
   - `secrets/ai_advanced_endpoint` → `AI_ADVANCED_ENDPOINT` in `.env`

3. Remove the old `secrets/` directory (optional)

4. Restart your services: `docker-compose up --build`

## Why the Change?

- **Simpler setup**: Single `.env` file instead of multiple secret files
- **Better developer experience**: Easier to see and manage all settings in one place
- **Industry standard**: `.env` files are a widely-adopted pattern
- **Flexibility**: Environment variables can be set via `.env`, shell, or CI/CD without file management

## Security Note

The `.env` file is gitignored by default. Never commit `.env` to version control. Only `.env.example` should be committed as a template.

