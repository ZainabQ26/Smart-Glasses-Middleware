# RAG Pipeline Testing Guide

This document provides instructions for testing the RAG (Retrieval-Augmented Generation) pipeline.

## Prerequisites

Make sure the following services are running:
- PostgreSQL
- MinIO
- Redis
- Ollama
- The Smart Glasses Middleware application

## Test Documents

Create some test documents to upload:

### 1. Simple Text File (test.txt)
```
This is a test document about smart glasses technology.

Smart glasses are wearable computers that add information to what the wearer sees.
They can display information hands-free and are useful in many industries.

The technology behind smart glasses includes:
- Optical displays
- Cameras
- Sensors
- Voice recognition
- AI integration

Applications of smart glasses:
1. Healthcare - Surgical assistance and patient data display
2. Manufacturing - Assembly instructions and quality control
3. Logistics - Warehouse navigation and inventory management
4. Education - Interactive learning experiences
```

### 2. PDF Document
Create a simple PDF with multiple pages containing technical information.

## Testing Steps

### Step 1: Upload a Document

```bash
# Create a test text file
cat > test.txt << 'EOF'
This is a test document about smart glasses technology.

Smart glasses are wearable computers that add information to what the wearer sees.
They can display information hands-free and are useful in many industries.

The technology behind smart glasses includes:
- Optical displays
- Cameras  
- Sensors
- Voice recognition
- AI integration

Applications of smart glasses:
1. Healthcare - Surgical assistance and patient data display
2. Manufacturing - Assembly instructions and quality control
3. Logistics - Warehouse navigation and inventory management
4. Education - Interactive learning experiences
EOF

# Upload the document
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@test.txt" \
  -w "\n" | jq .
```

**Expected Response:**
```json
{
  "id": "uuid",
  "fileName": "test.txt",
  "documentType": "txt",
  "totalPages": 1,
  "isProcessed": true,
  "processedAt": "2026-02-03T...",
  "chunkCount": 2
}
```

### Step 2: Query the Document

```bash
# Query about smart glasses
curl -X POST http://localhost:5000/api/documents/query \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What are smart glasses used for?",
    "topK": 3
  }' \
  -w "\n" | jq .
```

**Expected Response:**
```json
{
  "answer": "Based on the document, smart glasses are used in healthcare for surgical assistance...",
  "sources": [
    {
      "documentId": "uuid",
      "fileName": "test.txt",
      "pageNumber": 1,
      "chunkId": "uuid",
      "chunkText": "Applications of smart glasses: 1. Healthcare..."
    }
  ]
}
```

### Step 3: List Documents

```bash
# List all documents
curl -X GET "http://localhost:5000/api/documents?page=1&pageSize=10" \
  -w "\n" | jq .
```

### Step 4: Get Document Details

```bash
# Replace {documentId} with the actual ID from Step 1
curl -X GET "http://localhost:5000/api/documents/{documentId}" \
  -w "\n" | jq .
```

### Step 5: Highlight Document

```bash
# Highlight sections containing "smart glasses"
curl -X POST "http://localhost:5000/api/documents/{documentId}/highlight" \
  -H "Content-Type: application/json" \
  -d '{
    "searchText": "smart glasses"
  }' \
  --output highlighted_test.txt

# View the highlighted version
cat highlighted_test.txt
```

### Step 6: Test PDF Upload (if you have a PDF)

```bash
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@document.pdf" \
  -w "\n" | jq .
```

### Step 7: Delete Document

```bash
curl -X DELETE "http://localhost:5000/api/documents/{documentId}" \
  -w "\n"
```

**Expected Response:** HTTP 204 No Content

## Automated Test Script

Save this as `test-rag.sh`:

```bash
#!/bin/bash

API_URL="http://localhost:5000"

echo "=== RAG Pipeline Testing ==="
echo ""

# Create test file
echo "Creating test document..."
cat > /tmp/test_rag.txt << 'EOF'
Smart Glasses Technology Overview

Smart glasses are wearable computers that add information to what the wearer sees.
They combine optical displays, cameras, sensors, and AI to provide hands-free computing.

Key Features:
- Augmented Reality overlays
- Voice commands
- Real-time data display
- Camera and video recording

Use Cases:
1. Healthcare: Patient data and surgical guidance
2. Manufacturing: Assembly instructions
3. Logistics: Warehouse management
EOF

# Upload document
echo "Uploading document..."
UPLOAD_RESPONSE=$(curl -s -X POST "$API_URL/api/documents/upload" \
  -F "file=@/tmp/test_rag.txt")

DOC_ID=$(echo $UPLOAD_RESPONSE | jq -r '.id')
echo "Document uploaded with ID: $DOC_ID"
echo ""

# Wait for processing
sleep 2

# Query document
echo "Querying document..."
curl -s -X POST "$API_URL/api/documents/query" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What are the use cases for smart glasses?",
    "topK": 3
  }' | jq .
echo ""

# List documents
echo "Listing documents..."
curl -s -X GET "$API_URL/api/documents?page=1&pageSize=5" | jq .
echo ""

# Get document details
echo "Getting document details..."
curl -s -X GET "$API_URL/api/documents/$DOC_ID" | jq .
echo ""

# Cleanup
echo "Cleaning up..."
curl -s -X DELETE "$API_URL/api/documents/$DOC_ID"
rm /tmp/test_rag.txt
echo "Test completed!"
```

Make it executable and run:
```bash
chmod +x test-rag.sh
./test-rag.sh
```

## Troubleshooting

### Document not processing
- Check that all dependencies are running (PostgreSQL, MinIO, Redis)
- Check application logs for errors
- Verify file upload size limits

### Empty search results
- Ensure embeddings are generated (check logs)
- Try keyword-based search by querying with exact terms from document
- Check that document chunks were created

### Highlighting not working
- Verify document type is supported (PDF, image)
- Check that positional data exists in chunks
- Review application logs for errors

## Performance Testing

Test with larger documents:

```bash
# Generate a large test file
for i in {1..100}; do
  echo "Section $i: This is test content for section $i about various topics." >> large_test.txt
done

# Upload and test
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@large_test.txt" \
  -w "\n" | jq .
```

## Expected Behavior

1. **Upload**: Should complete in < 5 seconds for small documents
2. **Query**: Should return results in < 3 seconds
3. **Highlight**: Should generate highlighted copy in < 2 seconds
4. **Persistence**: Documents should survive application restarts
5. **Accuracy**: Search results should be relevant to queries

## Security Considerations

- Ensure file upload size limits are configured
- Validate file types before processing
- Check for malicious content in documents
- Implement rate limiting on upload endpoint
