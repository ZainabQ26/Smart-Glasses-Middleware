# RAG (Retrieval-Augmented Generation) Pipeline

This document describes the RAG pipeline implementation for document processing, text extraction, and intelligent querying.

> **Note**: For information about robustness enhancements, restart tolerance, and ResourceGroup integration, see [RAG_ROBUSTNESS.md](RAG_ROBUSTNESS.md).

## Overview

The RAG pipeline enables the Smart Glasses Middleware to:
- Extract text from various document formats (PDF, DOCX, TXT, images)
- Track positional information of text within documents
- Store document chunks with embeddings for semantic search
- Query documents using natural language
- Highlight specific sections in documents
- Maintain persistence across restarts
- Organize documents using ResourceGroups
- Automatically retry failed processing
- Include group-specific context in queries

## Features

### Document Processing

The system supports the following document types:

1. **PDF Files** (Digital)
   - Extracts text directly from digital PDFs
   - Maintains page numbers and positional information
   - OCR support for scanned PDFs (requires Tesseract configuration)

2. **DOCX Files**
   - Extracts text from Microsoft Word documents
   - Processes paragraphs and maintains structure

3. **Text Files**
   - Direct text extraction
   - Simple and efficient processing

4. **Images** (JPG, PNG, etc.)
   - OCR support (requires Tesseract configuration)
   - Placeholder implementation included

### Text Chunking

Documents are intelligently chunked for optimal retrieval:
- **Chunk Size**: 500 characters (configurable)
- **Chunk Overlap**: 50 characters (configurable)
- Smart boundary detection (sentence/word breaks)
- Preserves context between chunks

### Semantic Search

- Vector embeddings for each chunk
- Cosine similarity matching
- Fallback keyword search
- Configurable top-K results (default: 5)

### Document Highlighting

Generate highlighted copies of documents showing where specific information is located:
- PDF highlighting with yellow transparency
- Image highlighting with bounding boxes
- Original documents remain intact
- Returns a copy with highlights

## API Endpoints

### 1. Upload Document

Upload and process a document for RAG.

```http
POST /api/documents/upload
Content-Type: multipart/form-data

file: <document file>
```

**Response:**
```json
{
  "id": "guid",
  "fileName": "document.pdf",
  "documentType": "pdf",
  "totalPages": 5,
  "isProcessed": true,
  "processedAt": "2026-02-03T01:00:00Z",
  "chunkCount": 25
}
```

### 2. Query Documents

Query documents using natural language with RAG.

```http
POST /api/documents/query
Content-Type: application/json

{
  "query": "What is the main topic discussed?",
  "topK": 5
}
```

**Response:**
```json
{
  "answer": "The main topic discussed is...",
  "sources": [
    {
      "documentId": "guid",
      "fileName": "document.pdf",
      "pageNumber": 1,
      "chunkId": "guid",
      "chunkText": "Preview of relevant text..."
    }
  ]
}
```

### 3. Get Highlighted Document

Get a copy of a document with specific sections highlighted.

```http
POST /api/documents/{documentId}/highlight
Content-Type: application/json

{
  "chunkIds": ["guid1", "guid2"],
  "searchText": "optional search term"
}
```

**Response:** Binary file stream with highlighted document

### 4. List Documents

List all uploaded documents.

```http
GET /api/documents?page=1&pageSize=20
```

**Response:**
```json
{
  "totalCount": 50,
  "page": 1,
  "pageSize": 20,
  "documents": [
    {
      "id": "guid",
      "fileName": "document.pdf",
      "documentType": "pdf",
      "totalPages": 5,
      "fileSize": 1024000,
      "uploadedAt": "2026-02-03T01:00:00Z",
      "isProcessed": true,
      "processedAt": "2026-02-03T01:00:30Z",
      "chunkCount": 25
    }
  ]
}
```

### 5. Get Document Details

Get details of a specific document.

```http
GET /api/documents/{documentId}
```

### 6. Delete Document

Delete a document and all its chunks.

```http
DELETE /api/documents/{documentId}
```

## Configuration

Configure the RAG pipeline in `appsettings.json`:

```json
{
  "RAG": {
    "ChunkSize": 500,
    "ChunkOverlap": 50,
    "TopKResults": 5,
    "EnableOCR": false
  }
}
```

### Configuration Options

- **ChunkSize**: Number of characters per chunk (default: 500)
- **ChunkOverlap**: Number of overlapping characters between chunks (default: 50)
- **TopKResults**: Number of results to return from semantic search (default: 5)
- **EnableOCR**: Enable OCR for scanned documents (requires Tesseract setup)

## Database Schema

### DocumentMetadata Table

Stores metadata about uploaded documents.

| Column | Type | Description |
|--------|------|-------------|
| Id | Guid | Primary key |
| FileName | String | Current file name |
| OriginalFileName | String | Original upload name |
| ContentType | String | MIME type |
| StorageKey | String | MinIO storage key |
| UploadedAt | DateTime | Upload timestamp |
| FileSize | Long | File size in bytes |
| TotalPages | Int | Number of pages |
| DocumentType | String | pdf, docx, txt, image |
| IsProcessed | Boolean | Processing status |
| ProcessedAt | DateTime | Processing timestamp |
| ProcessingError | String | Error message if failed |

### DocumentChunk Table

Stores text chunks extracted from documents.

| Column | Type | Description |
|--------|------|-------------|
| Id | Guid | Primary key |
| DocumentMetadataId | Guid | Foreign key to DocumentMetadata |
| ChunkText | String | Extracted text chunk |
| PageNumber | Int | Page number in document |
| ChunkIndex | Int | Order within document |
| X, Y, Width, Height | Double | Positional bounding box |
| Embedding | String | JSON array of vector embedding |
| CreatedAt | DateTime | Creation timestamp |

## Architecture

### Services

1. **DocumentProcessingService**
   - Handles document upload and text extraction
   - Creates chunks with positional information
   - Supports multiple document formats

2. **RAGService**
   - Generates embeddings for chunks
   - Performs semantic search
   - Integrates with AI service for response generation

3. **DocumentHighlightService**
   - Creates highlighted copies of documents
   - Supports PDF and image highlighting
   - Preserves original documents

### Storage

- **PostgreSQL**: Document metadata and chunks
- **MinIO**: Original document files
- **Redis**: Response caching (via existing infrastructure)

## Usage Examples

### Upload and Query a Document

```bash
# Upload a document
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@document.pdf"

# Query the document
curl -X POST http://localhost:5000/api/documents/query \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What is discussed in section 3?",
    "topK": 5
  }'
```

### Get Highlighted Document

```bash
# Highlight specific text
curl -X POST http://localhost:5000/api/documents/{documentId}/highlight \
  -H "Content-Type: application/json" \
  -d '{
    "searchText": "important clause"
  }' \
  --output highlighted_document.pdf
```

## OCR Configuration (Optional)

To enable OCR for scanned documents and images:

1. Install Tesseract OCR engine
2. Download language data files (tessdata)
3. Update configuration:
   ```json
   {
     "RAG": {
       "EnableOCR": true,
       "TesseractDataPath": "/path/to/tessdata"
     }
   }
   ```

## Performance Considerations

- **Chunking**: Larger chunks provide more context but slower search
- **Embeddings**: Generation can be resource-intensive for large documents
- **Search**: Semantic search with embeddings is slower but more accurate than keyword search
- **Caching**: Responses are cached in Redis for faster subsequent queries

## Integration with Advanced AI

The RAG pipeline integrates seamlessly with the existing AI routing:
- When a query requires advanced reasoning, it's automatically forwarded to the advanced model
- Context from relevant document chunks is included in the prompt
- The advanced AI receives both the query and the retrieved context

## Future Enhancements

Potential improvements:
- Full Tesseract OCR integration
- Support for more document formats (Excel, PowerPoint)
- Advanced embedding models (e.g., OpenAI embeddings)
- Vector database integration (e.g., Pinecone, Weaviate)
- Multi-language support
- Document summarization
- Entity extraction and tagging
