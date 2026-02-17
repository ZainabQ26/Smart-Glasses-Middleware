using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace ConversionIntegration.Tests
{
    public class ConversionIntegrationTests
    {
        private readonly string _baseUrl = "http://localhost:5000";

        [Fact]
        public async Task HtmlUpload_Is_Accepted_And_Processed()
        {
            using var client = new HttpClient { BaseAddress = new System.Uri(_baseUrl) };

            var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<html><body><h1>Hello</h1><p>HTML body</p></body></html>");
            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(htmlBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            content.Add(byteContent, "file", "sample.html");
            content.Add(new StringContent("e2e-html"), "label");

            var resp = await client.PostAsync("/api/admin/files", content);
            Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task DocxUpload_Is_Accepted_And_Processed()
        {
            using var client = new HttpClient { BaseAddress = new System.Uri(_baseUrl) };

            var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(docxBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            content.Add(byteContent, "file", "sample.docx");
            content.Add(new StringContent("e2e-docx"), "label");

            var resp = await client.PostAsync("/api/admin/files", content);
            Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        }
    }
}
