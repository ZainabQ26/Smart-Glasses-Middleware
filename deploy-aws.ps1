# AWS Deployment Automation Script
# This script automates the AWS deployment process
# Run with: .\deploy-aws.ps1 -Phase <phase-number>

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("1","2","3","4","5","all")]
    [string]$Phase = "all",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "prod",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "us-east-1"
)

$ErrorActionPreference = "Stop"

function Write-Status {
    param([string]$Message, [string]$Type = "Info")
    $color = switch ($Type) {
        "Success" { "Green" }
        "Error" { "Red" }
        "Warning" { "Yellow" }
        default { "Cyan" }
    }
    Write-Host "`n[$Type] $Message" -ForegroundColor $color
}

function Test-Prerequisites {
    Write-Status "Checking prerequisites..." "Info"
    
    $checks = @(
        @{Name="AWS CLI"; Command="aws --version"},
        @{Name="Terraform"; Command="terraform --version"},
        @{Name="Docker"; Command="docker --version"},
        @{Name=".NET SDK"; Command="dotnet --version"}
    )
    
    foreach ($check in $checks) {
        try {
            $null = Invoke-Expression $check.Command 2>&1
            Write-Host "  ✓ $($check.Name) installed" -ForegroundColor Green
        } catch {
            Write-Status "$($check.Name) not found. Please install it." "Error"
            exit 1
        }
    }
    
    # Test AWS credentials
    try {
        $identity = aws sts get-caller-identity | ConvertFrom-Json
        Write-Host "  ✓ AWS credentials configured (Account: $($identity.Account))" -ForegroundColor Green
    } catch {
        Write-Status "AWS credentials not configured. Run 'aws configure'" "Error"
        exit 1
    }
}

function Deploy-Phase1 {
    Write-Status "Phase 1: AWS Setup" "Info"
    
    # Create Terraform state bucket
    $bucketName = "smart-glasses-tfstate-$(Get-Random -Maximum 99999)"
    Write-Status "Creating Terraform state bucket: $bucketName"
    
    aws s3 mb s3://$bucketName --region $Region
    aws s3api put-bucket-versioning --bucket $bucketName --versioning-configuration Status=Enabled
    aws s3api put-bucket-encryption --bucket $bucketName --server-side-encryption-configuration '{
        "Rules": [{"ApplyServerSideEncryptionByDefault": {"SSEAlgorithm": "AES256"}}]
    }'
    
    Write-Status "Terraform state bucket created: $bucketName" "Success"
    Write-Status "Update aws/infrastructure/main.tf with bucket name: $bucketName" "Warning"
    
    # Save bucket name for later use
    $bucketName | Out-File -FilePath "terraform-state-bucket.txt"
}

function Deploy-Phase2 {
    Write-Status "Phase 2: Infrastructure Deployment" "Info"
    
    Push-Location aws\infrastructure
    
    try {
        # Initialize Terraform
        Write-Status "Initializing Terraform..."
        terraform init
        
        # Plan
        Write-Status "Planning infrastructure..."
        terraform plan -out=tfplan
        
        # Ask for confirmation
        $confirm = Read-Host "Apply infrastructure? (yes/no)"
        if ($confirm -ne "yes") {
            Write-Status "Deployment cancelled" "Warning"
            Pop-Location
            return
        }
        
        # Apply
        Write-Status "Applying infrastructure (this takes 20-30 minutes)..."
        terraform apply tfplan
        
        # Save outputs
        terraform output > ..\..\terraform-outputs.txt
        
        Write-Status "Infrastructure deployed successfully!" "Success"
        Write-Status "Outputs saved to terraform-outputs.txt" "Info"
        
    } finally {
        Pop-Location
    }
}

function Deploy-Phase3 {
    Write-Status "Phase 3: Database Setup" "Info"
    
    Push-Location aws\infrastructure
    
    try {
        # Get RDS endpoint
        $rdsEndpoint = terraform output -raw rds_endpoint
        
        # Get DB credentials from Secrets Manager
        Write-Status "Retrieving database credentials..."
        $secretName = "smart-glasses-$Environment-db-credentials"
        $secretJson = aws secretsmanager get-secret-value --secret-id $secretName --query SecretString --output text
        $secret = $secretJson | ConvertFrom-Json
        
        Write-Status "Database endpoint: $rdsEndpoint" "Info"
        Write-Status "Please manually enable pgvector extension:" "Warning"
        Write-Host "  psql -h $rdsEndpoint -U $($secret.username) -d smartglasses -c 'CREATE EXTENSION IF NOT EXISTS vector;'"
        
        $continue = Read-Host "`nHave you enabled pgvector? (yes/no)"
        if ($continue -ne "yes") {
            Write-Status "Please enable pgvector before continuing" "Warning"
            Pop-Location
            return
        }
        
        # Run migrations
        Write-Status "Running database migrations..."
        Push-Location ..\..\src\SmartGlassesMiddleware
        
        $connString = "Host=$rdsEndpoint;Database=smartglasses;Username=$($secret.username);Password=$($secret.password);SSL Mode=Require"
        dotnet ef database update --connection $connString
        
        Pop-Location
        Write-Status "Database setup complete!" "Success"
        
    } finally {
        Pop-Location
    }
}

function Deploy-Phase4 {
    Write-Status "Phase 4: Build and Push Docker Images" "Info"
    
    Push-Location aws\infrastructure
    
    try {
        # Get ECR URLs
        $ecrApiRepo = terraform output -raw ecr_api_repository_url
        $ecrWhisperRepo = terraform output -raw ecr_whisper_repository_url
        $awsAccount = (aws sts get-caller-identity | ConvertFrom-Json).Account
        
        Write-Status "ECR API: $ecrApiRepo" "Info"
        Write-Status "ECR Whisper: $ecrWhisperRepo" "Info"
        
        # Login to ECR
        Write-Status "Logging into ECR..."
        aws ecr get-login-password --region $Region | docker login --username AWS --password-stdin "$awsAccount.dkr.ecr.$Region.amazonaws.com"
        
        Pop-Location
        
        # Build and push API
        Write-Status "Building .NET API image..."
        docker build -t smart-glasses-api:latest -f src/SmartGlassesMiddleware/Dockerfile .
        
        Write-Status "Pushing API image to ECR..."
        docker tag smart-glasses-api:latest "$ecrApiRepo`:latest"
        docker push "$ecrApiRepo`:latest"
        
        # Build and push Whisper Lambda
        Write-Status "Building Whisper Lambda image..."
        Push-Location aws\lambda-whisper
        docker build -t smart-glasses-whisper:latest .
        
        Write-Status "Pushing Whisper image to ECR..."
        docker tag smart-glasses-whisper:latest "$ecrWhisperRepo`:latest"
        docker push "$ecrWhisperRepo`:latest"
        Pop-Location
        
        # Update Terraform with Whisper image URI
        Write-Status "Updating Terraform with Whisper image..."
        $tfVarsPath = "aws\infrastructure\terraform.tfvars"
        $tfVarsContent = Get-Content $tfVarsPath
        $tfVarsContent = $tfVarsContent -replace 'whisper_image_uri\s*=\s*".*"', "whisper_image_uri = `"$ecrWhisperRepo`:latest`""
        $tfVarsContent | Set-Content $tfVarsPath
        
        Push-Location aws\infrastructure
        terraform apply -auto-approve
        Pop-Location
        
        Write-Status "Docker images deployed successfully!" "Success"
        
    } catch {
        Write-Status "Error: $_" "Error"
        Pop-Location
    }
}

function Deploy-Phase5 {
    Write-Status "Phase 5: Deploy ECS Service" "Info"
    
    $clusterName = "smart-glasses-$Environment"
    $serviceName = "smart-glasses-$Environment-api"
    
    Write-Status "Forcing ECS service update..."
    aws ecs update-service --cluster $clusterName --service $serviceName --force-new-deployment | Out-Null
    
    Write-Status "Waiting for service to stabilize (this may take 5-10 minutes)..."
    aws ecs wait services-stable --cluster $clusterName --services $serviceName
    
    # Test health endpoint
    Push-Location aws\infrastructure
    $albDns = terraform output -raw alb_dns_name
    Pop-Location
    
    Write-Status "Testing API health endpoint..."
    $healthUrl = "http://$albDns/health"
    
    Start-Sleep -Seconds 10
    
    try {
        $response = Invoke-RestMethod -Uri $healthUrl -Method Get
        Write-Status "API is healthy!" "Success"
        Write-Host ($response | ConvertTo-Json -Depth 10)
    } catch {
        Write-Status "API health check failed. Check ECS logs." "Error"
    }
    
    Write-Status "ECS service deployed!" "Success"
    Write-Status "API URL: $healthUrl" "Info"
}

# Main execution
Clear-Host
Write-Host @"
╔════════════════════════════════════════════════════════════╗
║        Smart Glasses Middleware - AWS Deployment          ║
║                                                            ║
║  Environment: $Environment                                ║
║  Region: $Region                                          ║
╚════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

Test-Prerequisites

switch ($Phase) {
    "1" { Deploy-Phase1 }
    "2" { Deploy-Phase2 }
    "3" { Deploy-Phase3 }
    "4" { Deploy-Phase4 }
    "5" { Deploy-Phase5 }
    "all" {
        Write-Status "Running full deployment..." "Info"
        Deploy-Phase1
        
        Write-Host "`nPress Enter after updating main.tf with the bucket name..."
        Read-Host
        
        Deploy-Phase2
        Deploy-Phase3
        Deploy-Phase4
        Deploy-Phase5
        
        Write-Status @"
╔════════════════════════════════════════════════════════════╗
║                  Deployment Complete! 🎉                  ║
╚════════════════════════════════════════════════════════════╝

Next steps:
1. Test your API endpoints
2. Deploy the client app (optional)
3. Set up monitoring and alerts
4. Configure custom domain (optional)

See AWS_DEPLOYMENT_GUIDE.md for detailed instructions.
"@ "Success"
    }
}

Write-Host "`n"
