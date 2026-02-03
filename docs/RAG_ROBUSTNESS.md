# RAG Pipeline Robustness and Production Enhancements

## Overview

This document describes the robustness, restart tolerance, and production-ready enhancements made to the RAG (Retrieval-Augmented Generation) pipeline.

## Key Enhancements

### 1. ResourceGroup Integration

The RAG pipeline now supports ResourceGroups, allowing documents to be organized and queried within specific contexts.

#### Features:
- **Document-Group Association**: Documents can be assigned to one or more ResourceGroups during upload
- **Group-Filtered Queries**: Queries can be scoped to specific ResourceGroups
- **Contextual Prompts**: ManagedPrompts from ResourceGroups are automatically included as context when querying
- **Group-Based Organization**: Documents are organized by groups for better management

#### Usage:

**Upload with Groups:**
```bash
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@document.pdf" \
  -F "groupIds=group-uuid-1" \
  -F "groupIds=group-uuid-2"
```

**Query with Group Context:**
```bash
curl -X POST http://localhost:5000/api/documents/query \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What are the requirements?",
    "topK": 5,
    "groupIds": ["group-uuid-1", "group-uuid-2"]
  }'
```

### 2. Retry Logic and Error Handling

Enhanced error handling with automatic retry mechanisms for transient failures.

#### Features:
- **Exponential Backoff**: Failed embedding generations retry with increasing delays
- **Configurable Retries**: Maximum retry attempts configurable via `RAG:MaxRetries` (default: 3)
- **Partial Success Handling**: Document processing continues even if some chunks fail
- **Detailed Logging**: Comprehensive logging of failures and retry attempts

#### Configuration:
```json
{
  "RAG": {
    "MaxRetries": 3,
    "RetryDelayMs": 1000
  }
}
```

### 3. Restart Tolerance

The system now automatically recovers from failures and processes pending documents on restart.

#### Features:
- **Background Processing Service**: Automatically processes unprocessed documents
- **Startup Recovery**: Checks for failed/unprocessed documents on application start
- **Periodic Checks**: Regular interval checks for pending documents
- **Graceful Degradation**: Upload succeeds even if embedding generation fails initially

#### Background Service:
- Runs on startup (after 10-second delay)
- Checks periodically (configurable interval, default: 15 minutes)
- Processes all unprocessed or failed documents
- Logs progress and results

#### Configuration:
```json
{
  "RAG": {
    "BackgroundProcessingIntervalMinutes": 15
  }
}
```

### 4. Production-Ready Features

#### File Validation
- **Size Limits**: Configurable maximum file size (default: 50MB)
- **Type Validation**: Document type validation before processing
- **Error Messages**: Clear, user-friendly error messages

#### Configuration:
```json
{
  "RAG": {
    "MaxFileSizeMB": 50
  }
}
```

#### Transaction Handling
- Proper database transaction management
- Rollback on failures
- Atomic operations for document-group associations

#### Comprehensive Logging
- Structured logging with correlation IDs
- Different log levels for different severity
- Performance metrics logging

## New API Endpoints

### Reprocess Failed Documents
Manually trigger reprocessing of failed documents:

```http
POST /api/documents/reprocess
```

**Response:**
```json
{
  "reprocessedCount": 5,
  "message": "Reprocessed 5 documents"
}
```

### Get Unprocessed Documents
List all documents that failed processing or are pending:

```http
GET /api/documents/unprocessed
```

**Response:**
```json
[
  {
    "id": "uuid",
    "fileName": "document.pdf",
    "uploadedAt": "2026-02-03T10:00:00Z",
    "processingError": "Embedding service timeout",
    "isProcessed": false
  }
]
```

## Database Schema Updates

### New Table: DocumentGroup
Join table for many-to-many relationship between DocumentMetadata and ResourceGroup:

```sql
CREATE TABLE DocumentGroup (
    DocumentMetadataId UUID NOT NULL,
    ResourceGroupId UUID NOT NULL,
    PRIMARY KEY (DocumentMetadataId, ResourceGroupId),
    FOREIGN KEY (DocumentMetadataId) REFERENCES DocumentMetadata(Id),
    FOREIGN KEY (ResourceGroupId) REFERENCES ResourceGroup(Id)
);
```

### Updated Models

**DocumentMetadata:**
- Added `DocumentGroups` navigation property
- Improved error tracking with `ProcessingError` field

**ResourceGroup:**
- Added `DocumentGroups` navigation property
- Now supports files, prompts, and documents

## Query Behavior Changes

### Without Groups (Backward Compatible)
Queries without `groupIds` search across all documents:
```json
{
  "query": "What is mentioned?",
  "topK": 5
}
```

### With Groups (Enhanced)
Queries with `groupIds` are scoped to those groups and include group context:
```json
{
  "query": "What is mentioned?",
  "topK": 5,
  "groupIds": ["group-uuid-1"]
}
```

**Enhanced Context Includes:**
1. Relevant document chunks from documents in specified groups
2. ManagedPrompts associated with specified groups
3. Additional metadata from group associations

## Monitoring and Health

### Metrics to Monitor
- Number of unprocessed documents
- Embedding generation success rate
- Background service execution frequency
- Document processing time
- Query latency by group

### Health Indicators
- Background service status
- Number of pending documents
- Recent processing errors
- Embedding service availability

## Best Practices

### Document Upload
1. Always specify relevant groups during upload
2. Monitor upload success but don't block on embedding generation
3. Check unprocessed documents periodically
4. Set appropriate file size limits

### Querying
1. Use group filtering for focused searches
2. Adjust `topK` based on expected result count
3. Include relevant groups for better context
4. Handle cases where no results are found

### Error Recovery
1. Monitor the unprocessed documents endpoint
2. Use the reprocess endpoint after fixing issues
3. Check logs for systematic failures
4. Adjust retry configuration if needed

## Migration Guide

### Existing Deployments

1. **Database Migration**: The application will auto-create the new `DocumentGroup` table on startup (using `EnsureCreated`)

2. **Existing Documents**: Documents uploaded before this update will:
   - Continue to work without groups
   - Can be manually associated with groups via admin interface
   - Are searchable in ungrouped queries

3. **Configuration**: Add new RAG configuration options:
```json
{
  "RAG": {
    "MaxRetries": 3,
    "RetryDelayMs": 1000,
    "MaxFileSizeMB": 50,
    "BackgroundProcessingIntervalMinutes": 15
  }
}
```

4. **Restart Required**: Deploy and restart the application to activate the background service

## Troubleshooting

### Documents Not Processing
1. Check application logs for errors
2. Verify embedding service is available
3. Check the `/api/documents/unprocessed` endpoint
4. Manually trigger reprocessing with `/api/documents/reprocess`

### Group Context Not Appearing
1. Verify groups exist and contain prompts
2. Ensure documents are associated with groups
3. Check that `groupIds` are included in query request
4. Review logs for group resolution issues

### Performance Issues
1. Reduce `MaxRetries` if embedding service is slow
2. Increase `BackgroundProcessingIntervalMinutes` to reduce load
3. Consider implementing pagination for large result sets
4. Monitor database query performance

## Security Considerations

- File size limits prevent denial-of-service attacks
- Group-based access control ready for future authorization
- Input validation on all endpoints
- Proper error handling prevents information leakage
- Transaction boundaries prevent data corruption

## Future Enhancements

Potential improvements for consideration:
1. Concurrent processing of multiple documents
2. Priority queue for important documents
3. Group-based access control and permissions
4. Advanced retry strategies (circuit breaker, etc.)
5. Metrics dashboard for monitoring
6. Document versioning within groups
7. Automatic group assignment based on content
