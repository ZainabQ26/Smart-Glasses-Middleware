using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests
{
    public class DocumentIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public DocumentIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    // Replace DB with in-memory
                    var dbContextDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                    if (dbContextDescriptor != null) services.Remove(dbContextDescriptor);

                    var dbDescriptor2 = services.SingleOrDefault(d => d.ServiceType == typeof(ApplicationDbContext));
                    if (dbDescriptor2 != null) services.Remove(dbDescriptor2);

                    services.AddDbContext<ApplicationDbContext>(opts => opts.UseInMemoryDatabase("DocIntegrationDb"));

                    // Replace background queue with test implementation
                    services.AddSingleton<IBackgroundTaskQueue, TestBackgroundTaskQueue>();
                });
            });
        }

        [Fact(Timeout = 20000)]
        public async Task ApiAsk_ImageUpload_Triggers_RagProcessing()
        {
            var mockStorage = new Mock<IMediaStorageService>();
            var mockRag = new Mock<IRagService>();

            // Configure mocks
            mockStorage.Setup(s => s.UploadMediaAsync(It.IsAny<System.IO.Stream>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((System.IO.Stream s, string fn, string ct) => $"storage/{fn}");

            // Build factory with our mocks
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IMediaStorageService>(mockStorage.Object);
                    services.AddSingleton<IRagService>(mockRag.Object);
                });
            });

            using var client = factory.CreateClient();

            var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF }; // minimal JPEG header
            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(imageBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            content.Add(byteContent, "files", "sample.jpg");
            content.Add(new StringContent("Describe image"), "prompt");

            var resp = await client.PostAsync("/api/ask", content);
            Assert.True(resp.StatusCode == HttpStatusCode.Accepted || resp.StatusCode == HttpStatusCode.OK);

            // Ensure RAG processing was invoked during upload
            mockRag.Verify(r => r.ProcessFileForRagAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.AtLeastOnce);
        }

        [Fact(Timeout = 20000)]
        public async Task AdminFiles_PdfUpload_Triggers_RagProcessing()
        {
            var mockStorage = new Mock<IMediaStorageService>();
            var mockRag = new Mock<IRagService>();

            mockStorage.Setup(s => s.UploadMediaAsync(It.IsAny<System.IO.Stream>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((System.IO.Stream s, string fn, string ct) => $"storage/{fn}");

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IMediaStorageService>(mockStorage.Object);
                    services.AddSingleton<IRagService>(mockRag.Object);
                });
            });

            using var client = factory.CreateClient();

            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(pdfBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(byteContent, "file", "doc.pdf");
            content.Add(new StringContent("Test PDF"), "label");

            var resp = await client.PostAsync("/api/admin/files", content);
            Assert.True(resp.StatusCode == HttpStatusCode.Created || resp.StatusCode == HttpStatusCode.OK);

            mockRag.Verify(r => r.ProcessFileForRagAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce);
        }

        [Fact(Timeout = 20000)]
        public async Task AdminFiles_XmlUpload_Triggers_RagProcessing()
        {
            var mockStorage = new Mock<IMediaStorageService>();
            var mockRag = new Mock<IRagService>();

            mockStorage.Setup(s => s.UploadMediaAsync(It.IsAny<System.IO.Stream>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((System.IO.Stream s, string fn, string ct) => $"storage/{fn}");

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IMediaStorageService>(mockStorage.Object);
                    services.AddSingleton<IRagService>(mockRag.Object);
                });
            });

            using var client = factory.CreateClient();

            var xmlBytes = System.Text.Encoding.UTF8.GetBytes("<doc><p>hello</p></doc>");
            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(xmlBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            content.Add(byteContent, "file", "sample.xml");
            content.Add(new StringContent("e2e-xml"), "label");

            var resp = await client.PostAsync("/api/admin/files", content);
            Assert.True(resp.StatusCode == HttpStatusCode.Accepted || resp.StatusCode == HttpStatusCode.OK);

            mockRag.Verify(r => r.ProcessFileForRagAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.AtLeastOnce);
        }

        [Fact(Timeout = 20000)]
        public async Task AdminFiles_HtmlUpload_Triggers_RagProcessing()
        {
            var mockStorage = new Mock<IMediaStorageService>();
            var mockRag = new Mock<IRagService>();

            mockStorage.Setup(s => s.UploadMediaAsync(It.IsAny<System.IO.Stream>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((System.IO.Stream s, string fn, string ct) => $"storage/{fn}");

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IMediaStorageService>(mockStorage.Object);
                    services.AddSingleton<IRagService>(mockRag.Object);
                });
            });

            using var client = factory.CreateClient();

            var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html><body><h1>Hello</h1><p>HTML body</p></body></html>");
            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(htmlBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            content.Add(byteContent, "file", "sample.html");
            content.Add(new StringContent("e2e-html"), "label");

            var resp = await client.PostAsync("/api/admin/files", content);
            Assert.True(resp.StatusCode == HttpStatusCode.Accepted || resp.StatusCode == HttpStatusCode.OK);

            mockRag.Verify(r => r.ProcessFileForRagAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.AtLeastOnce);
        }

        [Fact(Timeout = 20000)]
        public async Task AdminFiles_DocxUpload_Triggers_RagProcessing()
        {
            var mockStorage = new Mock<IMediaStorageService>();
            var mockRag = new Mock<IRagService>();

            mockStorage.Setup(s => s.UploadMediaAsync(It.IsAny<System.IO.Stream>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((System.IO.Stream s, string fn, string ct) => $"storage/{fn}");

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IMediaStorageService>(mockStorage.Object);
                    services.AddSingleton<IRagService>(mockRag.Object);
                });
            });

            using var client = factory.CreateClient();

            // Minimal DOCX-like bytes (ZIP header) to simulate a .docx file
            var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(docxBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            content.Add(byteContent, "file", "sample.docx");
            content.Add(new StringContent("e2e-docx"), "label");

            var resp = await client.PostAsync("/api/admin/files", content);
            Assert.True(resp.StatusCode == HttpStatusCode.Accepted || resp.StatusCode == HttpStatusCode.OK);

            mockRag.Verify(r => r.ProcessFileForRagAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.AtLeastOnce);
        }

        [Fact(Timeout = 20000)]
        public async Task DocumentsUpload_TextFile_Is_AutoNamed_And_Stored_As_Markdown()
        {
            var mockStorage = new Mock<IMediaStorageService>();

            string capturedMarkdown = null;
            mockStorage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<string>()))
                .ReturnsAsync((string key, System.IO.Stream st, string ct) =>
                {
                    try { st.Seek(0, System.IO.SeekOrigin.Begin); } catch { }
                    using var sr = new System.IO.StreamReader(st, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
                    capturedMarkdown = sr.ReadToEnd();
                    return key;
                });

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IMediaStorageService>(mockStorage.Object);
                });
            });

            using var client = factory.CreateClient();

            var text = "# Integration Title\n\nThis is the body of the document for integration test.";
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);

            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(bytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            content.Add(byteContent, "file", "test.txt");

            var resp = await client.PostAsync("/api/documents/upload", content);
            Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());

            var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var returnedFileName = body.GetProperty("fileName").GetString();

            Assert.Equal("integration-title.md", returnedFileName);

            // Verify database entry exists
            using var scope = factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var doc = await ctx.DocumentMetadatas.FirstOrDefaultAsync(d => d.FileName == returnedFileName);
            Assert.NotNull(doc);
            Assert.Contains("documents/", doc.StorageKey);

            // Ensure uploaded markdown content contains heading and body
            Assert.False(string.IsNullOrEmpty(capturedMarkdown));
            Assert.Contains("Integration Title", capturedMarkdown);
            Assert.Contains("This is the body of the document for integration test.", capturedMarkdown);
        }

        [Fact(Timeout = 30000)]
        public async Task ApiAsk_Ingestion_Produces_AutoNamed_Markdown()
        {
            var mockStorage = new Mock<IMediaStorageService>();

            // In-memory store for uploaded original files
            var originalStore = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();
            string capturedMarkdownKey = null;
            string capturedMarkdown = null;

            mockStorage.Setup(s => s.UploadMediaAsync(It.IsAny<System.IO.Stream>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((System.IO.Stream st, string fn, string ct) =>
                {
                    try { st.Seek(0, System.IO.SeekOrigin.Begin); } catch { }
                    using var ms = new System.IO.MemoryStream();
                    st.CopyTo(ms);
                    var key = $"uploads/{Guid.NewGuid():N}/{fn}";
                    originalStore[key] = ms.ToArray();
                    return key;
                });

            mockStorage.Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) =>
                {
                    if (originalStore.TryGetValue(key, out var bytes))
                    {
                        return new System.IO.MemoryStream(bytes);
                    }
                    return new System.IO.MemoryStream();
                });

            mockStorage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<string>()))
                .ReturnsAsync((string key, System.IO.Stream st, string ct) =>
                {
                    try { st.Seek(0, System.IO.SeekOrigin.Begin); } catch { }
                    using var sr = new System.IO.StreamReader(st, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
                    capturedMarkdown = sr.ReadToEnd();
                    capturedMarkdownKey = key;
                    return key;
                });

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IMediaStorageService>(mockStorage.Object);
                });
            });

            using var client = factory.CreateClient();

            // Minimal 1x1 PNG
            var pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";
            var png = Convert.FromBase64String(pngBase64);

            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(png);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(byteContent, "files", "my-fancy-title.png");
            content.Add(new StringContent("Process this image"), "prompt");

            var resp = await client.PostAsync("/api/ask", content);
            Assert.True(resp.StatusCode == HttpStatusCode.Accepted || resp.StatusCode == HttpStatusCode.OK);

            // Verify ManagedFile was created
            using var scope = factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var mf = await ctx.ManagedFiles.FirstOrDefaultAsync(m => m.FileName == "my-fancy-title.png");
            Assert.NotNull(mf);

            // Now run the processing pipeline deterministically using the stored file
            var services = scope.ServiceProvider;
            var processing = services.GetRequiredService<IDocumentProcessingService>();

            // Download original bytes via mocked DownloadFileAsync and process
            var downloadStream = await mockStorage.Object.DownloadFileAsync(mf.StorageKey);
            downloadStream.Position = 0;
            var docMeta = await processing.ProcessDocumentAsync(downloadStream, mf.FileName, mf.ContentType);

            // Ensure processing saved a markdown file and UploadFileAsync was called with a slugged filename
            Assert.NotNull(capturedMarkdownKey);
            Assert.Contains("my-fancy-title.md", capturedMarkdownKey);
            Assert.False(string.IsNullOrWhiteSpace(capturedMarkdown));
            Assert.Contains("my-fancy-title", capturedMarkdown);

            // Verify DocumentMetadata recorded
            var stored = await ctx.DocumentMetadatas.FirstOrDefaultAsync(d => d.Id == docMeta.Id);
            Assert.NotNull(stored);
            Assert.Equal(docMeta.FileName, stored.FileName);
        }

        // Simple test background queue implementation for integration tests
        private class TestBackgroundTaskQueue : IBackgroundTaskQueue
        {
            public Task EnqueueAsync(BackgroundTaskEntry entry)
            {
                // Mark as completed immediately for tests
                entry.Status = BackgroundTaskStatus.Completed;
                return Task.CompletedTask;
            }

            public ValueTask<BackgroundTaskEntry?> DequeueAsync(System.Threading.CancellationToken cancellationToken) => new ValueTask<BackgroundTaskEntry?>((BackgroundTaskEntry?)null);

            public BackgroundTaskEntry? Get(Guid id) => new BackgroundTaskEntry { Id = id, Status = BackgroundTaskStatus.Completed };

            public Task UpdateAsync(BackgroundTaskEntry entry) => Task.CompletedTask;
        }
    }
}
