# API Usage Examples

This document provides practical examples of using the Smart Glasses Middleware API.

## Prerequisites

Ensure the application is running. See [GETTING_STARTED.md](GETTING_STARTED.md) for setup instructions.

## Text Processing

### Simple Query
```bash
curl -X POST http://localhost:5000/api/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "What time is it?"
  }'
```

**Response:**
```json
{
  "task": "time",
  "response": "I can help you with the current time...",
  "usedStrongerModel": false,
  "requestId": "550e8400-e29b-41d4-a716-446655440000"
}
```

### Complex Query (Routes to Stronger Model)
```bash
curl -X POST http://localhost:5000/api/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Can you explain quantum computing in detail?"
  }'
```

**Response:**
```json
{
  "task": "unknown",
  "response": "Quantum computing is a type of computation...",
  "usedStrongerModel": true,
  "requestId": "550e8400-e29b-41d4-a716-446655440001"
}
```

## Audio Processing

### Audio with Transcription
```bash
curl -X POST http://localhost:5000/api/audio \
  -H "Content-Type: application/json" \
  -d '{
    "audioBase64": "UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=",
    "transcription": "Set a reminder for 3 PM"
  }'
```

**Response:**
```json
{
  "task": "reminder",
  "response": "I'll help you set a reminder for 3 PM...",
  "usedStrongerModel": false,
  "requestId": "550e8400-e29b-41d4-a716-446655440002"
}
```

## Media Processing

### Image Upload
```bash
curl -X POST http://localhost:5000/api/media \
  -F "file=@/path/to/image.jpg" \
  -F "description=What's in this image?"
```

**Response:**
```json
{
  "task": "unknown",
  "response": "I can see that this image contains...",
  "usedStrongerModel": true,
  "requestId": "550e8400-e29b-41d4-a716-446655440003"
}
```

### Video Upload
```bash
curl -X POST http://localhost:5000/api/media \
  -F "file=@/path/to/video.mp4" \
  -F "description=Analyze this video"
```

## History Retrieval

### Get Recent History
```bash
curl http://localhost:5000/api/history?count=5
```

**Response:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440003",
    "timestamp": "2024-01-25T10:30:00Z",
    "requestType": "Image",
    "inputText": "What's in this image?",
    "detectedTask": "unknown",
    "response": "I can see that this image contains...",
    "usedStrongerModel": true,
    "mediaStorageKey": "abc123/image.jpg"
  },
  {
    "id": "550e8400-e29b-41d4-a716-446655440002",
    "timestamp": "2024-01-25T10:25:00Z",
    "requestType": "Audio",
    "inputText": "Set a reminder for 3 PM",
    "detectedTask": "reminder",
    "response": "I'll help you set a reminder for 3 PM...",
    "usedStrongerModel": false,
    "mediaStorageKey": "xyz789/audio.wav"
  }
]
```

### Get Specific History Item
```bash
curl http://localhost:5000/api/history/550e8400-e29b-41d4-a716-446655440000
```

## Known Tasks

The following tasks are processed locally by the Ollama model:

| Task | Example Query |
|------|---------------|
| Weather | "What's the weather like?" |
| Time | "What time is it?" |
| Reminder | "Set a reminder for 5 PM" |
| Alarm | "Set an alarm for 7 AM" |
| Timer | "Start a 10-minute timer" |
| Note | "Take a note: Buy groceries" |
| Translation | "Translate 'hello' to Spanish" |
| Calculation | "What's 25 + 17?" |

All other queries are automatically routed to the stronger model.

## Error Handling

### Invalid Request
```bash
curl -X POST http://localhost:5000/api/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": ""
  }'
```

**Response (400 Bad Request):**
```json
{
  "error": "Text cannot be empty"
}
```

### Server Error
If the AI service is unavailable, you'll receive a 500 Internal Server Error:

```json
{
  "error": "An error occurred processing your request"
}
```

## Caching Behavior

- Text requests are cached for 5 minutes
- Identical text queries will return cached responses instantly
- Audio and media requests are not cached due to their unique nature

## Rate Limiting

Currently, there are no rate limits implemented. Consider adding rate limiting for production use.

## Testing with Different Tools

### Using Postman
1. Import the requests from `SmartGlassesMiddleware.http`
2. Update the base URL to `http://localhost:5000`
3. Execute requests

### Using JavaScript (Browser)
```javascript
fetch('http://localhost:5000/api/text', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
  },
  body: JSON.stringify({
    text: 'What time is it?'
  })
})
.then(response => response.json())
.then(data => console.log(data));
```

### Using Python
```python
import requests

response = requests.post(
    'http://localhost:5000/api/text',
    json={'text': 'What time is it?'}
)
print(response.json())
```
