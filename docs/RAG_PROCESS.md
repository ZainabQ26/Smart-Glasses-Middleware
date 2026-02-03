# RAG Process Documentation

This document provides a detailed explanation of the Retrieval-Augmented Generation (RAG) process implemented in the Smart-Glasses-Middleware.

## 1. Overview

The RAG pipeline is a sophisticated system that allows the AI to answer questions based on a collection of documents. It combines the power of large language models (LLMs) with a retrieval system that finds relevant information in your documents. This allows the AI to provide accurate answers on specific topics without needing to be retrained.

The process can be broken down into two main parts:

1.  **Document Ingestion**: Where documents are uploaded, processed, and stored in a way that makes them searchable.
2.  **Querying**: Where a user's question is used to find relevant information in the stored documents, and then an AI model is used to generate an answer based on that information.

## 2. Document Ingestion

The document ingestion process starts when a user uploads a document via the `POST /api/documents/upload` endpoint.

### 2.1. Supported Formats

The system supports a variety of document formats:
- PDF (both text-based and scanned images)
- DOCX
- TXT
- Images (PNG, JPG, etc.)

### 2.2. Processing Steps

1.  **Text Extraction**: The system extracts text and layout information from the document. For scanned documents and images, it uses Optical Character Recognition (OCR).
2.  **Chunking**: The extracted text is split into smaller chunks. This is done to ensure that the embeddings accurately represent the content of a specific part of the document. Each chunk is stored as a separate record in the database, linked to the original document.
3.  **Storage**:
    - The original document is stored in MinIO, an S3-compatible object storage service.
    - The document's metadata (file name, content type, etc.) and the extracted text chunks are stored in a PostgreSQL database.

## 3. Embedding Generation

After a document has been processed and chunked, the system generates a vector embedding for each chunk. An embedding is a numerical representation of the text that captures its semantic meaning.

1.  **Embedding Model**: The system uses an embedding model to convert each text chunk into a vector of numbers. The specific model used is configurable.
2.  **Storage**: The generated embeddings are stored in the PostgreSQL database alongside the text chunks. The system uses the `pgvector` extension for PostgreSQL, which allows for efficient storage and querying of vector embeddings.
3.  **Asynchronous Process**: Embedding generation is an asynchronous process that runs in the background. This means that a document might not be immediately available for querying after it's uploaded. The `GET /api/documents/unprocessed` endpoint can be used to check the status of document processing.

## 4. Querying

When a user submits a query via the `POST /api/documents/query` endpoint, the system performs the following steps:

1.  **Query Embedding**: The user's query is converted into a vector embedding using the same model that was used for the document chunks.
2.  **Similarity Search**: The system uses the query embedding to perform a similarity search on the stored document chunk embeddings. This is done using the `pgvector` extension in PostgreSQL, which can efficiently find the most similar vectors. The result is a list of the most relevant document chunks.
3.  **Context Building**: The text from the most relevant document chunks is combined to create a "context" for the AI model. This context is a block of text that contains the information the AI will use to answer the user's question. The system is careful to not exceed the token limit of the AI model, and it will truncate the context if necessary.

## 5. AI Response Generation

Once the context has been built, it's combined with the user's original query into a prompt that is sent to an AI model.

1.  **Model Selection**: The system first estimates the number of tokens in the prompt. If the number of tokens is above a certain threshold, the request is automatically sent to a more advanced (and expensive) AI model. Otherwise, it is sent to the local Ollama model.
2.  **Response Generation**: The AI model generates a response based on the provided context and the user's question.
3.  **Source Identification**: The system analyzes the AI's response to identify which of the source documents were actually used to generate the answer. This is done by looking for mentions of the document file names or page numbers in the response text.

The final response, along with the list of sources, is then returned to the user.

## 6. Configuration

The RAG service can be configured via the `appsettings.json` file, under the `"RAG"` section.

-   `MaxRetries`, `RetryDelayMs`: Configure the retry logic for embedding generation.
-   `MaxContextTokens`: The maximum number of tokens to use in the AI prompt.
-   `MaxLocalModelTokens`: The token threshold to automatically delegate to an advanced model.
-   `ApproximateTokensPerChar`: Used for estimating token counts.
-   `EmbeddingBatchSize`, `EmbeddingMaxConcurrency`: For controlling the embedding generation process.
-   `UseVectorDB`: To enable or disable `pgvector` search.
-   `VectorSimilarityThreshold`: For filtering vector search results.

## 7. API Endpoints

The following API endpoints are related to the RAG process:

-   `POST /api/documents/upload`: Upload a document for processing.
-   `POST /api/documents/query`: Query your documents.
-   `POST /api/documents/{documentId}/highlight`: Highlight a document based on chunk IDs or search text.
-   `GET /api/documents`: List all uploaded documents with pagination.
-   `GET /api/documents/{documentId}`: Get details of a specific document, including its chunks.
-   `DELETE /api/documents/{documentId}`: Delete a document.
-   `POST /api/documents/reprocess`: Reprocess documents that failed embedding generation.
-   `GET /api/documents/unprocessed`: List documents that are not yet processed.
-   `POST /api/documents/{id}/move`: Move a document between resource groups.
