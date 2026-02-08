#!/bin/bash
# Test script to verify Ollama is working and pull necessary models

echo "Testing Ollama connection..."

# Check if Ollama is running
if ! docker ps | grep -q smartglasses-ollama; then
    echo "ERROR: Ollama container is not running!"
    echo "Please start it with: docker-compose up -d ollama"
    exit 1
fi

echo "✓ Ollama container is running"

# Test Ollama endpoint
echo "Testing Ollama endpoint..."
if curl -s http://localhost:11434 > /dev/null; then
    echo "✓ Ollama is accessible at http://localhost:11434"
else
    echo "ERROR: Cannot reach Ollama at http://localhost:11434"
    exit 1
fi

# Pull the chat model if needed
echo ""
echo "Checking chat model: llama-3.2-3b..."
docker exec smartglasses-ollama ollama list | grep -q "llama-3.2-3b" || {
    echo "Pulling llama-3.2-3b model (this may take a while)..."
    docker exec smartglasses-ollama ollama pull llama-3.2-3b
}
echo "✓ Chat model llama-3.2-3b is available"

# Pull the embedding model if needed
echo ""
echo "Checking embedding model: nomic-embed-text..."
docker exec smartglasses-ollama ollama list | grep -q "nomic-embed-text" || {
    echo "Pulling nomic-embed-text model (this may take a while)..."
    docker exec smartglasses-ollama ollama pull nomic-embed-text
}
echo "✓ Embedding model nomic-embed-text is available"

echo ""
echo "All Ollama checks passed! ✓"
echo ""
echo "Available models:"
docker exec smartglasses-ollama ollama list
