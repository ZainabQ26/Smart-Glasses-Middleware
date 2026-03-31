using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;
using System;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class SelectDocumentAndOpenFileTests
    {
        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        // ── SelectDocumentTaskHandler ─────────────────────────────────────────

        [Test]
        public void SelectDocument_Name_ShouldBeSelectDocument()
        {
            using var context = CreateContext();
            var handler = new SelectDocumentTaskHandler(context);
            Assert.That(handler.Name, Is.EqualTo("select-document"));
        }

        [Test]
        public async Task SelectDocument_NoDocumentsInDb_ReturnsNoDocumentsFound()
        {
            using var context = CreateContext();
            var handler = new SelectDocumentTaskHandler(context);
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Does.Contain("No documents found"));
        }

        [Test]
        public async Task SelectDocument_DocumentMetadataExists_ReturnsSelectDocumentAction()
        {
            using var context = CreateContext();
            var docId = Guid.NewGuid();
            context.DocumentMetadatas.Add(new DocumentMetadata
            {
                Id = docId,
                FileName = "report.pdf",
                OriginalFileName = "report.pdf",
                StorageKey = "s3://bucket/report.pdf",
                UploadedAt = DateTime.UtcNow,
                FileSize = 1024
            });
            await context.SaveChangesAsync();

            var handler = new SelectDocumentTaskHandler(context);
            var result = await handler.HandleAsync(string.Empty);

            Assert.That(result, Does.StartWith("__ACTION:SELECT_DOCUMENT:"));
        }

        [Test]
        public async Task SelectDocument_DocumentMetadataWithManagedFileId_UsesManagedFileId()
        {
            using var context = CreateContext();
            var managedFileId = Guid.NewGuid();
            var docId = Guid.NewGuid();

            context.ManagedFiles.Add(new ManagedFile
            {
                Id = managedFileId,
                FileName = "managed.pdf",
                Label = "Managed File",
                ContentType = "application/pdf",
                StorageKey = "s3://bucket/managed.pdf",
                UploadedAt = DateTime.UtcNow,
                FileSize = 2048
            });

            context.DocumentMetadatas.Add(new DocumentMetadata
            {
                Id = docId,
                FileName = "processed.pdf",
                OriginalFileName = "processed.pdf",
                StorageKey = "s3://bucket/processed.pdf",
                UploadedAt = DateTime.UtcNow,
                FileSize = 2048,
                ManagedFileId = managedFileId
            });
            await context.SaveChangesAsync();

            var handler = new SelectDocumentTaskHandler(context);
            var result = await handler.HandleAsync(string.Empty);

            Assert.That(result, Does.Contain(managedFileId.ToString()));
        }

        [Test]
        public async Task SelectDocument_NoDocumentMetadataButManagedFileExists_UsesManagedFile()
        {
            using var context = CreateContext();
            var managedFileId = Guid.NewGuid();

            context.ManagedFiles.Add(new ManagedFile
            {
                Id = managedFileId,
                FileName = "upload.pdf",
                Label = "My Upload",
                ContentType = "application/pdf",
                StorageKey = "s3://bucket/upload.pdf",
                UploadedAt = DateTime.UtcNow,
                FileSize = 512
            });
            await context.SaveChangesAsync();

            var handler = new SelectDocumentTaskHandler(context);
            var result = await handler.HandleAsync(string.Empty);

            Assert.That(result, Does.StartWith("__ACTION:SELECT_DOCUMENT:"));
            Assert.That(result, Does.Contain(managedFileId.ToString()));
        }

        [Test]
        public async Task SelectDocument_DocumentMetadataWithFailedStorageKey_FallsBackToManagedFile()
        {
            using var context = CreateContext();
            var managedFileId = Guid.NewGuid();

            context.DocumentMetadatas.Add(new DocumentMetadata
            {
                Id = Guid.NewGuid(),
                FileName = "failed.pdf",
                OriginalFileName = "failed.pdf",
                StorageKey = "FAILED:processing error",
                UploadedAt = DateTime.UtcNow,
                FileSize = 0
            });

            context.ManagedFiles.Add(new ManagedFile
            {
                Id = managedFileId,
                FileName = "fallback.pdf",
                Label = "Fallback",
                ContentType = "application/pdf",
                StorageKey = "s3://bucket/fallback.pdf",
                UploadedAt = DateTime.UtcNow,
                FileSize = 1024
            });
            await context.SaveChangesAsync();

            var handler = new SelectDocumentTaskHandler(context);
            var result = await handler.HandleAsync(string.Empty);

            Assert.That(result, Does.Contain(managedFileId.ToString()));
        }

        // ── OpenFileTaskHandler ───────────────────────────────────────────────

        [Test]
        public void OpenFile_Name_ShouldBeOpenFile()
        {
            using var context = CreateContext();
            var handler = new OpenFileTaskHandler(context);
            Assert.That(handler.Name, Is.EqualTo("open-file"));
        }

        [Test]
        public async Task OpenFile_NoFilesInDb_ReturnsNoUploadedFilesFound()
        {
            using var context = CreateContext();
            var handler = new OpenFileTaskHandler(context);
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Does.Contain("No uploaded files found"));
        }

        [Test]
        public async Task OpenFile_ManagedFileExists_ReturnsShowRecentFilesAction()
        {
            using var context = CreateContext();
            context.ManagedFiles.Add(new ManagedFile
            {
                Id = Guid.NewGuid(),
                FileName = "document.pdf",
                Label = "My Document",
                ContentType = "application/pdf",
                StorageKey = "s3://bucket/document.pdf",
                UploadedAt = DateTime.UtcNow,
                FileSize = 4096
            });
            await context.SaveChangesAsync();

            var handler = new OpenFileTaskHandler(context);
            var result = await handler.HandleAsync(string.Empty);

            Assert.That(result, Is.EqualTo("__ACTION:SHOW_RECENT_FILES__"));
        }

        [Test]
        public async Task OpenFile_FileWithZeroSize_ReturnsNoUploadedFilesFound()
        {
            using var context = CreateContext();
            context.ManagedFiles.Add(new ManagedFile
            {
                Id = Guid.NewGuid(),
                FileName = "empty.pdf",
                Label = "",
                ContentType = "application/pdf",
                StorageKey = "s3://bucket/empty.pdf",
                UploadedAt = DateTime.UtcNow,
                FileSize = 0
            });
            await context.SaveChangesAsync();

            var handler = new OpenFileTaskHandler(context);
            var result = await handler.HandleAsync(string.Empty);

            Assert.That(result, Does.Contain("No uploaded files found"));
        }
    }
}
