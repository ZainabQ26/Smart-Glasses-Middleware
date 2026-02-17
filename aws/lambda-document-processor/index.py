import json
import os
import boto3
import logging
from typing import List, Dict, Any
from urllib.parse import unquote_plus
import psycopg2
from psycopg2.extras import execute_values
import numpy as np

# Initialize AWS clients
s3_client = boto3.client('s3')
bedrock_runtime = boto3.client('bedrock-runtime', region_name=os.environ['AWS_REGION'])
textract_client = boto3.client('textract')
secrets_client = boto3.client('secretsmanager')

logger = logging.getLogger()
logger.setLevel(logging.INFO)

# Configuration
DOCUMENTS_BUCKET = os.environ['DOCUMENTS_BUCKET']
DB_SECRET_ARN = os.environ['DB_SECRET_ARN']
BEDROCK_MODEL_ID = os.environ.get('BEDROCK_MODEL_ID', 'anthropic.claude-3-sonnet-20240229-v1:0')
EMBEDDING_MODEL_ID = os.environ.get('EMBEDDING_MODEL_ID', 'amazon.titan-embed-text-v2:0')
MAX_CHUNK_SIZE = int(os.environ.get('MAX_CHUNK_SIZE', 1000))
CHUNK_OVERLAP = int(os.environ.get('CHUNK_OVERLAP', 200))

# Cache for DB connection
db_connection = None


def get_db_connection():
    """Get database connection with credentials from Secrets Manager"""
    global db_connection
    
    if db_connection is not None:
        try:
            # Test connection
            db_connection.cursor().execute('SELECT 1')
            return db_connection
        except:
            db_connection = None
    
    # Get credentials from Secrets Manager
    secret_value = secrets_client.get_secret_value(SecretId=DB_SECRET_ARN)
    secret = json.loads(secret_value['SecretString'])
    
    # Create connection
    db_connection = psycopg2.connect(
        host=secret['host'],
        port=secret['port'],
        database=secret['dbname'],
        user=secret['username'],
        password=secret['password']
    )
    
    return db_connection


def extract_text_from_pdf(bucket: str, key: str) -> str:
    """Extract text from PDF using Amazon Textract"""
    try:
        response = textract_client.detect_document_text(
            Document={
                'S3Object': {
                    'Bucket': bucket,
                    'Name': key
                }
            }
        )
        
        # Extract text from blocks
        text = []
        for block in response['Blocks']:
            if block['BlockType'] == 'LINE':
                text.append(block['Text'])
        
        return '\n'.join(text)
    except Exception as e:
        logger.error(f"Error extracting text from PDF: {str(e)}")
        # Fallback: download and use basic extraction
        obj = s3_client.get_object(Bucket=bucket, Key=key)
        return obj['Body'].read().decode('utf-8', errors='ignore')


def chunk_text(text: str, max_chunk_size: int = MAX_CHUNK_SIZE, overlap: int = CHUNK_OVERLAP) -> List[Dict[str, Any]]:
    """Split text into overlapping chunks"""
    chunks = []
    words = text.split()
    
    start = 0
    chunk_index = 0
    
    while start < len(words):
        end = min(start + max_chunk_size, len(words))
        chunk_text = ' '.join(words[start:end])
        
        chunks.append({
            'text': chunk_text,
            'index': chunk_index,
            'start_position': start,
            'end_position': end
        })
        
        chunk_index += 1
        start = end - overlap if end < len(words) else end
    
    return chunks


def get_embedding(text: str) -> List[float]:
    """Generate embedding using Amazon Bedrock Titan Embeddings"""
    try:
        # Prepare request for Titan Embeddings V2
        request_body = json.dumps({
            "inputText": text[:8000],  # Titan V2 max input: 8192 tokens
            "dimensions": 384,  # Match pgvector dimension
            "normalize": True
        })
        
        response = bedrock_runtime.invoke_model(
            modelId=EMBEDDING_MODEL_ID,
            body=request_body
        )
        
        response_body = json.loads(response['body'].read())
        return response_body['embedding']
    except Exception as e:
        logger.error(f"Error generating embedding: {str(e)}")
        # Return zero vector as fallback
        return [0.0] * 384


def store_chunks_in_db(document_id: str, chunks: List[Dict[str, Any]], file_name: str):
    """Store document chunks with embeddings in PostgreSQL"""
    conn = get_db_connection()
    cursor = conn.cursor()
    
    try:
        # Insert document metadata if not exists
        cursor.execute("""
            INSERT INTO "DocumentMetadata" ("Id", "FileName", "ContentType", "FileSizeBytes", "UploadTimestamp", "ProcessingStatus")
            VALUES (%s, %s, %s, %s, NOW(), %s)
            ON CONFLICT ("Id") DO UPDATE SET "ProcessingStatus" = EXCLUDED."ProcessingStatus"
        """, (document_id, file_name, 'application/pdf', 0, 'Processing'))
        
        # Generate embeddings and store chunks
        chunk_data = []
        for chunk in chunks:
            embedding = get_embedding(chunk['text'])
            chunk_data.append((
                document_id,
                chunk['index'],
                chunk['text'],
                chunk['start_position'],
                chunk['end_position'],
                embedding
            ))
        
        # Batch insert chunks
        execute_values(
            cursor,
            """
            INSERT INTO "DocumentChunk" 
            ("DocumentId", "ChunkIndex", "Content", "StartPosition", "EndPosition", "Embedding")
            VALUES %s
            ON CONFLICT ("DocumentId", "ChunkIndex") DO UPDATE 
            SET "Content" = EXCLUDED."Content",
                "Embedding" = EXCLUDED."Embedding"
            """,
            chunk_data,
            template='(%s, %s, %s, %s, %s, %s::vector)'
        )
        
        # Update document status
        cursor.execute("""
            UPDATE "DocumentMetadata" 
            SET "ProcessingStatus" = %s, "ProcessedTimestamp" = NOW()
            WHERE "Id" = %s
        """, ('Completed', document_id))
        
        conn.commit()
        logger.info(f"Successfully stored {len(chunks)} chunks for document {document_id}")
        
    except Exception as e:
        conn.rollback()
        logger.error(f"Error storing chunks in database: {str(e)}")
        raise
    finally:
        cursor.close()


def handler(event, context):
    """Lambda handler for document processing"""
    logger.info(f"Received event: {json.dumps(event)}")
    
    try:
        # Parse S3 event
        for record in event['Records']:
            bucket = record['s3']['bucket']['name']
            key = unquote_plus(record['s3']['object']['key'])
            
            logger.info(f"Processing document: s3://{bucket}/{key}")
            
            # Extract document ID from key
            document_id = key.split('/')[-1].split('.')[0]
            file_name = key.split('/')[-1]
            
            # Extract text
            text = extract_text_from_pdf(bucket, key)
            logger.info(f"Extracted {len(text)} characters from document")
            
            # Chunk text
            chunks = chunk_text(text)
            logger.info(f"Created {len(chunks)} chunks")
            
            # Store in database
            store_chunks_in_db(document_id, chunks, file_name)
            
        return {
            'statusCode': 200,
            'body': json.dumps({
                'message': 'Document processed successfully',
                'chunks': len(chunks)
            })
        }
        
    except Exception as e:
        logger.error(f"Error processing document: {str(e)}", exc_info=True)
        return {
            'statusCode': 500,
            'body': json.dumps({
                'error': str(e)
            })
        }
