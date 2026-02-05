# API Conventions and Best Practices

This document outlines the conventions and best practices for the Smart Glasses Middleware API.

## Table of Contents

- [Route Naming](#route-naming)
- [HTTP Methods](#http-methods)
- [HTTP Status Codes](#http-status-codes)
- [Request and Response Format](#request-and-response-format)
- [Error Handling](#error-handling)
- [Headers](#headers)
- [Pagination](#pagination)
- [Versioning](#versioning)
- [Security](#security)
- [Rate Limiting](#rate-limiting)

## Route Naming

### Base URL Structure

All API endpoints follow the pattern:
```
/api/{resource}
```

### Conventions

1. **Lowercase with Hyphens**: All route segments use lowercase letters with hyphens for multi-word resources
   - ✅ `/api/ai-interactions`
   - ❌ `/api/AIInteractions`
   - ❌ `/api/ai_interactions`

2. **Plural Resource Names**: Resource collections use plural nouns
   - ✅ `/api/documents`
   - ✅ `/api/conversations`
   - ❌ `/api/document`

3. **Hierarchical Structure**: Related resources follow a hierarchical pattern
   - ✅ `/api/conversations/{id}/messages`
   - ✅ `/api/documents/{id}/highlight`
   - ❌ `/api/conversation-messages`

4. **Avoid Verbs in Routes**: Use HTTP methods instead of verbs in URLs
   - ✅ `POST /api/documents/{id}/highlight`
   - ❌ `POST /api/documents/highlight-document/{id}`

5. **Action Subresources**: When actions don't fit CRUD, use action subresources
   - ✅ `POST /api/documents/{id}/highlight` (action on a resource)
   - ✅ `POST /api/admin/cache/invalidate` (action endpoint)
   - ❌ `POST /api/documents/reprocess` (verb in path)

## HTTP Methods

### Standard CRUD Operations

- **GET**: Retrieve a resource or collection
  - `GET /api/documents` - List documents
  - `GET /api/documents/{id}` - Get a specific document

- **POST**: Create a new resource
  - `POST /api/documents/upload` - Create a new document
  - Returns `201 Created` with `Location` header

- **PUT**: Update (replace) an entire resource
  - `PUT /api/admin/prompts/{id}` - Replace a prompt
  - Should be idempotent

- **PATCH**: Partially update a resource
  - `PATCH /api/resource/{id}` - Update specific fields
  - Should be idempotent

- **DELETE**: Remove a resource
  - `DELETE /api/documents/{id}` - Delete a document
  - Returns `204 No Content` on success

### Non-CRUD Operations

For operations that don't fit CRUD:
- Use `POST` to action subresources
- Make the intent clear in the route
- Document idempotency behavior

Examples:
- `POST /api/conversations/{id}/archive` - Archive a conversation
- `POST /api/admin/cache/invalidate` - Invalidate caches
- `POST /api/documents/query` - Query documents (complex operation with body)

## HTTP Status Codes

### Success Codes

- **200 OK**: Successful GET, PUT, PATCH, or POST that returns data
- **201 Created**: Successful POST that creates a resource
  - Must include `Location` header pointing to the new resource
- **202 Accepted**: Request accepted for async processing
  - Include `Location` header to check status
- **204 No Content**: Successful DELETE or operation with no response body

### Client Error Codes

- **400 Bad Request**: Invalid request format or validation error
- **401 Unauthorized**: Missing or invalid authentication
- **403 Forbidden**: Authenticated but not authorized
- **404 Not Found**: Resource doesn't exist
- **409 Conflict**: Resource conflict (e.g., duplicate)
- **422 Unprocessable Entity**: Semantic validation error
- **429 Too Many Requests**: Rate limit exceeded

### Server Error Codes

- **500 Internal Server Error**: Unexpected server error
- **503 Service Unavailable**: Service temporarily unavailable

## Request and Response Format

### Content Types

- **JSON**: Default format for requests and responses
  - Content-Type: `application/json`
  - Character encoding: UTF-8

- **Multipart Form Data**: For file uploads
  - Content-Type: `multipart/form-data`

### Response Structure

#### Success Response

```json
{
  "id": "123e4567-e89b-12d3-a456-426614174000",
  "field1": "value1",
  "field2": "value2"
}
```

#### Collection Response (with Pagination)

```json
{
  "data": [...],
  "pagination": {
    "currentPage": 1,
    "pageSize": 20,
    "totalCount": 100,
    "totalPages": 5,
    "hasPrevious": false,
    "hasNext": true
  }
}
```

## Error Handling

### RFC 7807 Problem Details

All error responses follow [RFC 7807](https://tools.ietf.org/html/rfc7807) Problem Details format:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "The request could not be processed",
  "instance": "/api/documents/upload",
  "requestId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
  "timestamp": "2024-01-01T12:00:00Z"
}
```

### Validation Errors

Validation errors include field-level details:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Failed",
  "status": 400,
  "detail": "One or more validation errors occurred",
  "instance": "/api/documents/upload",
  "requestId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
  "timestamp": "2024-01-01T12:00:00Z",
  "errors": {
    "file": ["File is required"],
    "fileName": ["File name is too long"]
  }
}
```

## Headers

### Request Headers

- **X-Correlation-ID** (optional): Client-provided correlation ID for tracing
  - If not provided, server generates one
  - Should be a UUID or similar unique identifier

- **X-API-Key** (conditional): API key for authentication
  - Required for `/metrics` endpoint in production

- **Content-Type**: Specifies request body format
  - `application/json` for JSON requests
  - `multipart/form-data` for file uploads

### Response Headers

All responses include:

- **X-Request-Id**: Unique identifier for this request
  - Used for debugging and log correlation
  - Format: W3C Trace Context format

- **X-Correlation-ID**: Correlation identifier
  - Same as request if provided, or server-generated
  - Used for distributed tracing

### Cache Control

Safe GET endpoints may include:
- **Cache-Control**: Cache directives
- **ETag**: Entity tag for conditional requests

## Pagination

### Query Parameters

- **page**: Page number (1-indexed)
  - Default: 1
  - Minimum: 1

- **pageSize** (or **limit**): Number of items per page
  - Default: 20
  - Maximum: 100
  - Minimum: 1

### Response

```json
{
  "data": [...],
  "pagination": {
    "currentPage": 2,
    "pageSize": 20,
    "totalCount": 150,
    "totalPages": 8,
    "hasPrevious": true,
    "hasNext": true
  }
}
```

## Versioning

### Current Approach

Currently, the API does not use explicit versioning in routes (e.g., `/v1/`).

### Future Versioning Strategy

When breaking changes are necessary:

1. **URL Path Versioning** (Recommended)
   ```
   /api/v1/documents
   /api/v2/documents
   ```

2. **Header Versioning** (Alternative)
   ```
   X-API-Version: 2
   ```

### Deprecation Policy

1. **Announcement**: Document deprecated endpoints in CHANGELOG
2. **Warning Period**: Maintain deprecated endpoints for at least one major version
3. **Removal**: Remove in next major version

## Security

### Authentication

- Admin endpoints (`/api/admin/*`) should require authentication
- Metrics endpoint (`/metrics`) requires API key in production

### Authorization

- Implement role-based or scope-based access control
- Document required scopes in OpenAPI specification

### Input Validation

- Validate all input on the server side
- Use data annotations and model validation
- Return `400 Bad Request` with detailed validation errors

### File Uploads

- Enforce maximum file size limits
- Validate file types
- Scan uploaded files for malware
- Use content-type validation

## Rate Limiting

### Default Limits

- **60 requests per minute** per client (by IP or user)

### Rate Limit Headers

When rate limiting is enabled, responses include:
- **X-RateLimit-Limit**: Maximum requests allowed
- **X-RateLimit-Remaining**: Requests remaining in current window
- **X-RateLimit-Reset**: Time when the rate limit resets

### Rate Limit Exceeded

When rate limited, API returns:
- **Status**: `429 Too Many Requests`
- **Response**: RFC 7807 Problem Details
- **Retry-After**: Seconds to wait before retrying

```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Please try again later.",
  "instance": "/api/documents",
  "retryAfter": "60"
}
```

## CORS

### Development

- Allows all origins (`*`)
- Allows all methods
- Allows all headers

### Production

- Restricts to configured allowed origins
- Configure in `appsettings.json`:
  ```json
  {
    "Cors": {
      "AllowedOrigins": [
        "https://app.example.com",
        "https://admin.example.com"
      ]
    }
  }
  ```

## Best Practices Summary

1. **Be Consistent**: Follow conventions consistently across all endpoints
2. **Be RESTful**: Use appropriate HTTP methods and status codes
3. **Be Secure**: Validate input, authenticate users, authorize actions
4. **Be Observable**: Use correlation IDs, log appropriately, expose metrics
5. **Be Documented**: Keep OpenAPI spec up-to-date, document breaking changes
6. **Be Backwards Compatible**: Avoid breaking changes when possible
7. **Be Explicit**: Clear error messages, detailed validation errors
8. **Be Efficient**: Use pagination, caching, and rate limiting appropriately

## Resources

- [REST API Design Best Practices](https://restfulapi.net/)
- [RFC 7807 - Problem Details for HTTP APIs](https://tools.ietf.org/html/rfc7807)
- [OpenAPI Specification](https://swagger.io/specification/)
- [Microsoft REST API Guidelines](https://github.com/microsoft/api-guidelines)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
