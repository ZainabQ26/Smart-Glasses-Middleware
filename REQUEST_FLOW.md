# Request Flow: Prompt Page → Ollama

## Before Fix ❌

```
User (admin.html)
    │
    └─→ POST /api/documents/query
           │
           ├─→ MonitoringService.StartRequest() [❌ SignalR disabled - no broadcast]
           │
           └─→ RAGService.QueryDocumentsAsync()
                  │
                  ├─→ SearchChunksAsync()
                  │      │
                  │      └─→ GenerateQueryEmbeddingAsync()
                  │             │
                  │             └─→ GenerateTextEmbeddingAsync()
                  │                    │
                  │                    └─→ Kernel.GetService(ITextEmbeddingGenerationService)
                  │                           │
                  │                           └─→ ❌ Returns NULL (not registered)
                  │                                  │
                  │                                  └─→ Falls back to GenerateSimpleEmbedding() 
                  │                                         (SHA256 hash - not using Ollama!)
                  │
                  └─→ GenerateResponseWithLocalModel()
                         │
                         └─→ Kernel.GetService(IChatCompletionService)
                                │
                                └─→ ⏱️ HANGS (no timeout configured)
                                       Ollama may be slow/unavailable
                                       Request times out after 120 seconds
```

## After Fix ✅

```
User (admin.html)
    │
    └─→ POST /api/documents/query
           │
           ├─→ MonitoringService.StartRequest() 
           │      │
           │      └─→ ✅ SignalR.BroadcastUpdate() via Task.Run (non-blocking)
           │             │
           │             └─→ monitoring.html receives real-time update
           │
           └─→ RAGService.QueryDocumentsAsync()
                  │
                  ├─→ SearchChunksAsync()
                  │      │
                  │      └─→ GenerateQueryEmbeddingAsync()
                  │             │
                  │             └─→ GenerateTextEmbeddingAsync()
                  │                    │
                  │                    └─→ Kernel.GetService(ITextEmbeddingGenerationService)
                  │                           │
                  │                           └─→ ✅ Service found! (registered in Program.cs)
                  │                                  │
                  │                                  └─→ POST http://ollama:11434/v1/embeddings
                  │                                         Model: nomic-embed-text
                  │                                         Timeout: 30s
                  │                                         │
                  │                                         └─→ 📊 Ollama generates embedding
                  │                                                (dimension: 768)
                  │
                  └─→ GenerateResponseWithLocalModel()
                         │
                         └─→ Kernel.GetService(IChatCompletionService)
                                │
                                └─→ ✅ POST http://ollama:11434/v1/chat/completions
                                       Model: llama-3.2-3b
                                       Timeout: 30s ⏱️
                                       │
                                       └─→ 💬 Ollama generates response
                                              │
                                              └─→ MonitoringService.CompleteRequest()
                                                     │
                                                     └─→ monitoring.html shows "Completed"
```

## Key Changes

### 1. SignalR Broadcasting
- **Before**: Disabled to prevent deadlocks → No monitoring updates
- **After**: Enabled with `Task.Run` → Real-time updates

### 2. Embedding Service
- **Before**: Not registered → SHA256 fallback → Ollama never called
- **After**: Registered with `AddOpenAITextEmbeddingGeneration` → Ollama receives requests

### 3. Timeout Configuration  
- **Before**: No timeout → Hangs indefinitely on slow/unavailable Ollama
- **After**: 30-second timeout → Fails fast with clear error

## HTTP Requests to Ollama

### Embedding Request
```http
POST http://ollama:11434/v1/embeddings HTTP/1.1
Content-Type: application/json

{
  "model": "nomic-embed-text",
  "input": "What is the weather today?"
}
```

### Chat Completion Request
```http
POST http://ollama:11434/v1/chat/completions HTTP/1.1
Content-Type: application/json

{
  "model": "llama-3.2-3b",
  "messages": [
    {
      "role": "user",
      "content": "Based on the following context..."
    }
  ]
}
```

## Monitoring Timeline

### Before Fix
```
00:00  Request starts
00:120 ⏱️ Request times out (no response)
       ❌ Never appears in monitoring
```

### After Fix
```
00:00  ✅ "DocumentQuery" appears in monitoring (status: Started)
00:01  ✅ Status: "Searching documents..."
00:02  📊 Ollama generates embedding
00:04  💬 Ollama generates response
00:05  ✅ Status: "Completed" (duration: 5000ms)
       ✅ Response displayed in UI
```

## Logs Comparison

### Before Fix
```
[Warn] Embedding service not available
[Info] Using simple hash-based embedding fallback
[Error] Request timed out after 120 seconds
```

### After Fix  
```
[Info] Embedding service found, calling Ollama for embedding generation...
[Info] Successfully generated embedding from Ollama (dimension: 768)
[Info] Chat service obtained, sending request to Ollama...
[Info] Successfully received response from Ollama (length: 245)
[Info] === DOCUMENT QUERY COMPLETED SUCCESSFULLY ===
```
