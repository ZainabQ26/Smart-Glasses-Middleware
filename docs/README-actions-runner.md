# Dockerized GitHub Actions Runner

This directory runs a full self-hosted GitHub Actions runner in Docker.
It is designed to run `deploy-main.yml` without changing workflow inputs.

## 1. Prerequisites

- Docker Engine running on Linux host
- `gh` CLI authenticated (`gh auth login`)
- Repo secrets configured in GitHub (AWS credentials/role variables used by workflows)

## 2. Configure

```bash
cd scripts/actions-runner
cp .env.example .env
```

No manual token copy is required: helper scripts fetch a short-lived runner registration token from GitHub via `gh api`.

## 3. Start runner

```bash
docker compose up -d
docker compose logs -f
```

The runner should appear in repository settings under:
`Settings -> Actions -> Runners`.

## 4. Trigger deploy workflow on this runner

Run one command with defaults (no prompts):

Linux/macOS:

```bash
cd scripts/actions-runner
chmod +x run-deploy.sh
./run-deploy.sh
```

Windows PowerShell:

```powershell
cd scripts/actions-runner
./run-deploy.ps1
```

The scripts:

- create `.env` from `.env.example` if needed,
- fetch and write `RUNNER_TOKEN` automatically via `gh api`,
- start the Docker runner,
- create a temporary branch that changes `deploy-main.yml` to `runs-on: [self-hosted, linux]`,
- dispatch `deploy-main.yml` with defaults,
- wait for the run to complete,
- unregister the runner and tear down the container automatically,
- delete the temporary branch/worktree.

Defaults used by scripts:

- `DEPLOY_REF=main`
- `DEPLOY_ENVIRONMENT=dev`
- `DEPLOY_FORCE_RECREATE_DEV=false`

Override defaults via environment variables before running the script.

Lifecycle flags:

- `KEEP_RUNNER=true` keeps the runner registered and container running after completion.
- `KEEP_RUNNER_DATA=true` removes container but keeps Docker volume data.
- `USE_LOCAL_RUNNER_OVERRIDE=false` disables temporary branch override and uses `DEPLOY_REF` directly.

```bash
gh workflow run deploy-main.yml \
  --ref main \
  --field environment=dev \
  --field force_recreate_dev=false
```

## 5. Stop and remove runner container

```bash
docker compose down
```

## 6. Full cleanup (remove local runner work/cache)

```bash
docker compose down -v
```

This removes the `runner-data` volume and all checked-out workflow workspace data.

## Notes

- The runner container uses your host Docker daemon via `/var/run/docker.sock` so Docker builds happen on the host.
- The runner labels include `ubuntu-latest` to avoid extra workflow inputs.
- Registration tokens are short-lived and refreshed by scripts on each run.
- Workflow-generated files may remain in:
  - Docker images/layers on host Docker daemon
  - `runner-data` Docker volume
- AWS-side resources created by the workflow are not cleaned up by `docker compose down -v`.
