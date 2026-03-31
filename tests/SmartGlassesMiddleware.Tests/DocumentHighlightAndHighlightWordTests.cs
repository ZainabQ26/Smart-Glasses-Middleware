using NUnit.Framework;
using Moq;
using SmartGlassesMiddleware.Services;
using SmartGlassesMiddleware.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class DocumentHighlightAndHighlightWordTests
    {
        private Mock<IAbbreviationService> _mockAbbreviationService = null!;

        [SetUp]
        public void Setup()
        {
            _mockAbbreviationService = new Mock<IAbbreviationService>();

            // Default: no abbreviations found — return the single search term
            _mockAbbreviationService
                .Setup(s => s.GetRelatedTermsAsync(It.IsAny<string>()))
                .ReturnsAsync((string term) =>
                    ((List<string>)[term], new List<Abbreviation>()));
        }

        // ── DocumentHighlightTaskHandler ─────────────────────────────────────

        [Test]
        public void DocumentHighlight_Name_ShouldBeDocumentHighlight()
        {
            var handler = new DocumentHighlightTaskHandler(_mockAbbreviationService.Object);
            Assert.That(handler.Name, Is.EqualTo("document-highlight"));
        }

        [Test]
        public async Task DocumentHighlight_EmptyInput_ReturnsPromptMessage()
        {
            var handler = new DocumentHighlightTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Does.Contain("Please say what you want to highlight"));
        }

        [Test]
        public async Task DocumentHighlight_SingleTerm_NoAbbreviation_ReturnsActionToken()
        {
            var handler = new DocumentHighlightTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("highlight example");
            Assert.That(result, Is.EqualTo("__ACTION:HIGHLIGHT_WORD:example__"));
        }

        [Test]
        public async Task DocumentHighlight_WithAbbreviation_ReturnsMultiTermActionToken()
        {
            _mockAbbreviationService
                .Setup(s => s.GetRelatedTermsAsync("ACE"))
                .ReturnsAsync((
                    new List<string> { "ACE", "angiotensin converting enzyme" },
                    new List<Abbreviation>
                    {
                        new Abbreviation { ShortForm = "ACE", ExpandedForm = "angiotensin converting enzyme" }
                    }));

            var handler = new DocumentHighlightTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("highlight ACE");
            Assert.That(result, Is.EqualTo("__ACTION:HIGHLIGHT_WORD:ACE|angiotensin converting enzyme__"));
        }

        [Test]
        public async Task DocumentHighlight_StripsPrefixHighlightTheWord_ReturnsCorrectToken()
        {
            var handler = new DocumentHighlightTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("highlight the word test");
            Assert.That(result, Is.EqualTo("__ACTION:HIGHLIGHT_WORD:test__"));
        }

        [Test]
        public async Task DocumentHighlight_WhitespaceInput_ReturnsPromptMessage()
        {
            var handler = new DocumentHighlightTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("   ");
            Assert.That(result, Does.Contain("Please say what you want to highlight"));
        }

        // ── HighlightWordTaskHandler ──────────────────────────────────────────

        [Test]
        public void HighlightWord_Name_ShouldBeHighlightWord()
        {
            var handler = new HighlightWordTaskHandler(_mockAbbreviationService.Object);
            Assert.That(handler.Name, Is.EqualTo("highlight-word"));
        }

        [Test]
        public async Task HighlightWord_EmptyInput_ReturnsPromptMessage()
        {
            var handler = new HighlightWordTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync(string.Empty);
            Assert.That(result, Does.Contain("Please say what you want to highlight"));
        }

        [Test]
        public async Task HighlightWord_HighlightTheWord_ReturnsCorrectToken()
        {
            var handler = new HighlightWordTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("highlight the word security");
            Assert.That(result, Is.EqualTo("__ACTION:HIGHLIGHT_WORD:security__"));
        }

        [Test]
        public async Task HighlightWord_SearchForPrefix_ReturnsCorrectToken()
        {
            var handler = new HighlightWordTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("search for patient");
            Assert.That(result, Is.EqualTo("__ACTION:HIGHLIGHT_WORD:patient__"));
        }

        [Test]
        public async Task HighlightWord_FindThePhrasePrefix_ReturnsCorrectToken()
        {
            var handler = new HighlightWordTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("find the phrase blood pressure");
            Assert.That(result, Is.EqualTo("__ACTION:HIGHLIGHT_WORD:blood pressure__"));
        }

        [Test]
        public async Task HighlightWord_WithAbbreviations_IncludesAllTerms()
        {
            _mockAbbreviationService
                .Setup(s => s.GetRelatedTermsAsync("ACE"))
                .ReturnsAsync((
                    new List<string> { "ACE", "angiotensin converting enzyme" },
                    new List<Abbreviation>
                    {
                        new Abbreviation { ShortForm = "ACE", ExpandedForm = "angiotensin converting enzyme" }
                    }));

            var handler = new HighlightWordTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("highlight ACE");
            Assert.That(result, Does.Contain("ACE"));
            Assert.That(result, Does.Contain("angiotensin converting enzyme"));
        }

        [Test]
        public async Task HighlightWord_WhitespaceInput_ReturnsPromptMessage()
        {
            var handler = new HighlightWordTaskHandler(_mockAbbreviationService.Object);
            var result = await handler.HandleAsync("   ");
            Assert.That(result, Does.Contain("Please say what you want to highlight"));
        }
    }
}
