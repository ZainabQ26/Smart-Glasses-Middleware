# Aggressive best-effort cleanup for `dev` environment resources
# WARNING: Destructive. Deletes resources with names containing 'smart-glasses-dev'.
# Run with AWS credentials configured for the target account/region.

$prefix = 'smart-glasses-dev'
$region = $env:AWS_REGION
if (-not $region) { $region = 'us-east-1' }
Write-Output "Running destructive cleanup for prefix '$prefix' in region $region"

function Invoke-ConfirmedCommand($cmd) {
    Write-Output "About to run: $cmd"
    $confirm = Read-Host "Run this command? (y/N)"
    if ($confirm -ne 'y' -and $confirm -ne 'Y') {
        Write-Warning "Skipping command by user choice: $cmd"
        return
    }
    try {
        Start-Process -FilePath "powershell" -ArgumentList ('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $cmd) -NoNewWindow -Wait
    } catch {
        Write-Warning "Command failed: $cmd -- continuing: $_"
    }
}

# 1) ECR repositories
Write-Output "Deleting ECR repositories..."
$repos = aws ecr describe-repositories --region $region | ConvertFrom-Json
if ($repos.repositories) {
    foreach ($r in $repos.repositories) {
        if ($r.repositoryName -like "*$prefix*") {
            Write-Output "Deleting ECR repo: $($r.repositoryName)"
            Invoke-ConfirmedCommand "aws ecr delete-repository --repository-name $($r.repositoryName) --force --region $region"
        }
    }
}

# 2) CloudFront Origin Request Policies and other CF custom policies
Write-Output "Deleting CloudFront origin request policies..."
$orp = aws cloudfront list-origin-request-policies --region $region | ConvertFrom-Json
if ($orp) {
    foreach ($p in $orp.OriginRequestPolicyList.Items) {
        if ($p.OriginRequestPolicy.OriginRequestPolicyConfig.Name -like "*$prefix*") {
            $id = $p.OriginRequestPolicy.Id
            Write-Output "Deleting Origin Request Policy: $($p.OriginRequestPolicy.OriginRequestPolicyConfig.Name) ($id)"
            Invoke-ConfirmedCommand "aws cloudfront delete-origin-request-policy --id $id --region $region"
        }
    }
}

# 3) CloudFront cache/policies that match
Write-Output "Deleting CloudFront cache/response policies..."
$cp = aws cloudfront list-cache-policies --region $region | ConvertFrom-Json
if ($cp) {
    foreach ($p in $cp.CachePolicyList.Items) {
        if ($p.CachePolicy.CachePolicyConfig.Name -like "*$prefix*") {
            $id = $p.CachePolicy.Id
            Write-Output "Deleting Cache Policy: $($p.CachePolicy.CachePolicyConfig.Name) ($id)"
            Invoke-ConfirmedCommand "aws cloudfront delete-cache-policy --id $id --region $region"
        }
    }
}

# 4) KMS aliases
Write-Output "Deleting KMS aliases..."
$aliases = aws kms list-aliases --region $region | ConvertFrom-Json
foreach ($a in $aliases.Aliases) {
    if ($a.AliasName -like "*${prefix}*" -or $a.AliasName -like "alias/$prefix*") {
        Write-Output "Deleting KMS alias: $($a.AliasName)"
        Invoke-ConfirmedCommand "aws kms delete-alias --alias-name $($a.AliasName) --region $region"
    }
}

# 5) CloudWatch Log Groups
Write-Output "Deleting CloudWatch log groups..."
$lg = aws logs describe-log-groups --log-group-name-prefix "/" --region $region | ConvertFrom-Json
if ($lg.logGroups) {
    foreach ($g in $lg.logGroups) {
        if ($g.logGroupName -like "*$prefix*") {
            Write-Output "Deleting log group: $($g.logGroupName)"
            Invoke-ConfirmedCommand "aws logs delete-log-group --log-group-name '$($g.logGroupName)' --region $region"
        }
    }
}

# 6) IAM roles (detach policies, delete)
Write-Output "Deleting IAM roles..."
$roles = aws iam list-roles | ConvertFrom-Json
foreach ($r in $roles.Roles) {
    if ($r.RoleName -like "*$prefix*") {
        Write-Output "Processing role: $($r.RoleName)"
        # Detach managed policies
        $attached = aws iam list-attached-role-policies --role-name $($r.RoleName) | ConvertFrom-Json
        foreach ($p in $attached.AttachedPolicies) {
            Write-Output "Detaching policy $($p.PolicyArn) from $($r.RoleName)"
            Invoke-ConfirmedCommand "aws iam detach-role-policy --role-name $($r.RoleName) --policy-arn $($p.PolicyArn)"
        }
        # Delete inline policies
        $inline = aws iam list-role-policies --role-name $($r.RoleName) | ConvertFrom-Json
        foreach ($pn in $inline.PolicyNames) {
            Write-Output "Deleting inline policy $pn from $($r.RoleName)"
            Invoke-ConfirmedCommand "aws iam delete-role-policy --role-name $($r.RoleName) --policy-name $pn"
        }
        # Delete instance profiles referencing the role
        $profiles = aws iam list-instance-profiles-for-role --role-name $($r.RoleName) | ConvertFrom-Json
        foreach ($ip in $profiles.InstanceProfiles) {
            Write-Output "Removing role from instance profile $($ip.InstanceProfileName)"
            Invoke-ConfirmedCommand "aws iam remove-role-from-instance-profile --instance-profile-name $($ip.InstanceProfileName) --role-name $($r.RoleName)"
            Invoke-ConfirmedCommand "aws iam delete-instance-profile --instance-profile-name $($ip.InstanceProfileName)"
        }
        # Finally delete role
        Write-Output "Deleting role $($r.RoleName)"
        Invoke-ConfirmedCommand "aws iam delete-role --role-name $($r.RoleName)"
    }
}

# 7) S3 buckets prefixed
Write-Output "Deleting S3 buckets with prefix..."
$buckets = aws s3api list-buckets --region $region | ConvertFrom-Json
foreach ($b in $buckets.Buckets) {
    if ($b.Name -like "*$prefix*") {
        Write-Output "Emptying and deleting S3 bucket: $($b.Name)"
        Invoke-ConfirmedCommand "aws s3 rm s3://$($b.Name) --recursive --region $region"
        Invoke-ConfirmedCommand "aws s3api delete-bucket --bucket $($b.Name) --region $region"
    }
}

# 8) CloudFormation stacks with prefix (if any)
Write-Output "Deleting CloudFormation stacks with prefix..."
$stacks = aws cloudformation describe-stacks --region $region 2>$null | ConvertFrom-Json
if ($stacks.Stacks) {
    foreach ($s in $stacks.Stacks) {
        if ($s.StackName -like "*$prefix*") {
            Write-Output "Deleting stack: $($s.StackName)"
            Invoke-ConfirmedCommand "aws cloudformation delete-stack --stack-name $($s.StackName) --region $region"
        }
    }
}

Write-Output "Cleanup run complete. Some resources (VPCs, ENIs, NAT gateways) may require manual deletion if they block VPC removal."
Write-Output "Next: create S3 backend + DynamoDB lock table and run terraform init/apply."
