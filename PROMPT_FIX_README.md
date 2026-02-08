# Prompt Page Fix - Documentation

## Summary of Changes

I've fixed **three critical issues** that were preventing the prompt page from working and requests from reaching Ollama:

### 1. **SignalR Broadcasting Was Disabled** ❌ → ✅
- **Problem**: Requests weren't showing up in monitoring because SignalR was disabled to prevent deadlocks
- **Fix**: Re-enabled with proper async handling using `Task.Run` to prevent blocking
- **File**: `src/SmartGlassesMiddleware/Services/RequestMonitoringService.cs`

### 2. **No Embedding Service Registered** ❌ → ✅  
- **Problem**: Ollama wasn't receiving embedding requests because no embedding service was configured in Semantic Kernel
- **Fix**: Added `AddOpenAITextEmbeddingGeneration` to register the embedding service with nomic-embed-text model
- **File**: `src/SmartGlassesMiddleware/Program.cs`

### 3. **No Timeout Configuration** ❌ → ✅
- **Problem**: Requests would hang indefinitely if Ollama was slow or unavailable
- **Fix**: Added HttpClient with 30-second timeout for both chat and embedding requests
- **Files**: `src/SmartGlassesMiddleware/Program.cs`, `appsettings.json`, `docker-compose.yml`

### 4. **Improved Logging** 📊
- Added detailed logging throughout the request pipeline to diagnose issues
- **Files**: `DocumentsController.cs`, `RagService.cs`

---

## How to Test

### Quick Test (Recommended)
Run this script which does everything automatically:

```powershell
.\quick-test.ps1
```

This script will:
1. ✅ Check Docker is running
2. ✅ Start all containers
3. ✅ Wait for services to be healthy
4. ✅ Pull required Ollama models
5. ✅ Test Ollama connectivity
6. ✅ Open monitoring page
7. ✅ Send a test query to the API

### Manual Testing

#### Step 1: Start Services
```powershell
docker-compose up -d --build
```

#### Step 2: Pull Ollama Models
```powershell
# Chat model
docker exec smartglasses-ollama ollama pull llama-3.2-3b

# Embedding model (REQUIRED for prompt page to work)
docker exec smartglasses-ollama ollama pull nomic-embed-text
```

#### Step 3: Test the Prompt Page
1. Open http://localhost:5000/admin.html
2. Go to **Prompts** tab
3. Enter a query: "What is the weather today?"
4. Click **Send**
5. You should see a response within 5-10 seconds

#### Step 4: Verify Monitoring
1. Open http://localhost:5000/monitoring.html
2. You should see the "DocumentQuery" request appear in real-time
3. Status should change: Started → Processing → Completed

---

## Verifying Ollama Receives Requests

### Check Ollama Logs
```powershell
docker logs smartglasses-ollama -f --tail=50
```

You should see lines like:
```
[GIN] 2024/... | 200 |  POST "/api/embeddings"
[GIN] 2024/... | 200 |  POST "/api/chat"
```

### Check Application Logs  
```powershell
docker logs smartglasses-app -f --tail=100
```

Look for these log messages:
```
[Information] === DOCUMENT QUERY REQUEST RECEIVED ===
[Information] Attempting to generate embedding for text...
[Information] Embedding service found, calling Ollama for embedding generation...
[Information] Successfully generated embedding from Ollama (dimension: 768)
[Information] Generating response with local Ollama model...
[Information] Successfully received response from Ollama
```

---

## Troubleshooting

### Issue: "No embedding service registered in Kernel"
**Solution**: The Program.cs changes didn't apply. Rebuild the Docker image:
```powershell
docker-compose up -d --build app
```

### Issue: "Cannot connect to Ollama"
**Solutions**:
1. Check Ollama is running: `docker ps | findstr ollama`
2. Test Ollama directly: `curl http://localhost:11434/api/tags`
3. Check Ollama logs: `docker logs smartglasses-ollama`

### Issue: "Model not found: nomic-embed-text"
**Solution**: Pull the embedding model:
```powershell
docker exec smartglasses-ollama ollama pull nomic-embed-text
```

### Issue: Requests still timeout after 30 seconds
**Solution**: Increase timeout in `appsettings.json`:
```json
{
  "AI": {
    "OllamaTimeoutSeconds": 60,
    "OllamaEmbeddingTimeoutSeconds": 60
  }
}
```

### Issue: Monitoring page still empty
**Solutions**:
1. Hard refresh the page (Ctrl + Shift + R)
2. Check browser console for SignalR connection errors
3. Check app logs for "SignalR" errors

---

## What Should Happen Now

### ✅ Before Fix:
- ❌ Requests don't appear in monitoring
- ❌ Requests timeout after 120 seconds
- ❌ Ollama never receives requests
- ❌ Logs show "Using simple hash-based embedding fallback"

### ✅ After Fix:
- ✅ Requests appear in monitoring in real-time
- ✅ Requests complete in 5-15 seconds
- ✅ Ollama logs show incoming POST requests
- ✅ Logs show "Successfully generated embedding from Ollama"
- ✅ Logs show "Successfully received response from Ollama"

---

## Configuration Reference

### appsettings.json
```json
{
  "AI": {
    "OllamaEndpoint": "http://localhost:11434",
    "OllamaModel": "llama-3.2-3b",
    "OllamaEmbeddingModel": "nomic-embed-text",
    "OllamaTimeoutSeconds": 30,
    "OllamaEmbeddingTimeoutSeconds": 30
  },
  "RAG": {
    "QueryTimeoutSeconds": 120
  }
}
```

### Environment Variables (Docker)
```yaml
- AI__OllamaEndpoint=http://ollama:11434
- AI__OllamaModel=llama-3.2-3b
- AI__OllamaEmbeddingModel=nomic-embed-text
- AI__OllamaTimeoutSeconds=30
- AI__OllamaEmbeddingTimeoutSeconds=30
```

---

## Files Changed

| File | Purpose |
|------|---------|
| `RequestMonitoringService.cs` | Re-enabled SignalR broadcasting |
| `Program.cs` | Added embedding service + timeouts |
| `RagService.cs` | Added detailed logging |
| `DocumentsController.cs` | Added request tracking logs |
| `appsettings.json` | Added new config settings |
| `docker-compose.yml` | Added environment variables |
| `quick-test.ps1` | Automated test script |
| `test-prompt.ps1` | Manual API test script |

---

## Expected Log Output (Success)

When you send a prompt, you should see this sequence in the app logs:

```
[Info] → REQUEST START: POST /api/documents/query
[Info] === DOCUMENT QUERY REQUEST RECEIVED ===
[Info] Query: What is the weather today?
[Info] Starting monitoring request...
[Info] Monitoring request started with ID: abc-123
[Info] Calling RAG service QueryDocumentsAsync...
[Info] Attempting to generate embedding for text (length: 28)
[Info] Embedding service found, calling Ollama for embedding generation...
[Info] Successfully generated embedding from Ollama (dimension: 768)
[Info] Generating response with local Ollama model...
[Info] Chat service obtained, sending request to Ollama...
[Info] Successfully received response from Ollama (length: 245)
[Info] RAG service returned result. Answer length: 245
[Info] === DOCUMENT QUERY COMPLETED SUCCESSFULLY ===
[Info] ← REQUEST END: POST /api/documents/query - 200 (8456ms)
```

---

## Need Help?

Run the quick test script and share the output:
```powershell
.\quick-test.ps1 > test-results.txt
```

Then share `test-results.txt` along with:
- App logs: `docker logs smartglasses-app > app-logs.txt`
- Ollama logs: `docker logs smartglasses-ollama > ollama-logs.txt`

