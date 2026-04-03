import json
import os
import uuid
import boto3
import fitz  # PyMuPDF — extracts text from searchable PDFs without OCR
import logging
from typing import List, Dict, Any
from urllib.parse import unquote_plus
import pg8000
from datetime import datetime, timezone

# Initialize AWS clients
s3_client = boto3.client('s3')
secrets_client = boto3.client('secretsmanager')
textract_client = boto3.client('textract')

logger = logging.getLogger()
logger.setLevel(logging.INFO)

# Configuration
DOCUMENTS_BUCKET = os.environ['DOCUMENTS_BUCKET']
DB_SECRET_ARN = os.environ['DB_SECRET_ARN']
MAX_CHUNK_SIZE = int(os.environ.get('MAX_CHUNK_SIZE', 1000))
CHUNK_OVERLAP = int(os.environ.get('CHUNK_OVERLAP', 200))
MIN_TEXTRACT_CONFIDENCE = 10  # minimum Textract word confidence (0-100) to include in output

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
    db_connection = pg8000.connect(
        host=secret['host'],
        port=secret['port'],
        database=secret['dbname'],
        user=secret['username'],
        password=secret['password']
    )
    
    return db_connection



def create_searchable_pdf_from_image(bucket: str, key: str) -> str:
    """Download an image from S3, extract text using AWS Textract, and create a text-searchable PDF.
    Returns the extracted text, saves the generated PDF back to S3 with a .pdf extension, and deletes the original image."""
    # Use Textract to detect text directly from S3 — no local OCR binary required.
    textract_response = textract_client.detect_document_text(
        Document={'S3Object': {'Bucket': bucket, 'Name': key}}
    )

    # Fetch image bytes for embedding into the PDF
    obj = s3_client.get_object(Bucket=bucket, Key=key)
    image_bytes = obj['Body'].read()

    # Create a new PDF
    doc = fitz.open()

    # Open image with fitz to get dimensions
    img_doc = fitz.open(stream=image_bytes, filetype=key.split('.')[-1])
    img_page = img_doc[0]
    rect = img_page.rect

    # Insert a new page with the image dimensions
    page = doc.new_page(width=rect.width, height=rect.height)

    # Insert the image
    page.insert_image(rect, stream=image_bytes)

    extracted_text_parts = []
    for block in textract_response.get('Blocks', []):
        if block['BlockType'] == 'WORD' and block.get('Confidence', 0) > 10:
            text = block['Text']
            extracted_text_parts.append(text)

            # Textract returns normalized bounding box values in the range [0, 1]
            bbox = block['Geometry']['BoundingBox']
            x = bbox['Left'] * rect.width
            y = bbox['Top'] * rect.height
            w = bbox['Width'] * rect.width
            h = bbox['Height'] * rect.height

            fontsize = h * 0.8 if h > 0 else 12
            try:
                # render_mode=3 (invisible text) makes the PDF text-searchable
                page.insert_text(fitz.Point(x, y + h), text, fontsize=fontsize, render_mode=3)
            except Exception as e:
                logger.warning(f"Failed to insert text {text}: {e}")

    extracted_text = '\n'.join(extracted_text_parts).strip()

    # Save the new PDF to S3
    pdf_bytes = doc.tobytes()
    pdf_key = key.rsplit('.', 1)[0] + '.pdf'

    s3_client.put_object(
        Bucket=bucket,
        Key=pdf_key,
        Body=pdf_bytes,
        ContentType='application/pdf'
    )
    logger.info(f"Created text-searchable PDF from image and saved to s3://{bucket}/{pdf_key}")

    # Delete the original image file
    s3_client.delete_object(Bucket=bucket, Key=key)
    logger.info(f"Deleted original image file s3://{bucket}/{key}")

    return extracted_text











def update_document_metadata(document_id: str, file_name: str, storage_key: str):
    """Upsert document metadata into PostgreSQL.

    Uses an INSERT … ON CONFLICT DO UPDATE strategy so re-processing a document
    is idempotent.  The function writes to two EF Core-managed tables:

    - ``DocumentMetadatas`` — one row per document (upsert by primary key).
    - ``ManagedFiles`` — update if it exists.

    After updating, the document's ``IsProcessed`` flag is set to
    ``True`` and ``ProcessedAt`` is recorded.

    Args:
        document_id: UUID string identifying the document.
        file_name: Original file name.
        storage_key: The storage key of the generated PDF in S3.

    Raises:
        psycopg2.Error: On any database error; the transaction is rolled back.
    """
    conn = get_db_connection()
    cursor = conn.cursor()
    now = datetime.now(timezone.utc)

    try:
        # Upsert into DocumentMetadatas
        cursor.execute("""
            INSERT INTO "DocumentMetadatas"
                ("Id", "FileName", "OriginalFileName", "ContentType", "FileSize",
                 "DocumentType", "StorageKey", "UploadedAt", "IsProcessed")
            VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)
            ON CONFLICT ("Id") DO UPDATE
                SET "IsProcessed" = EXCLUDED."IsProcessed",
                    "FileName" = EXCLUDED."FileName",
                    "StorageKey" = EXCLUDED."StorageKey",
                    "ContentType" = EXCLUDED."ContentType",
                    "DocumentType" = EXCLUDED."DocumentType"
        """, (document_id, file_name, file_name, 'application/pdf', 0,
               'pdf', storage_key, now, False))

        # Update ManagedFile as well if it exists
        cursor.execute("""
            UPDATE "ManagedFiles"
            SET "FileName" = %s, "StorageKey" = %s, "ContentType" = %s
            WHERE "Id" = %s
        """, (file_name, storage_key, 'application/pdf', document_id))

        # Mark document as processed
        cursor.execute("""
            UPDATE "DocumentMetadatas"
            SET "IsProcessed" = %s, "ProcessedAt" = %s
            WHERE "Id" = %s
        """, (True, now, document_id))

        conn.commit()
        logger.info(f"Successfully updated metadata for document {document_id}")

    except Exception as e:
        conn.rollback()
        logger.error(f"Error updating database: {str(e)}")
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
            
            # Check if image or pdf
            is_image = key.lower().endswith(('.jpg', '.jpeg', '.png', '.webp', '.bmp'))
            
            # Extract text and process
            if is_image:
                text = create_searchable_pdf_from_image(bucket, key)
                logger.info(f"Extracted {len(text)} characters from image via Textract")
                # Change the file_name for DB insertion to .pdf since we uploaded a converted PDF
                file_name = file_name.rsplit('.', 1)[0] + '.pdf'
            else:
                logger.info(f"Document is already a PDF: s3://{bucket}/{key}")
            
            # Store in database
            update_document_metadata(document_id, file_name, f'uploads/{file_name}')
            
        return {
            'statusCode': 200,
            'body': json.dumps({
                'message': 'Document processed successfully'
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
