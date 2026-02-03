using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using Microsoft.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Tests;

public class RAGServiceRobustnessTests
{
    private readonly Mock<ILogger<RAGService>> _mockLogger;
    private readonly Mock<IAIService> _mockAIService;
    private readonly Mock<Kernel> _mockKernel;
    private readonly ApplicationDbContext _context;
    private readonly RAGService _service;

    public RAGServiceRobustnessTests()
    {
        _mockLogger = new Mock<ILogger<RAGService>>();
        _mockAIService = new Mock<IAIService>();
        _mockKernel = new Mock<Kernel>();
        
        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        
        _service = new RAGService(
            _context,
            _mockLogger.Object,
            _mockAIService.Object,
            _mockKernel.Object
        );
    }

    [Fact]
    public async Task SearchChunksAsync_WithGroupIds_ShouldFilterByGroups()
    {
        // Arrange
        var group1Id = Guid.NewGuid();
        var group2Id = Guid.NewGuid();
        
        var doc1 = CreateTestDocument("doc1.txt", group1Id);
        var doc2 = CreateTestDocument("doc2.txt", group2Id);
        
        _context.DocumentMetadatas.AddRange(doc1, doc2);
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.SearchChunksAsync("test", 10, new List<Guid> { group1Id });

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, chunk => 
            Assert.Contains(chunk.DocumentMetadata.DocumentGroups, 
                dg => dg.ResourceGroupId == group1Id));
    }

    [Fact]
    public async Task GetUnprocessedDocumentsAsync_ShouldReturnUnprocessedDocs()
    {
        // Arrange
        var processedDoc = CreateTestDocument("processed.txt", Guid.NewGuid());
        processedDoc.IsProcessed = true;
        
        var unprocessedDoc = CreateTestDocument("unprocessed.txt", Guid.NewGuid());
        unprocessedDoc.IsProcessed = false;
        
        var failedDoc = CreateTestDocument("failed.txt", Guid.NewGuid());
        failedDoc.IsProcessed = true;
        failedDoc.ProcessingError = "Some error";
        
        _context.DocumentMetadatas.AddRange(processedDoc, unprocessedDoc, failedDoc);
        await _context.SaveChangesAsync();

        // Act
        var results = await _service.GetUnprocessedDocumentsAsync();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, d => d.Id == unprocessedDoc.Id);
        Assert.Contains(results, d => d.Id == failedDoc.Id);
    }

    [Fact]
    public async Task QueryDocumentsAsync_WithGroups_ShouldIncludeGroupContext()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        
        var prompt = new ManagedPrompt
        {
            Id = Guid.NewGuid(),
            Name = "Context Prompt",
            PromptText = "This is additional context from the group.",
            CreatedAt = DateTime.UtcNow
        };
        
        var promptGroup = new ManagedPromptGroup
        {
            ManagedPromptId = prompt.Id,
            ManagedPrompt = prompt,
            ResourceGroupId = groupId,
            ResourceGroup = group
        };
        
        group.PromptGroups.Add(promptGroup);
        
        var doc = CreateTestDocument("test.txt", groupId);
        
        _context.ResourceGroups.Add(group);
        _context.ManagedPrompts.Add(prompt);
        _context.DocumentMetadatas.Add(doc);
        await _context.SaveChangesAsync();

        _mockAIService
            .Setup(s => s.ProcessRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(("task", "response", false));

        // Act
        var result = await _service.QueryDocumentsAsync("test query", 5, new List<Guid> { groupId });

        // Assert
        Assert.NotNull(result);
        _mockAIService.Verify(
            s => s.ProcessRequestAsync(It.Is<string>(p => p.Contains("Context Prompt"))),
            Times.Once);
    }

    private DocumentMetadata CreateTestDocument(string fileName, Guid groupId)
    {
        var doc = new DocumentMetadata
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            OriginalFileName = fileName,
            ContentType = "text/plain",
            StorageKey = $"test/{fileName}",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000,
            DocumentType = "txt",
            IsProcessed = false,
            TotalPages = 1
        };

        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentMetadataId = doc.Id,
            DocumentMetadata = doc,
            ChunkText = "This is test content",
            PageNumber = 1,
            ChunkIndex = 0,
            CreatedAt = DateTime.UtcNow
        };

        doc.Chunks.Add(chunk);
        
        doc.DocumentGroups.Add(new DocumentGroup
        {
            DocumentMetadataId = doc.Id,
            DocumentMetadata = doc,
            ResourceGroupId = groupId
        });

        return doc;
    }
}
