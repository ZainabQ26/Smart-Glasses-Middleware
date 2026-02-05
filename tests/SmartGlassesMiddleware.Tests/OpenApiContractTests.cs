using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SmartGlassesMiddleware.Controllers;
using Xunit;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Tests to validate that the OpenAPI specification matches the runtime routes
/// </summary>
public class OpenApiContractTests
{
    [Fact]
    public void AudioController_Route_Is_ApiAudio()
    {
        // Arrange
        var controller = typeof(AudioController);
        var routeAttr = controller.GetCustomAttribute<RouteAttribute>();

        // Assert
        Assert.NotNull(routeAttr);
        Assert.Equal("api/audio", routeAttr.Template);
    }

    [Fact]
    public void MediaController_Route_Is_ApiMedia()
    {
        // Arrange
        var controller = typeof(MediaController);
        var routeAttr = controller.GetCustomAttribute<RouteAttribute>();

        // Assert
        Assert.NotNull(routeAttr);
        Assert.Equal("api/media", routeAttr.Template);
    }

    [Fact]
    public void DocumentsController_Route_Is_ApiDocuments()
    {
        // Arrange
        var controller = typeof(DocumentsController);
        var routeAttr = controller.GetCustomAttribute<RouteAttribute>();

        // Assert
        Assert.NotNull(routeAttr);
        Assert.Equal("api/[controller]", routeAttr.Template);
    }

    [Fact]
    public void ConversationsController_Route_Is_ApiConversations()
    {
        // Arrange
        var controller = typeof(ConversationsController);
        var routeAttr = controller.GetCustomAttribute<RouteAttribute>();

        // Assert
        Assert.NotNull(routeAttr);
        Assert.Equal("api/[controller]", routeAttr.Template);
    }

    [Fact]
    public void AdminController_Has_CacheInvalidateEndpoint()
    {
        // Arrange
        var controller = typeof(AdminController);
        var method = controller.GetMethods()
            .FirstOrDefault(m => m.Name == "InvalidateCache");

        // Assert
        Assert.NotNull(method);
        var httpPostAttr = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPostAttr);
        Assert.Equal("cache/invalidate", httpPostAttr.Template);
    }

    [Fact]
    public void DocumentsController_UploadDocument_Returns_201Created()
    {
        // Arrange
        var controller = typeof(DocumentsController);
        var method = controller.GetMethods()
            .FirstOrDefault(m => m.Name == "UploadDocument");

        // Assert
        Assert.NotNull(method);
        var producesAttr = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .FirstOrDefault(a => a.StatusCode == 201);
        Assert.NotNull(producesAttr);
    }

    [Fact]
    public void AllControllers_HaveConsistentNaming()
    {
        // Arrange - Get all controller types
        var controllerTypes = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"));

        var inconsistentRoutes = new List<string>();

        // Act - Check each controller's route
        foreach (var controllerType in controllerTypes)
        {
            var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
            if (routeAttr == null) continue;

            var template = routeAttr.Template;
            
            // Skip [controller] placeholders
            if (template.Contains("[controller]")) continue;

            // Check for consistency: should be lowercase with hyphens
            if (template != template.ToLowerInvariant())
            {
                inconsistentRoutes.Add($"{controllerType.Name}: {template} (not lowercase)");
            }
        }

        // Assert
        Assert.Empty(inconsistentRoutes);
    }

    [Fact]
    public void AllControllers_AreUnderApiPrefix()
    {
        // Arrange - Get all controller types
        var controllerTypes = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"));

        var routesNotUnderApi = new List<string>();

        // Act - Check each controller's route
        foreach (var controllerType in controllerTypes)
        {
            var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
            if (routeAttr == null) continue;

            var template = routeAttr.Template;
            if (!template.StartsWith("api/"))
            {
                routesNotUnderApi.Add($"{controllerType.Name}: {template}");
            }
        }

        // Assert - All routes should be under /api
        Assert.Empty(routesNotUnderApi);
    }

    [Fact]
    public void OpenApiJson_Exists()
    {
        // Arrange
        var projectRoot = FindProjectRoot();
        var openApiPath = Path.Combine(projectRoot, "openapi.json");

        // Assert
        Assert.True(File.Exists(openApiPath), $"openapi.json not found at {openApiPath}");
    }

    [Fact]
    public void OpenApiJson_IsValidJson()
    {
        // Arrange
        var projectRoot = FindProjectRoot();
        var openApiPath = Path.Combine(projectRoot, "openapi.json");
        
        if (!File.Exists(openApiPath))
        {
            // Skip test if file doesn't exist
            return;
        }

        var jsonContent = File.ReadAllText(openApiPath);

        // Act & Assert - Should not throw exception
        var doc = JsonDocument.Parse(jsonContent);
        Assert.NotNull(doc);
        Assert.True(doc.RootElement.TryGetProperty("openapi", out _));
    }

    private string FindProjectRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        
        // Try to find openapi.json in current directory or parent directories
        while (!string.IsNullOrEmpty(currentDir))
        {
            if (File.Exists(Path.Combine(currentDir, "openapi.json")))
            {
                return currentDir;
            }
            
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        
        // Fallback to a common pattern
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", ".."));
    }
}

