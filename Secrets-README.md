Docker secrets (optional)
=========================

This project supports two patterns for providing secrets to Docker Compose:

1. Use a `.env` file (recommended for local development). Create a copy of `.env.example` named `.env` into the secrets directory and update values. **Do not commit** `.env` to source control.

2. Use Docker secrets (safer for production). Create secret files and reference them in `docker-compose.yml`. Example:

   mkdir -p secrets
   echo "your_postgres_password" > secrets/postgres_password
   echo "your_minio_password" > secrets/minio_root_password

Then update the compose file to reference them under `secrets:` and the service `secrets` section.

Note: Docker secrets require swarm mode for full support. For local development `.env` is simpler.
