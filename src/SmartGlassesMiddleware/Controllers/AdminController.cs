using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Controllers;

/// <summary>
/// Handles administrative operations for managing files, prompts, and resource groups
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IMediaStorageService _mediaStorageService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ApplicationDbContext context,
        IMediaStorageService mediaStorageService,
        ILogger<AdminController> logger)
    {
        _context = context;
        _mediaStorageService = mediaStorageService;
        _logger = logger;
    }

    #region Files

    /// <summary>
    /// Get all managed files
    /// </summary>
    [HttpGet("files")]
    public async Task<ActionResult<IEnumerable<object>>> GetFiles()
    {
        var files = await _context.ManagedFiles
            .Include(f => f.FileGroups)
            .ThenInclude(fg => fg.ResourceGroup)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.Label,
                f.ContentType,
                f.StorageKey,
                f.UploadedAt,
                f.FileSize,
                Groups = f.FileGroups.Select(fg => new { fg.ResourceGroup.Id, fg.ResourceGroup.Name })
            })
            .ToListAsync();

        return Ok(files);
    }

    /// <summary>
    /// Upload a new file with label
    /// </summary>
    [HttpPost("files")]
    public async Task<ActionResult> UploadFile([FromForm] FileUploadRequest request)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "File is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Label is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            // Upload to MinIO
            using var stream = request.File.OpenReadStream();
            var contentType = request.File.ContentType ?? "application/octet-stream";
            var storageKey = await _mediaStorageService.UploadMediaAsync(stream, request.File.FileName, contentType);

            // Save to database
            var managedFile = new ManagedFile
            {
                Id = Guid.NewGuid(),
                FileName = request.File.FileName,
                Label = request.Label,
                ContentType = contentType,
                StorageKey = storageKey,
                UploadedAt = DateTime.UtcNow,
                FileSize = request.File.Length
            };

            _context.ManagedFiles.Add(managedFile);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetFiles), new { id = managedFile.Id }, new
            {
                managedFile.Id,
                managedFile.FileName,
                managedFile.Label,
                managedFile.ContentType,
                managedFile.StorageKey,
                managedFile.UploadedAt,
                managedFile.FileSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while uploading the file",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Delete a managed file
    /// </summary>
    [HttpDelete("files/{id}")]
    public async Task<ActionResult> DeleteFile(Guid id)
    {
        var file = await _context.ManagedFiles.FindAsync(id);
        if (file == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"File with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            // Delete from MinIO
            await _mediaStorageService.DeleteMediaAsync(file.StorageKey);

            // Delete from database
            _context.ManagedFiles.Remove(file);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileId}", id);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while deleting the file",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    #endregion

    #region Prompts

    /// <summary>
    /// Get all managed prompts
    /// </summary>
    [HttpGet("prompts")]
    public async Task<ActionResult<IEnumerable<object>>> GetPrompts()
    {
        var prompts = await _context.ManagedPrompts
            .Include(p => p.PromptGroups)
            .ThenInclude(pg => pg.ResourceGroup)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.PromptText,
                p.CreatedAt,
                p.UpdatedAt,
                Groups = p.PromptGroups.Select(pg => new { pg.ResourceGroup.Id, pg.ResourceGroup.Name })
            })
            .ToListAsync();

        return Ok(prompts);
    }

    /// <summary>
    /// Create a new prompt
    /// </summary>
    [HttpPost("prompts")]
    public async Task<ActionResult> CreatePrompt([FromBody] PromptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Name is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(request.PromptText))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Prompt text is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var prompt = new ManagedPrompt
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                PromptText = request.PromptText,
                CreatedAt = DateTime.UtcNow
            };

            _context.ManagedPrompts.Add(prompt);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPrompts), new { id = prompt.Id }, new
            {
                prompt.Id,
                prompt.Name,
                prompt.PromptText,
                prompt.CreatedAt,
                prompt.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating prompt");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while creating the prompt",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Update an existing prompt
    /// </summary>
    [HttpPut("prompts/{id}")]
    public async Task<ActionResult> UpdatePrompt(Guid id, [FromBody] PromptRequest request)
    {
        var prompt = await _context.ManagedPrompts.FindAsync(id);
        if (prompt == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Prompt with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Name is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(request.PromptText))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Prompt text is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            prompt.Name = request.Name;
            prompt.PromptText = request.PromptText;
            prompt.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                prompt.Id,
                prompt.Name,
                prompt.PromptText,
                prompt.CreatedAt,
                prompt.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating prompt {PromptId}", id);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while updating the prompt",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Delete a prompt
    /// </summary>
    [HttpDelete("prompts/{id}")]
    public async Task<ActionResult> DeletePrompt(Guid id)
    {
        var prompt = await _context.ManagedPrompts.FindAsync(id);
        if (prompt == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Prompt with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            _context.ManagedPrompts.Remove(prompt);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting prompt {PromptId}", id);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while deleting the prompt",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    #endregion

    #region Resource Groups

    /// <summary>
    /// Get all resource groups
    /// </summary>
    [HttpGet("groups")]
    public async Task<ActionResult<IEnumerable<object>>> GetGroups()
    {
        var groups = await _context.ResourceGroups
            .Include(g => g.FileGroups)
            .ThenInclude(fg => fg.ManagedFile)
            .Include(g => g.PromptGroups)
            .ThenInclude(pg => pg.ManagedPrompt)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Description,
                g.CreatedAt,
                Files = g.FileGroups.Select(fg => new
                {
                    fg.ManagedFile.Id,
                    fg.ManagedFile.FileName,
                    fg.ManagedFile.Label
                }),
                Prompts = g.PromptGroups.Select(pg => new
                {
                    pg.ManagedPrompt.Id,
                    pg.ManagedPrompt.Name
                })
            })
            .ToListAsync();

        return Ok(groups);
    }

    /// <summary>
    /// Create a new resource group
    /// </summary>
    [HttpPost("groups")]
    public async Task<ActionResult> CreateGroup([FromBody] GroupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Name is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var group = new ResourceGroup
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                CreatedAt = DateTime.UtcNow
            };

            _context.ResourceGroups.Add(group);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetGroups), new { id = group.Id }, new
            {
                group.Id,
                group.Name,
                group.Description,
                group.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while creating the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Update a resource group
    /// </summary>
    [HttpPut("groups/{id}")]
    public async Task<ActionResult> UpdateGroup(Guid id, [FromBody] GroupRequest request)
    {
        var group = await _context.ResourceGroups.FindAsync(id);
        if (group == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Group with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Name is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            group.Name = request.Name;
            group.Description = request.Description;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                group.Id,
                group.Name,
                group.Description,
                group.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating group {GroupId}", id);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while updating the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Delete a resource group
    /// </summary>
    [HttpDelete("groups/{id}")]
    public async Task<ActionResult> DeleteGroup(Guid id)
    {
        var group = await _context.ResourceGroups.FindAsync(id);
        if (group == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Group with ID {id} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            _context.ResourceGroups.Remove(group);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting group {GroupId}", id);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while deleting the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Add a file to a group
    /// </summary>
    [HttpPost("groups/{groupId}/files/{fileId}")]
    public async Task<ActionResult> AddFileToGroup(Guid groupId, Guid fileId)
    {
        var group = await _context.ResourceGroups.FindAsync(groupId);
        if (group == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Group with ID {groupId} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        var file = await _context.ManagedFiles.FindAsync(fileId);
        if (file == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"File with ID {fileId} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            var existing = await _context.Set<ManagedFileGroup>()
                .FirstOrDefaultAsync(fg => fg.ManagedFileId == fileId && fg.ResourceGroupId == groupId);

            if (existing != null)
            {
                return Ok(new { message = "File is already in the group" });
            }

            var fileGroup = new ManagedFileGroup
            {
                ManagedFileId = fileId,
                ResourceGroupId = groupId
            };

            _context.Set<ManagedFileGroup>().Add(fileGroup);
            await _context.SaveChangesAsync();

            return Ok(new { message = "File added to group successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding file {FileId} to group {GroupId}", fileId, groupId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while adding the file to the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Remove a file from a group
    /// </summary>
    [HttpDelete("groups/{groupId}/files/{fileId}")]
    public async Task<ActionResult> RemoveFileFromGroup(Guid groupId, Guid fileId)
    {
        var fileGroup = await _context.Set<ManagedFileGroup>()
            .FirstOrDefaultAsync(fg => fg.ManagedFileId == fileId && fg.ResourceGroupId == groupId);

        if (fileGroup == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = "File is not in the specified group",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            _context.Set<ManagedFileGroup>().Remove(fileGroup);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing file {FileId} from group {GroupId}", fileId, groupId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while removing the file from the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Add a prompt to a group
    /// </summary>
    [HttpPost("groups/{groupId}/prompts/{promptId}")]
    public async Task<ActionResult> AddPromptToGroup(Guid groupId, Guid promptId)
    {
        var group = await _context.ResourceGroups.FindAsync(groupId);
        if (group == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Group with ID {groupId} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        var prompt = await _context.ManagedPrompts.FindAsync(promptId);
        if (prompt == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Prompt with ID {promptId} not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            var existing = await _context.Set<ManagedPromptGroup>()
                .FirstOrDefaultAsync(pg => pg.ManagedPromptId == promptId && pg.ResourceGroupId == groupId);

            if (existing != null)
            {
                return Ok(new { message = "Prompt is already in the group" });
            }

            var promptGroup = new ManagedPromptGroup
            {
                ManagedPromptId = promptId,
                ResourceGroupId = groupId
            };

            _context.Set<ManagedPromptGroup>().Add(promptGroup);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Prompt added to group successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding prompt {PromptId} to group {GroupId}", promptId, groupId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while adding the prompt to the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Remove a prompt from a group
    /// </summary>
    [HttpDelete("groups/{groupId}/prompts/{promptId}")]
    public async Task<ActionResult> RemovePromptFromGroup(Guid groupId, Guid promptId)
    {
        var promptGroup = await _context.Set<ManagedPromptGroup>()
            .FirstOrDefaultAsync(pg => pg.ManagedPromptId == promptId && pg.ResourceGroupId == groupId);

        if (promptGroup == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = "Prompt is not in the specified group",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            _context.Set<ManagedPromptGroup>().Remove(promptGroup);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing prompt {PromptId} from group {GroupId}", promptId, groupId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An error occurred while removing the prompt from the group",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    #endregion
}
