#!/bin/bash
# Script to build and package Lambda function

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Installing dependencies..."
pip install -r requirements.txt -t package/


echo "Copying function code..."
cp index.py package/

echo "Downloading tesseract static binary..."
# Download a compiled tesseract for Amazon Linux 2023 (Lambda environment)
# Source: https://github.com/agentgill/tesseract-for-aws
curl -sL https://raw.githubusercontent.com/agentgill/tesseract-for-aws/main/tesseract-layer.zip -o tesseract.zip
# Ensure temporary artifacts are removed even if an error occurs
trap 'rm -rf tesseract.zip tesseract_tmp/' EXIT
# The layer zip has: bin/tesseract, lib/, tessdata/
# Restructure into package/tesseract/ to match the path used in index.py
mkdir -p tesseract_tmp
unzip -q tesseract.zip -d tesseract_tmp/
mkdir -p package/tesseract
cp tesseract_tmp/bin/tesseract package/tesseract/tesseract
chmod +x package/tesseract/tesseract
cp -r tesseract_tmp/lib/ package/tesseract/lib/
cp -r tesseract_tmp/tessdata/ package/tesseract/tessdata/


echo "Creating deployment package..."
cd package
zip -r ../deployment.zip . -x "*.pyc" -x "*__pycache__*"
cd ..

echo "Cleaning up..."
rm -rf package/

echo "Lambda deployment package created: deployment.zip"
