#!/bin/bash
# Script to build and package Lambda function

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Installing dependencies..."
pip install -r requirements.txt -t package/

echo "Copying function code..."
cp index.py package/

echo "Creating deployment package..."
cd package
zip -r ../deployment.zip . -x "*.pyc" -x "*__pycache__*"
cd ..

echo "Cleaning up..."
rm -rf package/

echo "Lambda deployment package created: deployment.zip"
