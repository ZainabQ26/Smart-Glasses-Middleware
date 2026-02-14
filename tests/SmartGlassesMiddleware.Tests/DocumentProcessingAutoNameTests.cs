using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Tests
{
    public class DocumentProcessingAutoNameTests
    {
        [Fact(Timeout = 20000)]
        public async Task ProcessDocumentAsync_Generates_Slugified_FileName_From_MarkdownHeading()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);

            var mockStorage = new Mock<IMediaStorageService>();
            string capturedContent = null;
            mockStorage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync((string key, Stream st, string ct) =>
                {
                    try
                    {
                        st.Seek(0, SeekOrigin.Begin);
                    }
                    catch { }
                    using var sr = new StreamReader(st, Encoding.UTF8, true, 1024, leaveOpen: true);
                    var content = sr.ReadToEnd();
                    capturedContent = content;
                    return key;
                });

            var logger = new NullLogger<DocumentProcessingService>();
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

            var svc = new DocumentProcessingService(logger, mockStorage.Object, context, null, config);

            var markdown = "# My Fancy Title\n\nThis is the content of the document.";
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

            // Act
            var meta = await svc.ProcessDocumentAsync(ms, "upload.txt", "text/plain");

            // Assert
            Assert.NotNull(meta);
            Assert.Equal("my-fancy-title.md", meta.FileName);
            // Ensure storage key used contains the generated file name
            Assert.Contains(meta.FileName, meta.StorageKey);
            // Ensure the uploaded content (markdown) contains the heading and body text
            Assert.False(string.IsNullOrEmpty(capturedContent), "Uploaded content should not be empty");
            Assert.Contains("My Fancy Title", capturedContent);
            Assert.Contains("This is the content of the document.", capturedContent);
        }
    }
}
