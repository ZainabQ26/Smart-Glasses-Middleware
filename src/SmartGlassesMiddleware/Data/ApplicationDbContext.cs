using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Models;
using Pgvector.EntityFrameworkCore;

namespace SmartGlassesMiddleware.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<RequestHistory> RequestHistories { get; set; }
    public DbSet<ManagedFile> ManagedFiles { get; set; }
    public DbSet<ManagedPrompt> ManagedPrompts { get; set; }
    public DbSet<ResourceGroup> ResourceGroups { get; set; }
    public DbSet<DocumentMetadata> DocumentMetadatas { get; set; }
    public DbSet<DocumentChunk> DocumentChunks { get; set; }
    public DbSet<DocumentGroup> DocumentGroups { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<AIInteractionLog> AIInteractionLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<RequestHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.RequestType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.InputText).IsRequired();
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
            
            // Configure vector column for pgvector similarity search
            entity.Property(e => e.EmbeddingVector)
                .HasColumnType("vector(384)"); // 384-dimensional embeddings
            
            entity.HasOne(e => e.DocumentMetadata)
                .WithMany(d => d.Chunks)
                .HasForeignKey(e => e.DocumentMetadataId)
                .OnDelete(DeleteBehavior.Cascade);
                
            // Create index on vector column for efficient similarity search
            entity.HasIndex(e => e.EmbeddingVector)
                .HasMethod("ivfflat")
                .HasOperators("vector_cosine_ops");
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
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();
            
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
            
            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
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
    }
}
