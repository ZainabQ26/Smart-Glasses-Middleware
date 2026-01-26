secrets.example/
=================
Usage
-----

1. Copy the example files into `secrets/` and replace placeholders with real values:

   ```bash
   mkdir -p secrets
   cp -R secrets.example/* secrets/
   # then edit the files under secrets/
   ```

2. Secret filename conventions (simple, no duplicates):

   - `postgres_password` — Postgres password (used by Postgres image via `POSTGRES_PASSWORD_FILE` and by the app)
   - `minio_access_key` — MinIO access key
   - `minio_secret_key` — MinIO secret key
   - `ai_stronger_endpoint` — Stronger model endpoint URL

   These names are intentionally simple and unique; the application loads them from `/run/secrets/` and maps them into configuration automatically.

3. Start the stack:

   ```bash
   docker compose up --build
   ```

Notes
-----
- `secrets/` is ignored by git; `secrets.example/` is tracked and safe to publish.
- Do not commit real secrets into the repository.
-----
- `secrets/` is ignored by git; `secrets.example/` is tracked and safe to publish.
- Do not commit real secrets into the repository.
