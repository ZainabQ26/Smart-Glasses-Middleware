using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Models;
using Pgvector.EntityFrameworkCore;
using System.Linq;

namespace SmartGlassesMiddleware.Data;

public class ApplicationDbContext : DbContext
{
    private bool _isInMemoryProvider;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Cache provider details here so OnModelCreating doesn't query Database services.
        _isInMemoryProvider = optionsBuilder.Options.Extensions
            .Any(e => e.GetType().Name.Contains("InMemory"));

        // Suppress pending model changes warning to allow migrations to run
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    public DbSet<RequestHistory> RequestHistories { get; set; }
    public DbSet<RequestStatus> RequestStatuses { get; set; }
    public DbSet<ManagedFile> ManagedFiles { get; set; }
    public DbSet<ManagedPrompt> ManagedPrompts { get; set; }
    public DbSet<ResourceGroup> ResourceGroups { get; set; }
    public DbSet<DocumentMetadata> DocumentMetadatas { get; set; }
    public DbSet<DocumentChunk> DocumentChunks { get; set; }
    public DbSet<DocumentGroup> DocumentGroups { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<TranscriptionJob> TranscriptionJobs { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<ChatMessageAttachment> ChatMessageAttachments { get; set; }
    public DbSet<AIInteractionLog> AIInteractionLogs { get; set; }
    public DbSet<GroupConversationMapping> GroupConversationMappings { get; set; }
    public DbSet<Abbreviation> Abbreviations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension only for relational providers.
        if (!_isInMemoryProvider)
        {
            modelBuilder.HasPostgresExtension("vector");
        }

        modelBuilder.Entity<RequestHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.RequestType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.InputText).IsRequired();
        });

        modelBuilder.Entity<RequestStatus>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequestId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.RequestType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.StartTime).IsRequired();
            
            // Add index on StartTime for efficient querying of recent requests
            entity.HasIndex(e => e.StartTime);
        });

        modelBuilder.Entity<ManagedFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StorageKey).IsRequired();
            entity.Property(e => e.UploadedAt).IsRequired();
            entity.Property(e => e.FileSize).IsRequired();

            // Add index for ContentHash for deduplication lookups
            entity.HasIndex(e => e.ContentHash);
            // Speeds up latest-files list queries
            entity.HasIndex(e => e.UploadedAt);
        });

        modelBuilder.Entity<ManagedPrompt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.PromptText).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<ResourceGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.CreatedAt).IsRequired();
            // Speeds up latest-groups list queries
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ManagedFileGroup>(entity =>
        {
            entity.HasKey(e => new { e.ManagedFileId, e.ResourceGroupId });
            entity.HasOne(e => e.ManagedFile)
                .WithMany(f => f.FileGroups)
                .HasForeignKey(e => e.ManagedFileId);
            entity.HasOne(e => e.ResourceGroup)
                .WithMany(g => g.FileGroups)
                .HasForeignKey(e => e.ResourceGroupId);
            // Composite PK starts with ManagedFileId, so add lookup index for ResourceGroupId-based filters.
            entity.HasIndex(e => e.ResourceGroupId);
        });

        modelBuilder.Entity<ManagedPromptGroup>(entity =>
        {
            entity.HasKey(e => new { e.ManagedPromptId, e.ResourceGroupId });
            entity.HasOne(e => e.ManagedPrompt)
                .WithMany(p => p.PromptGroups)
                .HasForeignKey(e => e.ManagedPromptId);
            entity.HasOne(e => e.ResourceGroup)
                .WithMany(g => g.PromptGroups)
                .HasForeignKey(e => e.ResourceGroupId);
            // Composite PK starts with ManagedPromptId, so add lookup index for ResourceGroupId-based filters.
            entity.HasIndex(e => e.ResourceGroupId);
        });

        modelBuilder.Entity<DocumentMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StorageKey).IsRequired();
            entity.Property(e => e.UploadedAt).IsRequired();
            entity.Property(e => e.FileSize).IsRequired();
            entity.Property(e => e.DocumentType).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ChunkText).IsRequired();
            entity.Property(e => e.PageNumber).IsRequired();
            entity.Property(e => e.ChunkIndex).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Only configure vector column for relational providers (not for in-memory testing).
            if (!_isInMemoryProvider)
            {
                // Configure vector column for pgvector similarity search
                entity.Property(e => e.EmbeddingVector)
                    .HasColumnType("vector(1024)"); // 1024-dimensional embeddings (Amazon Titan Embed V2 default)

                // Create index on vector column for efficient similarity search
                entity.HasIndex(e => e.EmbeddingVector)
                    .HasMethod("ivfflat")
                    .HasOperators("vector_cosine_ops");
            }
            else
            {
                // For in-memory database, ignore the EmbeddingVector property
                entity.Ignore(e => e.EmbeddingVector);
            }

            entity.HasOne(e => e.DocumentMetadata)
                .WithMany(d => d.Chunks)
                .HasForeignKey(e => e.DocumentMetadataId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentGroup>(entity =>
        {
            entity.HasKey(e => new { e.DocumentMetadataId, e.ResourceGroupId });
            entity.HasOne(e => e.DocumentMetadata)
                .WithMany(d => d.DocumentGroups)
                .HasForeignKey(e => e.DocumentMetadataId);
            entity.HasOne(e => e.ResourceGroup)
                .WithMany(g => g.DocumentGroups)
                .HasForeignKey(e => e.ResourceGroupId);
            // Composite PK starts with DocumentMetadataId, so add lookup index for ResourceGroupId-based filters.
            entity.HasIndex(e => e.ResourceGroupId);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();

            entity.HasIndex(e => e.ResourceGroupId);

            entity.HasOne(e => e.ResourceGroup)
                .WithMany(g => g.Conversations)
                .HasForeignKey(e => e.ResourceGroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Timestamp).IsRequired();

            entity.HasIndex(e => e.ConversationId);

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessageAttachment>(entity =>
        {
            entity.HasKey(e => new { e.ChatMessageId, e.DocumentMetadataId });
            entity.HasOne(e => e.ChatMessage)
                .WithMany(m => m.Attachments)
                .HasForeignKey(e => e.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Document)
                .WithMany(d => d.MessageAttachments)
                .HasForeignKey(e => e.DocumentMetadataId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AIInteractionLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Input).IsRequired();
            entity.Property(e => e.Output).IsRequired();
            entity.Property(e => e.ModelUsed).IsRequired().HasMaxLength(100);
            entity.Property(e => e.DelegationReason).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.Success).IsRequired();

            entity.HasOne(e => e.ResourceGroup)
                .WithMany()
                .HasForeignKey(e => e.ResourceGroupId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Index for querying logs by timestamp
            entity.HasIndex(e => e.Timestamp);
            // Index for querying by model
            entity.HasIndex(e => e.ModelUsed);
        });

        modelBuilder.Entity<TranscriptionJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.JobId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.S3Bucket).IsRequired().HasMaxLength(255);
            entity.Property(e => e.S3Key).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.JobId).IsUnique();
        });

        modelBuilder.Entity<Abbreviation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ShortForm).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ExpandedForm).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(50);
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Indexes for efficient lookups
            entity.HasIndex(e => e.ShortForm);
            entity.HasIndex(e => e.ExpandedForm);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.Source);
        });
    }
}

