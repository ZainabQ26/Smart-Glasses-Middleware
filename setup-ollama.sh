#!/bin/bash

# Wait for Ollama service to be ready
echo "Waiting for Ollama service to be ready..."
sleep 10

# Pull the llama2 model
echo "Pulling llama2 model..."
docker exec smartglasses-ollama ollama pull llama2

echo "Setup complete! The llama2 model is now available."
echo "You can now run the application with: dotnet run --project src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj"
