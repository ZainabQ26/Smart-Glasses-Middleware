#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

REPO_SLUG="robertbiv/Smart-Glasses-Middleware"
KEEP_RUNNER="${KEEP_RUNNER:-false}"
KEEP_RUNNER_DATA="${KEEP_RUNNER_DATA:-false}"
USE_LOCAL_RUNNER_OVERRIDE="${USE_LOCAL_RUNNER_OVERRIDE:-true}"

RUNNER_PREFIX="smart-glasses-local-$(date +%s)"
RUNNER_ID=""
RUN_ID=""
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null || true)"
TMP_WORKTREE=""
TMP_BRANCH=""

cleanup() {
  if [ -n "$TMP_BRANCH" ] && [ -n "$REPO_ROOT" ]; then
    echo "Deleting temporary remote branch: $TMP_BRANCH"
    git -C "$REPO_ROOT" push origin --delete "$TMP_BRANCH" >/dev/null 2>&1 || true
  fi

  if [ -n "$TMP_WORKTREE" ] && [ -n "$REPO_ROOT" ]; then
    echo "Removing temporary worktree..."
    git -C "$REPO_ROOT" worktree remove --force "$TMP_WORKTREE" >/dev/null 2>&1 || rm -rf "$TMP_WORKTREE"
  fi

  if [ "$KEEP_RUNNER" = "true" ]; then
    echo "KEEP_RUNNER=true; leaving runner container and registration in place."
    return
  fi

  if [ -n "$RUNNER_ID" ]; then
    echo "Unregistering runner id=$RUNNER_ID from GitHub..."
    gh api -X DELETE "repos/${REPO_SLUG}/actions/runners/${RUNNER_ID}" >/dev/null 2>&1 || true
  fi

  if [ "$KEEP_RUNNER_DATA" = "true" ]; then
    echo "Stopping runner container (keeping volume)..."
    docker compose down || true
  else
    echo "Stopping runner container and removing volume..."
    docker compose down -v || true
  fi
}

trap cleanup EXIT

if ! command -v docker >/dev/null 2>&1; then
  echo "docker is required." >&2
  exit 1
fi
if ! command -v gh >/dev/null 2>&1; then
  echo "gh (GitHub CLI) is required." >&2
  exit 1
fi

if ! gh auth status >/dev/null 2>&1; then
  echo "gh is not authenticated. Run: gh auth login" >&2
  exit 1
fi

if [ ! -f ".env" ]; then
  cp .env.example .env
fi

if grep -q '^RUNNER_NAME_PREFIX=' .env; then
  sed -i "s|^RUNNER_NAME_PREFIX=.*$|RUNNER_NAME_PREFIX=${RUNNER_PREFIX}|" .env
else
  printf 'RUNNER_NAME_PREFIX=%s\n' "$RUNNER_PREFIX" >> .env
fi

REG_TOKEN=$(gh api -X POST "repos/${REPO_SLUG}/actions/runners/registration-token" --jq .token)
if [ -z "$REG_TOKEN" ]; then
  echo "Failed to retrieve runner registration token via gh API." >&2
  exit 1
fi

# Refresh short-lived registration token each run.
if grep -q '^RUNNER_TOKEN=' .env; then
  sed -i "s|^RUNNER_TOKEN=.*$|RUNNER_TOKEN=${REG_TOKEN}|" .env
else
  printf '\nRUNNER_TOKEN=%s\n' "$REG_TOKEN" >> .env
fi

# Optional PAT fallback for users who still prefer it.
TOKEN="${ACCESS_TOKEN:-${GH_PAT:-${GITHUB_PAT:-}}}"
if [ -n "$TOKEN" ]; then
  if grep -q '^ACCESS_TOKEN=' .env; then
    sed -i "s|^ACCESS_TOKEN=.*$|ACCESS_TOKEN=${TOKEN}|" .env
  else
    printf 'ACCESS_TOKEN=%s\n' "$TOKEN" >> .env
  fi
fi

echo "Starting Dockerized GitHub runner..."
docker compose up -d

echo "Waiting briefly for runner registration..."
sleep 8

RUNNER_ID=$(gh api "repos/${REPO_SLUG}/actions/runners" \
  --jq ".runners[] | select(.name | startswith(\"${RUNNER_PREFIX}\")) | .id" | head -n1 || true)

if [ -z "$RUNNER_ID" ]; then
  echo "Could not locate newly registered runner in GitHub." >&2
  exit 1
fi

echo "Registered runner id=$RUNNER_ID (prefix=$RUNNER_PREFIX)."

REF="${DEPLOY_REF:-main}"
ENVIRONMENT="${DEPLOY_ENVIRONMENT:-dev}"
FORCE_RECREATE_DEV="${DEPLOY_FORCE_RECREATE_DEV:-false}"

if [ "$USE_LOCAL_RUNNER_OVERRIDE" = "true" ]; then
  if ! command -v git >/dev/null 2>&1; then
    echo "git is required when USE_LOCAL_RUNNER_OVERRIDE=true" >&2
    exit 1
  fi
  if [ -z "$REPO_ROOT" ]; then
    echo "Could not determine repository root from script location." >&2
    exit 1
  fi

  TMP_BRANCH="tmp/local-runner-${REF//\//-}-$(date +%s)"
  TMP_WORKTREE="$(mktemp -d "${TMPDIR:-/tmp}/sgm-local-runner-XXXXXX")"

  echo "Creating temporary override branch: $TMP_BRANCH"
  git -C "$REPO_ROOT" fetch origin "$REF"
  git -C "$REPO_ROOT" worktree add "$TMP_WORKTREE" -b "$TMP_BRANCH" "origin/$REF"

  WF_PATH="$TMP_WORKTREE/.github/workflows/deploy-main.yml"
  if [ ! -f "$WF_PATH" ]; then
    echo "Missing workflow file in temporary worktree: $WF_PATH" >&2
    exit 1
  fi

  # Override runner target only in temporary branch.
  sed -i 's/runs-on: ubuntu-latest/runs-on: [self-hosted, linux]/g' "$WF_PATH"

  git -C "$TMP_WORKTREE" config user.name "local-runner-script"
  git -C "$TMP_WORKTREE" config user.email "local-runner-script@local"
  git -C "$TMP_WORKTREE" add .github/workflows/deploy-main.yml
  git -C "$TMP_WORKTREE" commit -m "chore(ci): temporary self-hosted override for local deploy"
  git -C "$TMP_WORKTREE" push -u origin "$TMP_BRANCH"

  REF="$TMP_BRANCH"
  echo "Using temporary ref: $REF"
fi

echo "Dispatching deploy-main.yml (ref=$REF, environment=$ENVIRONMENT)..."
gh workflow run deploy-main.yml \
  --ref "$REF" \
  --field environment="$ENVIRONMENT" \
  --field force_recreate_dev="$FORCE_RECREATE_DEV"

echo "Waiting for dispatched run id..."
for _ in $(seq 1 30); do
  RUN_ID=$(gh run list \
    --workflow deploy-main.yml \
    --branch "$REF" \
    --event workflow_dispatch \
    --limit 10 \
    --json databaseId,status \
    --jq '.[] | select(.status == "queued" or .status == "in_progress" or .status == "waiting" or .status == "requested" or .status == "pending") | .databaseId' \
    | head -n1 || true)
  if [ -n "$RUN_ID" ]; then
    break
  fi
  sleep 2
done

if [ -z "$RUN_ID" ]; then
  echo "Could not find the dispatched workflow run in time." >&2
  exit 1
fi

echo "Watching run id=$RUN_ID until completion..."
gh run watch "$RUN_ID" --exit-status

echo "Workflow finished. Cleanup will run automatically."
