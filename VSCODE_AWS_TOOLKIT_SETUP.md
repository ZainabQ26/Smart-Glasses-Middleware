# VS Code AWS Toolkit Setup for LocalStack

This guide explains how to set up VS Code AWS Toolkit to work with LocalStack for local AWS development.

## Prerequisites

- VS Code installed
- AWS Toolkit extension for VS Code installed
- Docker and Docker Compose running
- AWS CLI v2 installed

## Step 1: Install AWS Toolkit Extension

1. Open VS Code
2. Go to Extensions (Ctrl+Shift+X / Cmd+Shift+X)
3. Search for "AWS Toolkit"
4. Click Install on the official AWS Toolkit extension

## Step 2: Configure LocalStack AWS Profile

Create a dedicated AWS profile for LocalStack using AWS CLI:

### On Windows (PowerShell):
```powershell
aws configure set aws_access_key_id test --profile localstack
aws configure set aws_secret_access_key test --profile localstack
aws configure set region us-east-1 --profile localstack
```

### On Linux/Mac (Bash):
```bash
aws configure set aws_access_key_id test --profile localstack
aws configure set aws_secret_access_key test --profile localstack
aws configure set region us-east-1 --profile localstack
```

This creates a profile named `localstack` with test credentials that LocalStack accepts.

## Step 3: Start LocalStack

Start LocalStack with all required services:

```bash
docker compose -f docker-compose.localstack.yml up -d
```

Wait for LocalStack to be ready (check logs):
```bash
docker compose -f docker-compose.localstack.yml logs -f localstack
```

## Step 4: Connect AWS Toolkit to LocalStack Profile

1. Open VS Code Command Palette (Ctrl+Shift+P / Cmd+Shift+P)
2. Type and select: `AWS: Add Connection`
3. Choose `Shared credentials` (IAM User)
4. Select the `localstack` profile from the list
5. Click Connect

The AWS Toolkit will now show your connection in the AWS Explorer sidebar.

## Step 5: Verify LocalStack Services

Use AWS CLI with the LocalStack endpoint to verify services are running:

### On Windows (PowerShell):
```powershell
$env:AWS_PROFILE = "localstack"
$env:AWS_ENDPOINT_URL = "http://localhost:4566"
aws s3 ls --endpoint-url http://localhost:4566
```

### On Linux/Mac (Bash):
```bash
export AWS_PROFILE=localstack
export AWS_ENDPOINT_URL=http://localhost:4566
aws s3 ls --endpoint-url http://localhost:4566
```

You should see the three S3 buckets created by LocalStack:
- smart-glasses-dev-media
- smart-glasses-dev-documents
- smart-glasses-dev-frontend

## Step 6: Use AWS Toolkit with LocalStack

### Important Notes:

1. **AWS Toolkit Explorer uses real AWS endpoints** - The AWS Toolkit's visual Explorer connects to actual AWS services, not LocalStack. This is a limitation of the extension.

2. **Use AWS CLI for LocalStack** - For interacting with LocalStack services, use the AWS CLI with the `--endpoint-url` flag:
   ```bash
   aws --endpoint-url=http://localhost:4566 <service> <command>
   ```

3. **Profile management** - The AWS Toolkit is useful for managing multiple profiles (localstack, dev, prod), but CLI commands must specify the endpoint URL.

### Examples of Working with LocalStack via CLI:

**List S3 buckets:**
```bash
aws --endpoint-url=http://localhost:4566 s3 ls
```

**Upload a file to S3:**
```bash
aws --endpoint-url=http://localhost:4566 s3 cp myfile.txt s3://smart-glasses-dev-media/
```

**Get a secret from Secrets Manager:**
```bash
aws --endpoint-url=http://localhost:4566 secretsmanager get-secret-value \
  --secret-id smart-glasses-dev-db-credentials
```

**List Lambda functions:**
```bash
aws --endpoint-url=http://localhost:4566 lambda list-functions
```

## Step 7: Testing the Application

With LocalStack running, test the application:

1. **Check LocalStack health:**
   ```bash
   curl http://localhost:4566/_localstack/health
   ```

2. **Check API health:**
   ```bash
   curl http://localhost:5000/health
   ```

3. **Access Swagger UI:**
   Open your browser to: http://localhost:5000/swagger

## Using VS Code Tasks (Optional)

You can create VS Code tasks to automate common LocalStack operations. Create `.vscode/tasks.json`:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "Start LocalStack",
      "type": "shell",
      "command": "docker compose -f docker-compose.localstack.yml up -d",
      "problemMatcher": []
    },
    {
      "label": "Stop LocalStack",
      "type": "shell",
      "command": "docker compose -f docker-compose.localstack.yml down",
      "problemMatcher": []
    },
    {
      "label": "LocalStack Logs",
      "type": "shell",
      "command": "docker compose -f docker-compose.localstack.yml logs -f",
      "problemMatcher": []
    },
    {
      "label": "List S3 Buckets",
      "type": "shell",
      "command": "aws --endpoint-url=http://localhost:4566 s3 ls",
      "problemMatcher": []
    }
  ]
}
```

Then run tasks via Command Palette > `Tasks: Run Task`.

## Troubleshooting

### AWS Toolkit shows no resources
- **Expected behavior**: The AWS Toolkit Explorer connects to real AWS, not LocalStack
- **Solution**: Use AWS CLI with `--endpoint-url` flag for LocalStack operations

### LocalStack not starting
```bash
# Check Docker logs
docker compose -f docker-compose.localstack.yml logs localstack

# Restart LocalStack
docker compose -f docker-compose.localstack.yml restart localstack

# Clean restart (removes data)
docker compose -f docker-compose.localstack.yml down -v
docker compose -f docker-compose.localstack.yml up -d
```

### Connection refused errors
- Ensure LocalStack is running: `docker ps | grep localstack`
- Check LocalStack health: `curl http://localhost:4566/_localstack/health`
- Verify port 4566 is not in use by another service

### Profile not found
- Verify profile exists: `aws configure list-profiles`
- Re-create profile using the commands in Step 2

## Production Deployment

When deploying to production AWS (not LocalStack):

1. Create a new AWS profile with real credentials:
   ```bash
   aws configure --profile production
   ```

2. Connect AWS Toolkit to the production profile

3. Remove `--endpoint-url` flags from AWS CLI commands

4. Update environment variables to use production AWS service endpoints

## Additional Resources

- [AWS Toolkit Documentation](https://docs.aws.amazon.com/toolkit-for-vscode/)
- [LocalStack Documentation](https://docs.localstack.cloud/)
- [AWS CLI Documentation](https://docs.aws.amazon.com/cli/)
- [Project LocalStack Guide](aws/LOCALSTACK.md)
