using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartGlassesMiddleware.Services;

public class DatabaseState
{
    public List<Models.RequestHistory> RequestHistories { get; set; } = new();
    public List<Models.RequestStatus> RequestStatuses { get; set; } = new();
    public List<Models.ManagedFile> ManagedFiles { get; set; } = new();
    public List<Models.ManagedPrompt> ManagedPrompts { get; set; } = new();
    public List<Models.ResourceGroup> ResourceGroups { get; set; } = new();
    public List<Models.DocumentMetadata> DocumentMetadatas { get; set; } = new();
    public List<Models.DocumentChunk> DocumentChunks { get; set; } = new();
    public List<Models.DocumentGroup> DocumentGroups { get; set; } = new();
    public List<Models.Conversation> Conversations { get; set; } = new();
    public List<Models.TranscriptionJob> TranscriptionJobs { get; set; } = new();
    public List<Models.ChatMessage> ChatMessages { get; set; } = new();
    public List<Models.ChatMessageAttachment> ChatMessageAttachments { get; set; } = new();
    public List<Models.AIInteractionLog> AIInteractionLogs { get; set; } = new();
    public List<Models.GroupConversationMapping> GroupConversationMappings { get; set; } = new();
    public List<Models.Abbreviation> Abbreviations { get; set; } = new();
    public List<Models.ManagedFileGroup> ManagedFileGroups { get; set; } = new();
    public List<Models.ManagedPromptGroup> ManagedPromptGroups { get; set; } = new();
}

public class StatePersistenceService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StatePersistenceService> _logger;
    private const string StateFileKey = "database-state.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public StatePersistenceService(IServiceScopeFactory scopeFactory, ILogger<StatePersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to load database state from {Key}", StateFileKey);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Stream? stream = null;
            try
            {
                stream = await storageService.DownloadMediaAsync(StateFileKey);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("No existing state file found or error downloading ({Message}). Starting fresh.", ex.Message);
            }

            if (stream != null)
            {
                using (stream)
                {
                    var state = await JsonSerializer.DeserializeAsync<DatabaseState>(stream, _jsonOptions, cancellationToken);
                    if (state != null)
                    {
                        if (state.RequestHistories?.Any() == true) context.RequestHistories.AddRange(state.RequestHistories);
                        if (state.RequestStatuses?.Any() == true) context.RequestStatuses.AddRange(state.RequestStatuses);
                        if (state.ManagedFiles?.Any() == true) context.ManagedFiles.AddRange(state.ManagedFiles);
                        if (state.ManagedPrompts?.Any() == true) context.ManagedPrompts.AddRange(state.ManagedPrompts);
                        if (state.ResourceGroups?.Any() == true) context.ResourceGroups.AddRange(state.ResourceGroups);
                        if (state.DocumentMetadatas?.Any() == true) context.DocumentMetadatas.AddRange(state.DocumentMetadatas);
                        if (state.DocumentChunks?.Any() == true) context.DocumentChunks.AddRange(state.DocumentChunks);
                        if (state.DocumentGroups?.Any() == true) context.DocumentGroups.AddRange(state.DocumentGroups);
                        if (state.Conversations?.Any() == true) context.Conversations.AddRange(state.Conversations);
                        if (state.TranscriptionJobs?.Any() == true) context.TranscriptionJobs.AddRange(state.TranscriptionJobs);
                        if (state.ChatMessages?.Any() == true) context.ChatMessages.AddRange(state.ChatMessages);
                        if (state.ChatMessageAttachments?.Any() == true) context.ChatMessageAttachments.AddRange(state.ChatMessageAttachments);
                        if (state.AIInteractionLogs?.Any() == true) context.AIInteractionLogs.AddRange(state.AIInteractionLogs);
                        if (state.GroupConversationMappings?.Any() == true) context.GroupConversationMappings.AddRange(state.GroupConversationMappings);
                        if (state.Abbreviations?.Any() == true) context.Abbreviations.AddRange(state.Abbreviations);

                        // Note: ManagedFileGroups / ManagedPromptGroups don't have direct DbSets in DbContext,
                        // but are accessible through navigation properties or via Set<T>()
                        if (state.ManagedFileGroups?.Any() == true) context.Set<Models.ManagedFileGroup>().AddRange(state.ManagedFileGroups);
                        if (state.ManagedPromptGroups?.Any() == true) context.Set<Models.ManagedPromptGroup>().AddRange(state.ManagedPromptGroups);

                        await context.SaveChangesAsync(cancellationToken);
                        _logger.LogInformation("Successfully loaded database state from {Key}", StateFileKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load database state from {Key}", StateFileKey);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving database state to {Key} before shutdown", StateFileKey);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var storageService = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var state = new DatabaseState
            {
                RequestHistories = await context.RequestHistories.AsNoTracking().ToListAsync(cancellationToken),
                RequestStatuses = await context.RequestStatuses.AsNoTracking().ToListAsync(cancellationToken),
                ManagedFiles = await context.ManagedFiles.AsNoTracking().ToListAsync(cancellationToken),
                ManagedPrompts = await context.ManagedPrompts.AsNoTracking().ToListAsync(cancellationToken),
                ResourceGroups = await context.ResourceGroups.AsNoTracking().ToListAsync(cancellationToken),
                DocumentMetadatas = await context.DocumentMetadatas.AsNoTracking().ToListAsync(cancellationToken),
                DocumentChunks = await context.DocumentChunks.AsNoTracking().ToListAsync(cancellationToken),
                DocumentGroups = await context.DocumentGroups.AsNoTracking().ToListAsync(cancellationToken),
                Conversations = await context.Conversations.AsNoTracking().ToListAsync(cancellationToken),
                TranscriptionJobs = await context.TranscriptionJobs.AsNoTracking().ToListAsync(cancellationToken),
                ChatMessages = await context.ChatMessages.AsNoTracking().ToListAsync(cancellationToken),
                ChatMessageAttachments = await context.ChatMessageAttachments.AsNoTracking().ToListAsync(cancellationToken),
                AIInteractionLogs = await context.AIInteractionLogs.AsNoTracking().ToListAsync(cancellationToken),
                GroupConversationMappings = await context.GroupConversationMappings.AsNoTracking().ToListAsync(cancellationToken),
                Abbreviations = await context.Abbreviations.AsNoTracking().ToListAsync(cancellationToken),
                ManagedFileGroups = await context.Set<Models.ManagedFileGroup>().AsNoTracking().ToListAsync(cancellationToken),
                ManagedPromptGroups = await context.Set<Models.ManagedPromptGroup>().AsNoTracking().ToListAsync(cancellationToken)
            };

            var memoryStream = new MemoryStream();
            await JsonSerializer.SerializeAsync(memoryStream, state, _jsonOptions, cancellationToken);
            memoryStream.Position = 0;

            await storageService.UploadMediaAsync(memoryStream, StateFileKey, "application/json");
            _logger.LogInformation("Successfully saved database state to {Key}", StateFileKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save database state to {Key}", StateFileKey);
        }
    }
}
