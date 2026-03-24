import json
import os
import uuid
import boto3
import fitz  # PyMuPDF — extracts text from searchable PDFs without OCR
import logging
from typing import List, Dict, Any
from urllib.parse import unquote_plus
import psycopg2
from psycopg2.extras import execute_values
from datetime import datetime, timezone

# Initialize AWS clients
s3_client = boto3.client('s3')
bedrock_runtime = boto3.client('bedrock-runtime', region_name=os.environ['AWS_REGION'])
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
    """Return a live psycopg2 connection, reusing a cached one when still healthy.

    Credentials are fetched from AWS Secrets Manager on the first call and cached
    for the lifetime of the Lambda execution environment.  A lightweight ``SELECT 1``
    probe is run to detect stale connections caused by a Lambda container restart or
    a database failover; if the probe fails the connection is discarded and a fresh
    one is opened.

    Returns:
        psycopg2.connection: An open, ready-to-use database connection.

    Raises:
        psycopg2.OperationalError: If the database is unreachable.
        botocore.exceptions.ClientError: If the Secrets Manager call fails.
    """
    global db_connection
    
    if db_connection is not None:
        try:
            # Test connection and properly close cursor
            cursor = db_connection.cursor()
            try:
                cursor.execute('SELECT 1')
            finally:
                cursor.close()
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
    """Extract all text from a searchable PDF stored in S3 using PyMuPDF.

    The uploaded file is a text-based PDF produced by iText/LibreOffice, so the
    text layer is read directly — no OCR or Textract calls are made.

    Args:
        bucket: S3 bucket name containing the PDF.
        key: S3 object key for the PDF file.

    Returns:
        The full extracted text with pages separated by newlines.
        Returns an empty string if no text layer is found (image-only PDFs).

    Raises:
        botocore.exceptions.ClientError: If the S3 GetObject call fails.
        fitz.FitzError: If the file cannot be parsed as a PDF.
    """
    obj = s3_client.get_object(Bucket=bucket, Key=key)
    pdf_bytes = obj['Body'].read()

    text_parts = []
    with fitz.open(stream=pdf_bytes, filetype='pdf') as doc:
        for page in doc:
            text_parts.append(page.get_text())

    extracted = '\n'.join(text_parts).strip()

    if not extracted:
        logger.warning("PyMuPDF extracted no text from %s/%s — PDF may be image-only", bucket, key)

    return extracted


def chunk_text(text: str, max_chunk_size: int = MAX_CHUNK_SIZE, overlap: int = CHUNK_OVERLAP) -> List[Dict[str, Any]]:
    """Split text into overlapping word-based chunks for embedding generation.

    Overlapping windows ensure that context spanning chunk boundaries is
    captured in at least one chunk, improving RAG retrieval accuracy.

    Args:
        text: The full document text to split.
        max_chunk_size: Maximum number of words per chunk (default: ``MAX_CHUNK_SIZE``).
        overlap: Number of words shared between consecutive chunks (default: ``CHUNK_OVERLAP``).

    Returns:
        A list of chunk dicts, each with keys:
        - ``text`` (str): The chunk content.
        - ``index`` (int): Zero-based position in the document.
        - ``start_position`` (int): Word index where this chunk starts.
        - ``end_position`` (int): Word index where this chunk ends (exclusive).
    """
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
    """Generate a 1024-dimensional embedding vector using Amazon Bedrock Titan Embeddings V2.

    Truncates input to 25 000 characters (≈ 6 250 tokens) to stay within the
    model's 8 192-token context window.  Falls back to a zero vector on error so
    a single bad chunk does not abort the entire document ingestion.

    Args:
        text: The text to embed.

    Returns:
        A list of 1024 floats representing the embedding.  A zero vector
        ``[0.0] * 1024`` is returned if the Bedrock call fails.
    """
    try:
        # Prepare request for Titan Embeddings V2
        # Titan V2 supports up to 8192 tokens (~32768 characters for English)
        # Using 25000 characters as a conservative estimate (1 token ≈ 4 chars)
        request_body = json.dumps({
            "inputText": text[:25000],
            "dimensions": 1024,  # Match pgvector dimension (vector1024 migration)
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
        # Return zero vector as fallback (must match 1024-dim schema from SwitchToBedrockEmbeddings_vector1024 migration)
        return [0.0] * 1024


def store_chunks_in_db(document_id: str, chunks: List[Dict[str, Any]], file_name: str):
    """Upsert document metadata and all embedding chunks into PostgreSQL.

    Uses an INSERT … ON CONFLICT DO UPDATE strategy so re-processing a document
    is idempotent.  The function writes to two EF Core-managed tables:

    - ``DocumentMetadatas`` — one row per document (upsert by primary key).
    - ``DocumentChunks`` — one row per chunk, with the ``EmbeddingVector`` stored
      as a pgvector ``vector(1024)`` literal.

    After inserting all chunks the document's ``IsProcessed`` flag is set to
    ``True`` and ``ProcessedAt`` is recorded.

    Args:
        document_id: UUID string identifying the document (must match the
            ``DocumentMetadatas.Id`` column).
        chunks: List of chunk dicts produced by :func:`chunk_text`.
        file_name: Original file name stored in ``DocumentMetadatas.FileName``.

    Raises:
        psycopg2.Error: On any database error; the transaction is rolled back
            before re-raising so the caller can decide on retry logic.
    """
    conn = get_db_connection()
    cursor = conn.cursor()
    now = datetime.now(timezone.utc)

    try:
        # Upsert into DocumentMetadatas using EF Core column names
        cursor.execute("""
            INSERT INTO "DocumentMetadatas"
                ("Id", "FileName", "OriginalFileName", "ContentType", "FileSize",
                 "DocumentType", "StorageKey", "UploadedAt", "IsProcessed")
            VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)
            ON CONFLICT ("Id") DO UPDATE
                SET "IsProcessed" = EXCLUDED."IsProcessed"
        """, (document_id, file_name, file_name, 'application/pdf', 0,
               'pdf', '', now, False))

        # Generate embeddings and store chunks using EF Core column names
        chunk_data = []
        for chunk in chunks:
            embedding = get_embedding(chunk['text'])
            chunk_id = str(uuid.uuid4())
            chunk_data.append((
                chunk_id,
                document_id,
                chunk['text'],
                1,                  # PageNumber — Lambda has no page info, default to 1
                chunk['index'],     # ChunkIndex
                '[' + ','.join(str(v) for v in embedding) + ']',  # EmbeddingVector as pgvector literal
                now
            ))

        execute_values(
            cursor,
            """
            INSERT INTO "DocumentChunks"
                ("Id", "DocumentMetadataId", "ChunkText", "PageNumber",
                 "ChunkIndex", "EmbeddingVector", "CreatedAt")
            VALUES %s
            ON CONFLICT ("Id") DO UPDATE
                SET "ChunkText" = EXCLUDED."ChunkText",
                    "EmbeddingVector" = EXCLUDED."EmbeddingVector"
            """,
            chunk_data,
            template='(%s, %s, %s, %s, %s, %s::vector, %s)'
        )

        # Mark document as processed
        cursor.execute("""
            UPDATE "DocumentMetadatas"
            SET "IsProcessed" = %s, "ProcessedAt" = %s
            WHERE "Id" = %s
        """, (True, now, document_id))

        conn.commit()
        logger.info(f"Successfully stored {len(chunks)} chunks for document {document_id}")

    except Exception as e:
        conn.rollback()
        logger.error(f"Error storing chunks in database: {str(e)}")
        raise
    finally:
        cursor.close()


def handler(event, context):
    """Lambda entry point — process one or more S3 document events.

    Triggered by an S3 ``ObjectCreated`` event.  For each record the function:

    1. Fetches the PDF from S3 via :func:`extract_text_from_pdf`.
    2. Splits it into overlapping word chunks via :func:`chunk_text`.
    3. Generates Bedrock embeddings and persists everything via :func:`store_chunks_in_db`.

    Args:
        event (dict): Standard Lambda event payload.  Expected to contain an
            ``event['Records']`` list of S3 event records as documented at
            https://docs.aws.amazon.com/lambda/latest/dg/with-s3.html.
        context (LambdaContext): Runtime context object provided by Lambda
            (used for logging; not accessed directly here).

    Returns:
        dict: A response body with ``statusCode`` 200 and a ``body`` JSON string
        containing ``{ "message": "...", "chunks": <int> }`` on success, or
        ``statusCode`` 500 with an ``error`` string on failure.
    """
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
