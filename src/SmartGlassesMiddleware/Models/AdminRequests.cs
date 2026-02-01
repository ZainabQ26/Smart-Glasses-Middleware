using Microsoft.AspNetCore.Http;

namespace SmartGlassesMiddleware.Models;

/// <summary>
/// Request model for uploading a file
/// </summary>
public class FileUploadRequest
{
    public IFormFile? File { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Request model for creating or updating a prompt
/// </summary>
public class PromptRequest
{
    public string Name { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
}

/// <summary>
/// Request model for creating or updating a resource group
/// </summary>
public class GroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
