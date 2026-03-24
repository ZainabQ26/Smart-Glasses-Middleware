This folder contains a minimal AWS SAM template and quick commands to build and deploy the two Python Lambda functions used by the project.

Quick steps

1. Install SAM CLI: https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/serverless-sam-cli-install.html
2. From repository root, build the functions:

```bash
cd aws/sam
sam build --use-container
```

3. Deploy (guided):

```bash
sam deploy --guided --region us-east-1
```

Notes

- The template expects two parameters: `DocumentsBucket` and `DBSecretArn` (you will be prompted during `sam deploy --guided`).
- `CodeUri` paths are relative to this template file and point at `aws/lambda-document-processor/` and `aws/lambda-whisper/`.
- The `whisper` lambda includes heavy native dependencies; building may require Docker and extra time. If you prefer container packaging for the whisper runtime, we can adjust the template to use `PackageType: Image`.

Using the AWS Toolkit (VS Code)

- Install the AWS Toolkit extension in VS Code and configure an AWS credential/profile (Command Palette -> `AWS: Connect to AWS`).
- Open the *AWS Explorer* (AWS icon in the Activity Bar) and select the correct AWS profile and region from the top of the AWS Explorer pane.
- Deploy the SAM template from the workspace:
	- Option A (recommended): Open the file `aws/sam/template.yaml` in the editor, right-click the editor and choose **AWS: Deploy SAM Application**. The Toolkit will prompt for the template parameters (`DocumentsBucket`, `DBSecretArn`), an S3 bucket for staging (or let the Toolkit create one), and a deployment name.
	- Option B: Use the Command Palette and run `AWS: Deploy SAM Application`, then pick the template file when prompted.
- Before deploying you can run `sam build --use-container` locally; the Toolkit also runs a build step if needed. For the `whisper` function consider container/image packaging if native dependencies fail to build in the Toolkit's environment.
- After deployment the Lambda functions appear under *Lambda* in AWS Explorer. Right-click a function for actions: *Invoke on AWS*, *View CloudWatch Logs*, *Open in Console*, or *Configure Triggers*.

Notes on LocalStack

- The AWS Toolkit does not natively target LocalStack endpoints. To test against LocalStack use the included scripts and CLI tasks in the `aws/` folder (see `docker-compose.localstack.yml` and `aws/LOCALSTACK.md`).

Files

- SAM template: [aws/sam/template.yaml](aws/sam/template.yaml)
- Template README: [aws/sam/README.md](aws/sam/README.md)
