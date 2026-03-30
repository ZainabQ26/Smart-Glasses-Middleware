using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class SimpleTaskHandlerTests
    {
        // ── AdminPanelTaskHandler ────────────────────────────────────────────

        [Test]
        public void AdminPanel_Name_ShouldBeOpenAdminPanel()
        {
            var handler = new AdminPanelTaskHandler();
            Assert.That(handler.Name, Is.EqualTo("open-admin-panel"));
        }

        [Test]
        public void AdminPanel_Keywords_ShouldNotBeEmpty()
        {
            var handler = new AdminPanelTaskHandler();
            Assert.That(handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public void AdminPanel_Keywords_ShouldContainExpectedTerms()
        {
            var handler = new AdminPanelTaskHandler();
            Assert.That(handler.Keywords, Does.Contain("admin panel"));
        }

        [Test]
        public async Task AdminPanel_HandleAsync_ReturnsActionToken()
        {
            var handler = new AdminPanelTaskHandler();
            var result = await handler.HandleAsync("open admin panel");
            Assert.That(result, Is.EqualTo("__ACTION:OPEN_ADMIN_PANEL__"));
        }

        [Test]
        public async Task AdminPanel_HandleAsync_EmptyInput_ReturnsActionToken()
        {
            var handler = new AdminPanelTaskHandler();
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Is.EqualTo("__ACTION:OPEN_ADMIN_PANEL__"));
        }

        // ── CloseDocumentTaskHandler ─────────────────────────────────────────

        [Test]
        public void CloseDocument_Name_ShouldBeCloseDocument()
        {
            var handler = new CloseDocumentTaskHandler();
            Assert.That(handler.Name, Is.EqualTo("close-document"));
        }

        [Test]
        public void CloseDocument_Keywords_ShouldNotBeEmpty()
        {
            var handler = new CloseDocumentTaskHandler();
            Assert.That(handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public void CloseDocument_Keywords_ShouldContainExpectedTerms()
        {
            var handler = new CloseDocumentTaskHandler();
            Assert.That(handler.Keywords, Does.Contain("close document"));
        }

        [Test]
        public async Task CloseDocument_HandleAsync_ReturnsActionToken()
        {
            var handler = new CloseDocumentTaskHandler();
            var result = await handler.HandleAsync("close the document");
            Assert.That(result, Is.EqualTo("__ACTION:CLOSE_DOCUMENT__"));
        }

        [Test]
        public async Task CloseDocument_HandleAsync_EmptyInput_ReturnsActionToken()
        {
            var handler = new CloseDocumentTaskHandler();
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Is.EqualTo("__ACTION:CLOSE_DOCUMENT__"));
        }

        // ── NextPageTaskHandler ───────────────────────────────────────────────

        [Test]
        public void NextPage_Name_ShouldBeNextPage()
        {
            var handler = new NextPageTaskHandler();
            Assert.That(handler.Name, Is.EqualTo("next-page"));
        }

        [Test]
        public void NextPage_Keywords_ShouldNotBeEmpty()
        {
            var handler = new NextPageTaskHandler();
            Assert.That(handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public void NextPage_Keywords_ShouldContainExpectedTerms()
        {
            var handler = new NextPageTaskHandler();
            Assert.That(handler.Keywords, Does.Contain("next page"));
        }

        [Test]
        public async Task NextPage_HandleAsync_ReturnsActionToken()
        {
            var handler = new NextPageTaskHandler();
            var result = await handler.HandleAsync("next page");
            Assert.That(result, Is.EqualTo("__ACTION:NEXT_PDF_PAGE__"));
        }

        [Test]
        public async Task NextPage_HandleAsync_EmptyInput_ReturnsActionToken()
        {
            var handler = new NextPageTaskHandler();
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Is.EqualTo("__ACTION:NEXT_PDF_PAGE__"));
        }

        // ── PreviousPageTaskHandler ───────────────────────────────────────────

        [Test]
        public void PreviousPage_Name_ShouldBePreviousPage()
        {
            var handler = new PreviousPageTaskHandler();
            Assert.That(handler.Name, Is.EqualTo("previous-page"));
        }

        [Test]
        public void PreviousPage_Keywords_ShouldNotBeEmpty()
        {
            var handler = new PreviousPageTaskHandler();
            Assert.That(handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public void PreviousPage_Keywords_ShouldContainExpectedTerms()
        {
            var handler = new PreviousPageTaskHandler();
            Assert.That(handler.Keywords, Does.Contain("previous page"));
        }

        [Test]
        public async Task PreviousPage_HandleAsync_ReturnsActionToken()
        {
            var handler = new PreviousPageTaskHandler();
            var result = await handler.HandleAsync("previous page");
            Assert.That(result, Is.EqualTo("__ACTION:PREVIOUS_PDF_PAGE__"));
        }

        [Test]
        public async Task PreviousPage_HandleAsync_EmptyInput_ReturnsActionToken()
        {
            var handler = new PreviousPageTaskHandler();
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Is.EqualTo("__ACTION:PREVIOUS_PDF_PAGE__"));
        }

        // ── RemoveHighlightsTaskHandler ───────────────────────────────────────

        [Test]
        public void RemoveHighlights_Name_ShouldBeRemoveHighlights()
        {
            var handler = new RemoveHighlightsTaskHandler();
            Assert.That(handler.Name, Is.EqualTo("remove-highlights"));
        }

        [Test]
        public void RemoveHighlights_Keywords_ShouldNotBeEmpty()
        {
            var handler = new RemoveHighlightsTaskHandler();
            Assert.That(handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public void RemoveHighlights_Keywords_ShouldContainExpectedTerms()
        {
            var handler = new RemoveHighlightsTaskHandler();
            Assert.That(handler.Keywords, Does.Contain("remove highlights"));
        }

        [Test]
        public async Task RemoveHighlights_HandleAsync_ReturnsActionToken()
        {
            var handler = new RemoveHighlightsTaskHandler();
            var result = await handler.HandleAsync("remove highlights");
            Assert.That(result, Is.EqualTo("__ACTION:REMOVE_HIGHLIGHTS__"));
        }

        [Test]
        public async Task RemoveHighlights_HandleAsync_EmptyInput_ReturnsActionToken()
        {
            var handler = new RemoveHighlightsTaskHandler();
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Is.EqualTo("__ACTION:REMOVE_HIGHLIGHTS__"));
        }

        // ── SummarizePageTaskHandler ──────────────────────────────────────────

        [Test]
        public void SummarizePage_Name_ShouldBeSummarizePage()
        {
            var handler = new SummarizePageTaskHandler();
            Assert.That(handler.Name, Is.EqualTo("summarize-page"));
        }

        [Test]
        public void SummarizePage_Keywords_ShouldNotBeEmpty()
        {
            var handler = new SummarizePageTaskHandler();
            Assert.That(handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public void SummarizePage_Keywords_ShouldContainExpectedTerms()
        {
            var handler = new SummarizePageTaskHandler();
            Assert.That(handler.Keywords, Does.Contain("summarize page"));
        }

        [Test]
        public async Task SummarizePage_HandleAsync_ReturnsActionToken()
        {
            var handler = new SummarizePageTaskHandler();
            var result = await handler.HandleAsync("summarize this page");
            Assert.That(result, Is.EqualTo("__ACTION:SUMMARIZE_CURRENT_PAGE__"));
        }

        [Test]
        public async Task SummarizePage_HandleAsync_EmptyInput_ReturnsActionToken()
        {
            var handler = new SummarizePageTaskHandler();
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Is.EqualTo("__ACTION:SUMMARIZE_CURRENT_PAGE__"));
        }

        // ── PrintHighlightedPagesTaskHandler ──────────────────────────────────

        [Test]
        public void PrintHighlightedPages_Name_ShouldBePrintHighlightedPages()
        {
            var handler = new PrintHighlightedPagesTaskHandler();
            Assert.That(handler.Name, Is.EqualTo("print-highlighted-pages"));
        }

        [Test]
        public void PrintHighlightedPages_Keywords_ShouldNotBeEmpty()
        {
            var handler = new PrintHighlightedPagesTaskHandler();
            Assert.That(handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public void PrintHighlightedPages_Keywords_ShouldContainExpectedTerms()
        {
            var handler = new PrintHighlightedPagesTaskHandler();
            Assert.That(handler.Keywords, Does.Contain("print highlighted pages"));
        }

        [Test]
        public async Task PrintHighlightedPages_HandleAsync_ReturnsActionToken()
        {
            var handler = new PrintHighlightedPagesTaskHandler();
            var result = await handler.HandleAsync("print highlighted pages");
            Assert.That(result, Is.EqualTo("__ACTION:PRINT_HIGHLIGHTED_PAGES__"));
        }

        [Test]
        public async Task PrintHighlightedPages_HandleAsync_EmptyInput_ReturnsActionToken()
        {
            var handler = new PrintHighlightedPagesTaskHandler();
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Is.EqualTo("__ACTION:PRINT_HIGHLIGHTED_PAGES__"));
        }
    }
}
