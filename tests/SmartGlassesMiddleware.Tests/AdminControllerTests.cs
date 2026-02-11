using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests;

public class AdminControllerTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private AdminController CreateController(
        ApplicationDbContext? context = null,
        Mock<IMediaStorageService>? mediaStorageService = null,
        Mock<ICacheService>? cacheService = null,
        Mock<IRagService>? ragService = null,
        Mock<IRAGService>? documentRagService = null,
        Mock<ILogger<AdminController>>? logger = null,
        Mock<IRequestMonitoringService>? monitoringService = null)
    {
        var ctx = context ?? GetInMemoryContext();
        var mockMediaStorage = mediaStorageService ?? new Mock<IMediaStorageService>();
        var mockCache = cacheService ?? new Mock<ICacheService>();
        var mockRag = ragService ?? new Mock<IRagService>();
        var mockDocRag = documentRagService ?? new Mock<IRAGService>();
        var mockLogger = logger ?? new Mock<ILogger<AdminController>>();
        var mockMonitoring = monitoringService ?? new Mock<IRequestMonitoringService>();
        
        // Setup default monitoring service behavior to return a GUID
        mockMonitoring.Setup(m => m.StartRequest(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        mockMonitoring.Setup(m => m.UpdateRequestStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        mockMonitoring.Setup(m => m.CompleteRequest(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var controller = new AdminController(
            ctx,
            mockMediaStorage.Object,
            mockCache.Object,
            mockRag.Object,
            mockDocRag.Object,
            mockLogger.Object,
            mockMonitoring.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private IFormFile CreateMockFormFile(string fileName, string content = "test content", string contentType = "application/pdf")
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var formFile = new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
        return formFile;
    }

    #region Files Tests

    [Fact]
    public async Task GetFiles_Returns_Ok_With_Pagination()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var file1 = new ManagedFile
        {
            Id = Guid.NewGuid(),
            FileName = "test1.pdf",
            Label = "Test File 1",
            ContentType = "application/pdf",
            StorageKey = "key1",
            UploadedAt = DateTime.UtcNow.AddDays(-2),
            FileSize = 1000
        };
        var file2 = new ManagedFile
        {
            Id = Guid.NewGuid(),
            FileName = "test2.pdf",
            Label = "Test File 2",
            ContentType = "application/pdf",
            StorageKey = "key2",
            UploadedAt = DateTime.UtcNow.AddDays(-1),
            FileSize = 2000
        };

        context.ManagedFiles.AddRange(file1, file2);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.GetFiles(page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
        mockCache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task GetFiles_Returns_Cached_Result_When_Available()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var cachedData = new { Data = new[] { new { Id = Guid.NewGuid(), FileName = "cached.pdf" } } };
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync(cachedData);

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.GetFiles(page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(cachedData, okResult.Value);
        mockCache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task GetFiles_Validates_Pagination_Parameters()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var controller = CreateController(context, cacheService: mockCache);

        // Act - Test invalid page (< 1)
        var result = await controller.GetFiles(page: -1, pageSize: 10);

        // Assert - Should normalize to page 1
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task UploadFile_Returns_BadRequest_When_File_Is_Null()
    {
        // Arrange
        var controller = CreateController();
        var request = new FileUploadRequest { File = null, Label = "Test" };

        // Act
        var result = await controller.UploadFile(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("File is required", problemDetails.Detail);
    }

    [Fact]
    public async Task UploadFile_Returns_BadRequest_When_Label_Is_Empty()
    {
        // Arrange
        var controller = CreateController();
        var file = CreateMockFormFile("test.pdf");
        var request = new FileUploadRequest { File = file, Label = "" };

        // Act
        var result = await controller.UploadFile(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Label is required", problemDetails.Detail);
    }

    [Fact]
    public async Task UploadFile_Returns_Existing_File_When_Duplicate_Hash()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockCache = new Mock<ICacheService>();
        var mockRag = new Mock<IRagService>();

        var contentHash = "hash123";
        var existingFile = new ManagedFile
        {
            Id = Guid.NewGuid(),
            FileName = "existing.pdf",
            Label = "Existing",
            ContentType = "application/pdf",
            StorageKey = "existing-key",
            ContentHash = contentHash,
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ManagedFiles.Add(existingFile);
        await context.SaveChangesAsync();

        mockMediaStorage.Setup(m => m.ComputeContentHash(It.IsAny<Stream>())).Returns(contentHash);

        var controller = CreateController(context, mockMediaStorage, mockCache, mockRag);
        var file = CreateMockFormFile("duplicate.pdf");
        var request = new FileUploadRequest { File = file, Label = "Duplicate" };

        // Act
        var result = await controller.UploadFile(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        // Verify it's the existing file
        var savedFile = await context.ManagedFiles.FindAsync(existingFile.Id);
        Assert.NotNull(savedFile);
        Assert.Equal(existingFile.Id, savedFile.Id);
        mockMediaStorage.Verify(m => m.UploadMediaAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadFile_Uploads_New_File_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockCache = new Mock<ICacheService>();
        var mockRag = new Mock<IRagService>();

        var contentHash = "newhash123";
        var storageKey = "storage-key-123";

        mockMediaStorage.Setup(m => m.ComputeContentHash(It.IsAny<Stream>())).Returns(contentHash);
        mockMediaStorage.Setup(m => m.UploadMediaAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(storageKey);
        mockRag.Setup(r => r.ProcessFileForRagAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockCache, mockRag);
        var file = CreateMockFormFile("newfile.pdf", "new content");
        var request = new FileUploadRequest { File = file, Label = "New File" };

        // Act
        var result = await controller.UploadFile(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(createdResult.Value);

        mockMediaStorage.Verify(m => m.UploadMediaAsync(It.IsAny<Stream>(), "newfile.pdf", It.IsAny<string>()), Times.Once);
        mockRag.Verify(r => r.ProcessFileForRagAsync(It.IsAny<Guid>(), "newfile.pdf", It.IsAny<string>(), storageKey), Times.Once);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);

        // Verify file was saved to database
        var savedFile = await context.ManagedFiles.FirstOrDefaultAsync();
        Assert.NotNull(savedFile);
        Assert.Equal("newfile.pdf", savedFile.FileName);
    }

    [Fact]
    public async Task UploadFile_Continues_On_RAG_Processing_Failure()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockCache = new Mock<ICacheService>();
        var mockRag = new Mock<IRagService>();

        mockMediaStorage.Setup(m => m.ComputeContentHash(It.IsAny<Stream>())).Returns("hash");
        mockMediaStorage.Setup(m => m.UploadMediaAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("key");
        mockRag.Setup(r => r.ProcessFileForRagAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("RAG processing failed"));

        var controller = CreateController(context, mockMediaStorage, mockCache, mockRag);
        var file = CreateMockFormFile("test.pdf");
        var request = new FileUploadRequest { File = file, Label = "Test" };

        // Act
        var result = await controller.UploadFile(request);

        // Assert - Should still return success even if RAG fails
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(createdResult.Value);
    }

    [Fact]
    public async Task DeleteFile_Returns_NotFound_When_File_Not_Exists()
    {
        // Arrange
        var controller = CreateController();
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await controller.DeleteFile(nonExistentId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains(nonExistentId.ToString(), problemDetails.Detail);
    }

    [Fact]
    public async Task DeleteFile_Deletes_File_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockCache = new Mock<ICacheService>();

        var fileId = Guid.NewGuid();
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "test-key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        mockMediaStorage.Setup(m => m.DeleteMediaAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var controller = CreateController(context, mockMediaStorage, mockCache);

        // Act
        var result = await controller.DeleteFile(fileId);

        // Assert
        Assert.IsType<NoContentResult>(result);
        mockMediaStorage.Verify(m => m.DeleteMediaAsync("test-key"), Times.Once);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);

        // Verify file was deleted from database
        var deletedFile = await context.ManagedFiles.FindAsync(fileId);
        Assert.Null(deletedFile);
    }

    [Fact]
    public async Task DeleteFile_Returns_Error_When_Storage_Deletion_Fails()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();

        var fileId = Guid.NewGuid();
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "test-key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        mockMediaStorage.Setup(m => m.DeleteMediaAsync(It.IsAny<string>())).ThrowsAsync(new Exception("Storage error"));

        var controller = CreateController(context, mockMediaStorage);

        // Act
        var result = await controller.DeleteFile(fileId);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    #endregion

    #region Prompts Tests

    [Fact]
    public async Task GetPrompts_Returns_Ok_With_Pagination()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var prompt1 = new ManagedPrompt
        {
            Id = Guid.NewGuid(),
            Name = "Prompt 1",
            PromptText = "Text 1",
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
        var prompt2 = new ManagedPrompt
        {
            Id = Guid.NewGuid(),
            Name = "Prompt 2",
            PromptText = "Text 2",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        context.ManagedPrompts.AddRange(prompt1, prompt2);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.GetPrompts(page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
        mockCache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task GetPrompts_Returns_Cached_Result_When_Available()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        var cachedData = new { Data = new[] { new { Id = Guid.NewGuid(), Name = "Cached Prompt" } } };
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync(cachedData);

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.GetPrompts(page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(cachedData, okResult.Value);
    }

    [Fact]
    public async Task CreatePrompt_Returns_BadRequest_When_Name_Is_Empty()
    {
        // Arrange
        var controller = CreateController();
        var request = new PromptRequest { Name = "", PromptText = "Test" };

        // Act
        var result = await controller.CreatePrompt(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Name is required", problemDetails.Detail);
    }

    [Fact]
    public async Task CreatePrompt_Returns_BadRequest_When_PromptText_Is_Empty()
    {
        // Arrange
        var controller = CreateController();
        var request = new PromptRequest { Name = "Test", PromptText = "" };

        // Act
        var result = await controller.CreatePrompt(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Prompt text is required", problemDetails.Detail);
    }

    [Fact]
    public async Task CreatePrompt_Creates_Prompt_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var controller = CreateController(context, cacheService: mockCache);
        var request = new PromptRequest { Name = "New Prompt", PromptText = "This is a test prompt" };

        // Act
        var result = await controller.CreatePrompt(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(createdResult.Value);

        // Verify prompt was saved to database
        var savedPrompt = await context.ManagedPrompts.FirstOrDefaultAsync();
        Assert.NotNull(savedPrompt);
        Assert.Equal("New Prompt", savedPrompt.Name);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdatePrompt_Returns_NotFound_When_Prompt_Not_Exists()
    {
        // Arrange
        var controller = CreateController();
        var nonExistentId = Guid.NewGuid();
        var request = new PromptRequest { Name = "Updated", PromptText = "Updated text" };

        // Act
        var result = await controller.UpdatePrompt(nonExistentId, request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains(nonExistentId.ToString(), problemDetails.Detail);
    }

    [Fact]
    public async Task UpdatePrompt_Updates_Prompt_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var promptId = Guid.NewGuid();
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Original",
            PromptText = "Original text",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);
        var request = new PromptRequest { Name = "Updated", PromptText = "Updated text" };

        // Act
        var result = await controller.UpdatePrompt(promptId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        // Verify database was updated
        var updatedPrompt = await context.ManagedPrompts.FindAsync(promptId);
        Assert.NotNull(updatedPrompt);
        Assert.Equal("Updated", updatedPrompt.Name);
        Assert.NotNull(updatedPrompt.UpdatedAt);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DeletePrompt_Returns_NotFound_When_Prompt_Not_Exists()
    {
        // Arrange
        var controller = CreateController();
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await controller.DeletePrompt(nonExistentId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains(nonExistentId.ToString(), problemDetails.Detail);
    }

    [Fact]
    public async Task DeletePrompt_Deletes_Prompt_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var promptId = Guid.NewGuid();
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Test Prompt",
            PromptText = "Test text",
            CreatedAt = DateTime.UtcNow
        };

        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.DeletePrompt(promptId);

        // Assert
        Assert.IsType<NoContentResult>(result);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);

        // Verify prompt was deleted from database
        var deletedPrompt = await context.ManagedPrompts.FindAsync(promptId);
        Assert.Null(deletedPrompt);
    }

    #endregion

    #region Groups Tests

    [Fact]
    public async Task GetGroups_Returns_Ok_With_Pagination()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var group1 = new ResourceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Group 1",
            Description = "Description 1",
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
        var group2 = new ResourceGroup
        {
            Id = Guid.NewGuid(),
            Name = "Group 2",
            Description = "Description 2",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        context.ResourceGroups.AddRange(group1, group2);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.GetGroups(page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
        mockCache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task CreateGroup_Returns_BadRequest_When_Name_Is_Empty()
    {
        // Arrange
        var controller = CreateController();
        var request = new GroupRequest { Name = "", Description = "Test" };

        // Act
        var result = await controller.CreateGroup(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Name is required", problemDetails.Detail);
    }

    [Fact]
    public async Task CreateGroup_Creates_Group_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var controller = CreateController(context, cacheService: mockCache);
        var request = new GroupRequest { Name = "New Group", Description = "Test description" };

        // Act
        var result = await controller.CreateGroup(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(createdResult.Value);

        // Verify group was saved to database
        var savedGroup = await context.ResourceGroups.FirstOrDefaultAsync();
        Assert.NotNull(savedGroup);
        Assert.Equal("New Group", savedGroup.Name);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateGroup_Returns_NotFound_When_Group_Not_Exists()
    {
        // Arrange
        var controller = CreateController();
        var nonExistentId = Guid.NewGuid();
        var request = new GroupRequest { Name = "Updated", Description = "Updated description" };

        // Act
        var result = await controller.UpdateGroup(nonExistentId, request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains(nonExistentId.ToString(), problemDetails.Detail);
    }

    [Fact]
    public async Task UpdateGroup_Updates_Group_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var groupId = Guid.NewGuid();
        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Original",
            Description = "Original description",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        context.ResourceGroups.Add(group);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);
        var request = new GroupRequest { Name = "Updated", Description = "Updated description" };

        // Act
        var result = await controller.UpdateGroup(groupId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        // Verify database was updated
        var updatedGroup = await context.ResourceGroups.FindAsync(groupId);
        Assert.NotNull(updatedGroup);
        Assert.Equal("Updated", updatedGroup.Name);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DeleteGroup_Returns_NotFound_When_Group_Not_Exists()
    {
        // Arrange
        var controller = CreateController();
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await controller.DeleteGroup(nonExistentId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains(nonExistentId.ToString(), problemDetails.Detail);
    }

    [Fact]
    public async Task DeleteGroup_Deletes_Group_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var groupId = Guid.NewGuid();
        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            Description = "Test description",
            CreatedAt = DateTime.UtcNow
        };

        context.ResourceGroups.Add(group);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.DeleteGroup(groupId);

        // Assert
        Assert.IsType<NoContentResult>(result);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);

        // Verify group was deleted from database
        var deletedGroup = await context.ResourceGroups.FindAsync(groupId);
        Assert.Null(deletedGroup);
    }

    #endregion

    #region File-Group Association Tests

    [Fact]
    public async Task AddFileToGroup_Returns_NotFound_When_Group_Not_Exists()
    {
        // Arrange
        var context = GetInMemoryContext();
        var fileId = Guid.NewGuid();
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };
        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var nonExistentGroupId = Guid.NewGuid();

        // Act
        var result = await controller.AddFileToGroup(nonExistentGroupId, fileId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains("Group", problemDetails.Detail);
    }

    [Fact]
    public async Task AddFileToGroup_Returns_NotFound_When_File_Not_Exists()
    {
        // Arrange
        var context = GetInMemoryContext();
        var groupId = Guid.NewGuid();
        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        context.ResourceGroups.Add(group);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var nonExistentFileId = Guid.NewGuid();

        // Act
        var result = await controller.AddFileToGroup(groupId, nonExistentFileId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains("File", problemDetails.Detail);
    }

    [Fact]
    public async Task AddFileToGroup_Returns_Ok_When_Already_Associated()
    {
        // Arrange
        var context = GetInMemoryContext();
        var groupId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ResourceGroups.Add(group);
        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        // Add association
        var association = new ManagedFileGroup
        {
            ManagedFileId = fileId,
            ResourceGroupId = groupId
        };
        context.Set<ManagedFileGroup>().Add(association);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.AddFileToGroup(groupId, fileId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task AddFileToGroup_Adds_Association_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var groupId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ResourceGroups.Add(group);
        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.AddFileToGroup(groupId, fileId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);

        // Verify association was created
        var association = await context.Set<ManagedFileGroup>()
            .FirstOrDefaultAsync(fg => fg.ManagedFileId == fileId && fg.ResourceGroupId == groupId);
        Assert.NotNull(association);
    }

    [Fact]
    public async Task RemoveFileFromGroup_Returns_NotFound_When_Not_Associated()
    {
        // Arrange
        var context = GetInMemoryContext();
        var groupId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ResourceGroups.Add(group);
        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.RemoveFileFromGroup(groupId, fileId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains("not in the specified group", problemDetails.Detail);
    }

    [Fact]
    public async Task RemoveFileFromGroup_Removes_Association_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var groupId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ResourceGroups.Add(group);
        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        // Add association
        var association = new ManagedFileGroup
        {
            ManagedFileId = fileId,
            ResourceGroupId = groupId
        };
        context.Set<ManagedFileGroup>().Add(association);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.RemoveFileFromGroup(groupId, fileId);

        // Assert
        Assert.IsType<NoContentResult>(result);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);

        // Verify association was removed
        var removedAssociation = await context.Set<ManagedFileGroup>()
            .FirstOrDefaultAsync(fg => fg.ManagedFileId == fileId && fg.ResourceGroupId == groupId);
        Assert.Null(removedAssociation);
    }

    #endregion

    #region Prompt-Group Association Tests

    [Fact]
    public async Task AddPromptToGroup_Returns_NotFound_When_Group_Not_Exists()
    {
        // Arrange
        var context = GetInMemoryContext();
        var promptId = Guid.NewGuid();
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Test Prompt",
            PromptText = "Test text",
            CreatedAt = DateTime.UtcNow
        };
        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var nonExistentGroupId = Guid.NewGuid();

        // Act
        var result = await controller.AddPromptToGroup(nonExistentGroupId, promptId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains("Group", problemDetails.Detail);
    }

    [Fact]
    public async Task AddPromptToGroup_Returns_NotFound_When_Prompt_Not_Exists()
    {
        // Arrange
        var context = GetInMemoryContext();
        var groupId = Guid.NewGuid();
        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        context.ResourceGroups.Add(group);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var nonExistentPromptId = Guid.NewGuid();

        // Act
        var result = await controller.AddPromptToGroup(groupId, nonExistentPromptId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains("Prompt", problemDetails.Detail);
    }

    [Fact]
    public async Task AddPromptToGroup_Returns_Ok_When_Already_Associated()
    {
        // Arrange
        var context = GetInMemoryContext();
        var groupId = Guid.NewGuid();
        var promptId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Test Prompt",
            PromptText = "Test text",
            CreatedAt = DateTime.UtcNow
        };

        context.ResourceGroups.Add(group);
        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        // Add association
        var association = new ManagedPromptGroup
        {
            ManagedPromptId = promptId,
            ResourceGroupId = groupId
        };
        context.Set<ManagedPromptGroup>().Add(association);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.AddPromptToGroup(groupId, promptId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task AddPromptToGroup_Adds_Association_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var groupId = Guid.NewGuid();
        var promptId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Test Prompt",
            PromptText = "Test text",
            CreatedAt = DateTime.UtcNow
        };

        context.ResourceGroups.Add(group);
        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.AddPromptToGroup(groupId, promptId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);

        // Verify association was created
        var association = await context.Set<ManagedPromptGroup>()
            .FirstOrDefaultAsync(pg => pg.ManagedPromptId == promptId && pg.ResourceGroupId == groupId);
        Assert.NotNull(association);
    }

    [Fact]
    public async Task RemovePromptFromGroup_Returns_NotFound_When_Not_Associated()
    {
        // Arrange
        var context = GetInMemoryContext();
        var groupId = Guid.NewGuid();
        var promptId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Test Prompt",
            PromptText = "Test text",
            CreatedAt = DateTime.UtcNow
        };

        context.ResourceGroups.Add(group);
        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        // Act
        var result = await controller.RemovePromptFromGroup(groupId, promptId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Contains("not in the specified group", problemDetails.Detail);
    }

    [Fact]
    public async Task RemovePromptFromGroup_Removes_Association_Successfully()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var groupId = Guid.NewGuid();
        var promptId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Test Prompt",
            PromptText = "Test text",
            CreatedAt = DateTime.UtcNow
        };

        context.ResourceGroups.Add(group);
        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        // Add association
        var association = new ManagedPromptGroup
        {
            ManagedPromptId = promptId,
            ResourceGroupId = groupId
        };
        context.Set<ManagedPromptGroup>().Add(association);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.RemovePromptFromGroup(groupId, promptId);

        // Assert
        Assert.IsType<NoContentResult>(result);
        mockCache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.AtLeastOnce);

        // Verify association was removed
        var removedAssociation = await context.Set<ManagedPromptGroup>()
            .FirstOrDefaultAsync(pg => pg.ManagedPromptId == promptId && pg.ResourceGroupId == groupId);
        Assert.Null(removedAssociation);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public async Task GetFiles_Handles_Large_Page_Size()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var controller = CreateController(context, cacheService: mockCache);

        // Act - Request page size > MaxPageSize (100)
        var result = await controller.GetFiles(page: 1, pageSize: 200);

        // Assert - Should normalize to DefaultPageSize (20)
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetPrompts_Validates_Pagination_Parameters()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var controller = CreateController(context, cacheService: mockCache);

        // Act - Test invalid parameters
        var result = await controller.GetPrompts(page: 0, pageSize: -1);

        // Assert - Should normalize parameters
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetGroups_Validates_Pagination_Parameters()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var controller = CreateController(context, cacheService: mockCache);

        // Act - Test invalid parameters
        var result = await controller.GetGroups(page: -5, pageSize: 0);

        // Assert - Should normalize parameters
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task UploadFile_Returns_Error_When_Upload_Fails()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockMediaStorage = new Mock<IMediaStorageService>();
        var mockCache = new Mock<ICacheService>();

        mockMediaStorage.Setup(m => m.ComputeContentHash(It.IsAny<Stream>())).Returns("hash");
        mockMediaStorage.Setup(m => m.UploadMediaAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Upload failed"));

        var controller = CreateController(context, mockMediaStorage, mockCache);
        var file = CreateMockFormFile("test.pdf");
        var request = new FileUploadRequest { File = file, Label = "Test" };

        // Act
        var result = await controller.UploadFile(request);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task UpdateGroup_Returns_BadRequest_When_Name_Is_Empty()
    {
        // Arrange
        var context = GetInMemoryContext();
        var groupId = Guid.NewGuid();
        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Original",
            CreatedAt = DateTime.UtcNow
        };
        context.ResourceGroups.Add(group);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var request = new GroupRequest { Name = "", Description = "Updated" };

        // Act
        var result = await controller.UpdateGroup(groupId, request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Name is required", problemDetails.Detail);
    }

    [Fact]
    public async Task UpdatePrompt_Returns_BadRequest_When_Name_Is_Empty()
    {
        // Arrange
        var context = GetInMemoryContext();
        var promptId = Guid.NewGuid();
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Original",
            PromptText = "Original text",
            CreatedAt = DateTime.UtcNow
        };
        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var request = new PromptRequest { Name = "", PromptText = "Updated" };

        // Act
        var result = await controller.UpdatePrompt(promptId, request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Name is required", problemDetails.Detail);
    }

    [Fact]
    public async Task UpdatePrompt_Returns_BadRequest_When_PromptText_Is_Empty()
    {
        // Arrange
        var context = GetInMemoryContext();
        var promptId = Guid.NewGuid();
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Original",
            PromptText = "Original text",
            CreatedAt = DateTime.UtcNow
        };
        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var request = new PromptRequest { Name = "Updated", PromptText = "" };

        // Act
        var result = await controller.UpdatePrompt(promptId, request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Prompt text is required", problemDetails.Detail);
    }

    [Fact]
    public async Task GetFiles_With_Groups_Returns_Correct_Data()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var groupId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ResourceGroups.Add(group);
        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        // Add association
        var association = new ManagedFileGroup
        {
            ManagedFileId = fileId,
            ResourceGroupId = groupId
        };
        context.Set<ManagedFileGroup>().Add(association);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.GetFiles(page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetPrompts_With_Groups_Returns_Correct_Data()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetAsync<object>(It.IsAny<string>())).ReturnsAsync((object?)null);

        var groupId = Guid.NewGuid();
        var promptId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var prompt = new ManagedPrompt
        {
            Id = promptId,
            Name = "Test Prompt",
            PromptText = "Test text",
            CreatedAt = DateTime.UtcNow
        };

        context.ResourceGroups.Add(group);
        context.ManagedPrompts.Add(prompt);
        await context.SaveChangesAsync();

        // Add association
        var association = new ManagedPromptGroup
        {
            ManagedPromptId = promptId,
            ResourceGroupId = groupId
        };
        context.Set<ManagedPromptGroup>().Add(association);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.GetPrompts(page: 1, pageSize: 10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task UploadFile_With_Empty_File_Returns_BadRequest()
    {
        // Arrange
        var controller = CreateController();
        var emptyFile = CreateMockFormFile("test.pdf", "");
        var request = new FileUploadRequest { File = emptyFile, Label = "Test" };

        // Act
        var result = await controller.UploadFile(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("File is required", problemDetails.Detail);
    }

    [Fact]
    public async Task CreateGroup_With_Null_Description_Succeeds()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var controller = CreateController(context, cacheService: mockCache);
        var request = new GroupRequest { Name = "Test Group", Description = null };

        // Act
        var result = await controller.CreateGroup(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(createdResult.Value);

        // Verify in database
        var savedGroup = await context.ResourceGroups.FirstOrDefaultAsync();
        Assert.NotNull(savedGroup);
        Assert.Equal("Test Group", savedGroup.Name);
        Assert.Null(savedGroup.Description);
    }

    [Fact]
    public async Task DeleteGroup_With_Associated_Files_Succeeds()
    {
        // Arrange
        var context = GetInMemoryContext();
        var mockCache = new Mock<ICacheService>();

        var groupId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var group = new ResourceGroup
        {
            Id = groupId,
            Name = "Test Group",
            CreatedAt = DateTime.UtcNow
        };
        var file = new ManagedFile
        {
            Id = fileId,
            FileName = "test.pdf",
            Label = "Test",
            ContentType = "application/pdf",
            StorageKey = "key",
            UploadedAt = DateTime.UtcNow,
            FileSize = 1000
        };

        context.ResourceGroups.Add(group);
        context.ManagedFiles.Add(file);
        await context.SaveChangesAsync();

        // Add association
        var association = new ManagedFileGroup
        {
            ManagedFileId = fileId,
            ResourceGroupId = groupId
        };
        context.Set<ManagedFileGroup>().Add(association);
        await context.SaveChangesAsync();

        var controller = CreateController(context, cacheService: mockCache);

        // Act
        var result = await controller.DeleteGroup(groupId);

        // Assert
        Assert.IsType<NoContentResult>(result);

        // Verify group was deleted
        var deletedGroup = await context.ResourceGroups.FindAsync(groupId);
        Assert.Null(deletedGroup);

        // Verify file still exists (only association should be removed)
        var existingFile = await context.ManagedFiles.FindAsync(fileId);
        Assert.NotNull(existingFile);
    }

    #endregion
}
