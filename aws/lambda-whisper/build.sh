#!/usr/bin/env bash
set -euo pipefail

# Usage: ./build.sh <ecr-repo-uri> <tag>
ECR_REPO=${1:-}
TAG=${2:-latest}

if [ -z "$ECR_REPO" ]; then
  echo "Usage: $0 <ecr-repo-uri> [tag]"
  exit 1
fi

docker build -t lambda-whisper:temp .
docker tag lambda-whisper:temp ${ECR_REPO}:${TAG}
docker push ${ECR_REPO}:${TAG}
