# Document Processor Lambda Function

This Lambda function processes documents uploaded to S3, extracts text, generates embeddings, and stores them in PostgreSQL with pgvector.

## Features

- **Text Extraction**: Uses Amazon Textract for PDF processing
- **Chunking**: Splits documents into manageable chunks with overlap
- **Embeddings**: Generates vector embeddings using Amazon Bedrock Titan Embeddings V2
- **Storage**: Stores chunks and embeddings in PostgreSQL with pgvector extension

## Environment Variables

- `AWS_REGION`: AWS region (set by Lambda)
- `DOCUMENTS_BUCKET`: S3 bucket for documents
- `DB_SECRET_ARN`: ARN of Secrets Manager secret with DB credentials
- `BEDROCK_MODEL_ID`: Bedrock model ID for LLM (optional)
- `EMBEDDING_MODEL_ID`: Bedrock model ID for embeddings (default: amazon.titan-embed-text-v2:0)
- `MAX_CHUNK_SIZE`: Maximum chunk size in words (default: 1000)
- `CHUNK_OVERLAP`: Overlap between chunks in words (default: 200)

## Building

Run the build script to create the deployment package:

```bash
chmod +x build.sh
./build.sh
```

This creates `deployment.zip` which can be deployed to Lambda.

## Testing Locally

You can test the function locally using AWS SAM CLI or by mocking the S3 event.

## Database Schema

The function expects these tables in PostgreSQL:

- `DocumentMetadata`: Document information
- `DocumentChunk`: Text chunks with embeddings (vector type)

## IAM Permissions Required

- `s3:GetObject` on documents bucket
- `bedrock:InvokeModel` for embeddings
- `textract:DetectDocumentText` for PDF processing
- `secretsmanager:GetSecretValue` for DB credentials
- VPC and networking permissions for database access
