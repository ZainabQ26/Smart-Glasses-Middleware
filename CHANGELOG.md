# Changelog

All notable changes to the Smart Glasses Middleware API will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Correlation ID Support**: The API now accepts and returns `X-Correlation-ID` headers for request tracing across services
  - If a client provides `X-Correlation-ID`, it will be preserved and returned in the response
  - If not provided, the server generates a unique correlation ID automatically
  - All responses include both `X-Request-Id` and `X-Correlation-ID` headers for debugging and log correlation

- **Admin Cache Management Endpoint**: New endpoint `POST /api/admin/cache/invalidate` for manual cache invalidation
  - Supports selective cache invalidation by providing a list of targets: `files`, `prompts`, `groups`
  - Invalidates all caches when no targets are specified
  - Returns `204 No Content` on success
  - Example request body:
    ```json
    {
      "targets": ["files", "prompts"]
    }
    ```

- **Contract Tests**: Added comprehensive contract tests to validate API consistency
  - Verifies controller routes match expected patterns
  - Ensures all routes use consistent naming conventions
  - Validates route structure and HTTP status codes
  - Confirms OpenAPI specification is valid and accessible
  - 10 new contract tests covering route validation

### Changed
- **Route Naming Standardization**: Updated routes for consistency and clarity
  - `/api/transcribe` → `/api/audio` (AudioController)
  - `/api/ask` → `/api/media` (MediaController)
  - All route paths now use lowercase consistently

- **HTTP Status Codes**: Improved semantic correctness of HTTP responses
  - Document upload (`POST /api/documents/upload`) now returns `201 Created` with `Location` header instead of `200 OK`
  - The Location header points to `GET /api/documents/{documentId}` for retrieving the created resource

- **OpenAPI Specification Sync**: Updated openapi.json to match runtime implementation
  - All paths converted to lowercase (e.g., `/api/Documents` → `/api/documents`)
  - Updated response codes to match implementation (`201 Created` for document uploads)
  - Added documentation for `/api/admin/cache/invalidate` endpoint
  - Enhanced `/health` and `/metrics` endpoint documentation with detailed schemas
  - Added global header documentation (`X-Request-Id`, `X-Correlation-ID`)
  - Added RFC 7807 Problem Details schemas for error responses
  - Added Operations tag for health/metrics endpoints

### Fixed
- Fixed inconsistency between runtime routes and OpenAPI specification
- Fixed case sensitivity issues in route paths
- Corrected HTTP status codes to follow REST best practices

### Testing
- All 465 existing tests continue to pass
- 7 tests skipped (integration tests requiring external services)
- 10 new contract tests added
- Updated 3 tests to expect `201 Created` instead of `200 OK` for document uploads

### Documentation
- Enhanced OpenAPI specification with:
  - Global header documentation
  - Error response schemas (ProblemDetails, ValidationProblemDetails)
  - Rate limiting information
  - Correlation ID usage examples
  - Authentication requirements for metrics endpoint
  - Detailed health check response schema

## [1.0.0] - 2024-01-01

### Initial Release
- Base API implementation with document processing, RAG, conversations, and AI interactions
- Health and metrics endpoints
- Admin endpoints for managing files, prompts, and groups
- Rate limiting and CORS support
- Background task processing
- SignalR real-time notifications

---

## Migration Guide

### Breaking Changes

#### Route Changes
If you're currently using these endpoints, update your API calls:

**Before:**
```
POST /api/ask              → Use POST /api/media
POST /api/transcribe       → Use POST /api/audio
```

**After:**
```
POST /api/media            # Media processing endpoint
POST /api/audio            # Audio transcription endpoint
```

#### Status Code Changes

Document upload now returns `201 Created` with a `Location` header:

**Before:**
```http
POST /api/documents/upload
Response: 200 OK
{
  "id": "123...",
  "fileName": "document.pdf",
  ...
}
```

**After:**
```http
POST /api/documents/upload
Response: 201 Created
Location: /api/documents/123...
{
  "id": "123...",
  "fileName": "document.pdf",
  ...
}
```

### New Features

#### Using Correlation IDs

To trace requests across services, include a correlation ID:

```http
GET /api/documents
X-Correlation-ID: 550e8400-e29b-41d4-a716-446655440000
```

The response will include the same correlation ID:

```http
200 OK
X-Correlation-ID: 550e8400-e29b-41d4-a716-446655440000
X-Request-Id: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00
```

#### Cache Management

To invalidate specific caches:

```http
POST /api/admin/cache/invalidate
Content-Type: application/json

{
  "targets": ["files", "prompts"]
}
```

To invalidate all caches:

```http
POST /api/admin/cache/invalidate
Content-Type: application/json

{}
```

### Recommendations

1. **Update API Clients**: Update any clients using the old routes (`/api/ask`, `/api/transcribe`)
2. **Handle 201 Created**: Update code that expects `200 OK` from document upload to handle `201 Created`
3. **Use Correlation IDs**: For production deployments, consider generating and passing correlation IDs for better observability
4. **Review OpenAPI Spec**: The updated OpenAPI specification at `/openapi/v1.json` (or `openapi.json` file) includes all the latest changes

### Deprecation Policy

The old routes (`/api/ask`, `/api/transcribe`) are no longer documented in the OpenAPI specification. While the current implementation maintains backward compatibility through the route changes, future versions may remove support for these paths entirely.

**Timeline:**
- **Current Release**: Routes changed, OpenAPI updated
- **Next Release**: Consider removing old route aliases if created
- **Future**: Full removal of deprecated endpoints

Please migrate to the new routes as soon as possible to avoid issues with future updates.
