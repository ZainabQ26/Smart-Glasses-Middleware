using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Models;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
    }
}
