# API Usage Examples (simplified)

These examples show the minimal requests your client should send. The service will decide what to do: it auto-detects the user's intent and will route to an advanced model when needed.

Prerequisite: the app is running. See [GETTING_STARTED.md](GETTING_STARTED.md).

1) Text

Send plain text; the AI decides the task (e.g., time, reminder) or forwards to the advanced model if required.

Example:
```bash
curl -s -X POST http://localhost:5000/api/text \
  -H "Content-Type: application/json" \
  -d '{"text":"What's the weather like today?"}'
```

Typical response:
```json
{
  "task": "weather",
  "response": "It's sunny and 72°F in your area.",
  "usedAdvancedModel": false,
  "requestId": "..."
}
```

2) Audio

Send base64 audio; provide `transcription` if available. The AI will use the transcription or a placeholder.

Example:
```bash
curl -s -X POST http://localhost:5000/api/audio \
  -H "Content-Type: application/json" \
  -d '{"audioBase64":"<BASE64>", "transcription":"Call Alice at 4 PM"}'
```

Or upload audio directly via `/api/media` (multipart):

```bash
curl -s -X POST http://localhost:5000/api/media \
  -F "file=@/path/to/recording.wav" \
  -F "prompt=Transcribe and summarize this message" \
  -F "transcription=Call Alice at 4 PM" \
  http://localhost:5000/api/media
```

When you upload audio via `/api/media` you can optionally include `transcription`; the server forwards both `prompt` and `transcription` together with the media metadata to the AI.

3) Media (images/videos)

Upload the file; optional `prompt` helps the AI. The AI will analyze and decide whether to use the local model or the advanced model.

Example (image):
```bash
curl -s -X POST http://localhost:5000/api/media \
  -F "file=@/path/to/image.jpg" \
  -F "prompt=Describe this image and identify main objects" \
  http://localhost:5000/api/media
```

What the server sends to the AI
- The server uploads the media to MinIO and then forwards a single text payload to the AI that contains:
  - your `prompt` (or a generated placeholder)
  - `MediaStorageKey` (the MinIO object key)
  - `MediaFileName` and `MediaContentType`

Example of the text forwarded to the AI (for illustration):
```
Describe this image and identify main objects

MediaStorageKey: 3f1b2c4d-.../image.jpg
MediaFileName: image.jpg
MediaContentType: image/jpeg
```

The AI can use the `MediaStorageKey` to fetch the object from MinIO (server-side) if you implement that retrieval in the AI pipeline; at minimum the AI will have both your text and the media metadata together when deciding how to respond.

4) History

Get recent history:
```bash
curl -s http://localhost:5000/api/history?count=5
```

Notes
- The service auto-detects tasks and routes queries; clients should send natural requests only.
- Keep secrets in `secrets/` or environment variables; do not include credentials in requests.
- Text responses are cached for 5 minutes; audio/media are stored and referenced by key.

Examples in code (quick)

JavaScript:
```javascript
fetch('http://localhost:5000/api/text', {
  method: 'POST', headers: {'Content-Type':'application/json'},
  body: JSON.stringify({ text: 'Set a reminder for tomorrow 9am' })
}).then(r=>r.json()).then(console.log)
```

Python:
```python
import requests
print(requests.post('http://localhost:5000/api/text', json={'text':'What time is it?'}).json())
```
