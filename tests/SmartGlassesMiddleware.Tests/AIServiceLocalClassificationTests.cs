using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests;

/// <summary>
/// Verifies that AIService classifies ALL known commands locally — no external AI
/// (Bedrock / advanced model) is invoked for recognised commands.
/// </summary>
[TestFixture]
public class AIServiceLocalClassificationTests
{
    private AIService _service = null!;
    private Mock<ITaskDispatcher> _dispatcherMock = null!;

    [SetUp]
    public void Setup()
    {
        // Kernel that has no chat completion service — any attempt to call Bedrock will throw.
        var kernel = Kernel.CreateBuilder().Build();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:UseAdvancedModel"] = "false",
                ["AI:AdvancedModelEndpoint"] = "",
                ["AI:KnownTasks:0"] = "reminder",
                ["AI:KnownTasks:1"] = "edit-reminder",
                ["AI:KnownTasks:2"] = "delete-reminder",
                ["AI:KnownTasks:3"] = "show-reminders",
                ["AI:KnownTasks:4"] = "open-file",
                ["AI:KnownTasks:5"] = "select-document",
                ["AI:KnownTasks:6"] = "next-page",
                ["AI:KnownTasks:7"] = "previous-page",
                ["AI:KnownTasks:8"] = "next-highlight",
                ["AI:KnownTasks:9"] = "previous-highlight",
                ["AI:KnownTasks:10"] = "summarize-page",
                ["AI:KnownTasks:11"] = "highlight-word",
                ["AI:KnownTasks:12"] = "open-admin-panel",
                ["AI:KnownTasks:13"] = "close-document",
                ["AI:KnownTasks:14"] = "go-to-page",
                ["AI:KnownTasks:15"] = "go-to-loinc",
                ["AI:KnownTasks:16"] = "print",
                ["AI:KnownTasks:17"] = "print-highlighted-pages",
                ["AI:KnownTasks:18"] = "remove-highlights",
                ["AI:KnownTasks:19"] = "document-search",
                ["AI:KnownTasks:20"] = "document-query",
                ["AI:KnownTasks:21"] = "document-highlight",
                ["AI:KnownTasks:22"] = "ocr",
                ["AI:KnownTasks:23"] = "ocr-retry",
                ["AI:KnownTasks:24"] = "advanced",
            })
            .Build();

        _dispatcherMock = new Mock<ITaskDispatcher>();

        // Return known tasks from config
        var knownTasks = new List<string>
        {
            "reminder","edit-reminder","delete-reminder","show-reminders",
            "open-file","select-document","next-page","previous-page","next-highlight","previous-highlight","summarize-page",
            "highlight-word","open-admin-panel","close-document","go-to-page","go-to-loinc",
            "print","print-highlighted-pages","remove-highlights","document-search",
            "document-query","document-highlight","ocr","ocr-retry","advanced"
        };
        _dispatcherMock.Setup(d => d.KnownTasks).Returns(knownTasks);
        // By default keyword matching returns null (rule-based takes precedence)
        _dispatcherMock.Setup(d => d.TryMatchByKeyword(It.IsAny<string>())).Returns((string?)null);

        var logger = Mock.Of<ILogger<AIService>>();
        _service = new AIService(kernel, new HttpClient(), config, logger, _dispatcherMock.Object);
    }

    // ── reminder ──────────────────────────────────────────────────────────────

    [TestCase("remind me to call John")]
    [TestCase("set a reminder for tomorrow")]
    [TestCase("add a reminder about the meeting")]
    [TestCase("create reminder to take medication")]
    [TestCase("dont forget to buy milk")]
    public async Task ClassifyOnlyAsync_ReminderVariants_ReturnsReminder(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("reminder"), $"Input: '{input}'");
    }

    // ── edit-reminder ─────────────────────────────────────────────────────────

    [TestCase("edit reminder about dentist")]
    [TestCase("update my reminder")]
    [TestCase("change reminder to 3pm")]
    public async Task ClassifyOnlyAsync_EditReminderVariants_ReturnsEditReminder(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("edit-reminder"), $"Input: '{input}'");
    }

    // ── delete-reminder ───────────────────────────────────────────────────────

    [TestCase("delete reminder")]
    [TestCase("cancel the reminder")]
    [TestCase("remove reminder about meeting")]
    public async Task ClassifyOnlyAsync_DeleteReminderVariants_ReturnsDeleteReminder(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("delete-reminder"), $"Input: '{input}'");
    }

    // ── show-reminders ────────────────────────────────────────────────────────

    [TestCase("show reminders")]
    [TestCase("what are my reminders")]
    [TestCase("list reminders")]
    [TestCase("show my reminders")]
    public async Task ClassifyOnlyAsync_ShowRemindersVariants_ReturnsShowReminders(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("show-reminders"), $"Input: '{input}'");
    }

    // ── close-document ────────────────────────────────────────────────────────

    [TestCase("close document")]
    [TestCase("close the pdf")]
    [TestCase("exit document")]
    public async Task ClassifyOnlyAsync_CloseDocumentVariants_ReturnsCloseDocument(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("close-document"), $"Input: '{input}'");
    }

    // ── go-to-page ────────────────────────────────────────────────────────────

    [TestCase("go to page 5")]
    [TestCase("jump to page 10")]
    [TestCase("navigate to page 3")]
    public async Task ClassifyOnlyAsync_GoToPageVariants_ReturnsGoToPage(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("go-to-page"), $"Input: '{input}'");
    }

    // ── go-to-loinc ───────────────────────────────────────────────────────────

    [TestCase("go to loinc page")]
    [TestCase("find loinc code 8480-6")]
    [TestCase("loinc 8480-6")]
    public async Task ClassifyOnlyAsync_GoToLoincVariants_ReturnsGoToLoinc(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("go-to-loinc"), $"Input: '{input}'");
    }

    // ── print-highlighted-pages ───────────────────────────────────────────────

    [TestCase("print highlighted pages")]
    [TestCase("print the highlighted")]
    public async Task ClassifyOnlyAsync_PrintHighlightedVariants_ReturnsPrintHighlightedPages(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("print-highlighted-pages"), $"Input: '{input}'");
    }

    // ── print ─────────────────────────────────────────────────────────────────

    [TestCase("print document")]
    [TestCase("print the pdf")]
    [TestCase("print this page")]
    public async Task ClassifyOnlyAsync_PrintVariants_ReturnsPrint(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("print"), $"Input: '{input}'");
    }

    // ── remove-highlights ─────────────────────────────────────────────────────

    [TestCase("remove highlights")]
    [TestCase("clear all highlights")]
    [TestCase("delete highlights")]
    public async Task ClassifyOnlyAsync_RemoveHighlightsVariants_ReturnsRemoveHighlights(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("remove-highlights"), $"Input: '{input}'");
    }

    // ── select-document ───────────────────────────────────────────────────────

    [TestCase("select document")]
    [TestCase("open the last document")]
    [TestCase("latest file")]
    [TestCase("open latest document")]
    public async Task ClassifyOnlyAsync_SelectDocumentVariants_ReturnsSelectDocument(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("select-document"), $"Input: '{input}'");
    }

    // ── open-file ─────────────────────────────────────────────────────────────

    [TestCase("open file")]
    [TestCase("show recent files")]
    [TestCase("browse documents")]
    [TestCase("my files")]
    public async Task ClassifyOnlyAsync_OpenFileVariants_ReturnsOpenFile(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("open-file"), $"Input: '{input}'");
    }

    // ── next-page ─────────────────────────────────────────────────────────────

    [TestCase("next page")]
    [TestCase("page down")]
    public async Task ClassifyOnlyAsync_NextPageVariants_ReturnsNextPage(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("next-page"), $"Input: '{input}'");
    }

    // ── previous-page ─────────────────────────────────────────────────────────

    [TestCase("previous page")]
    [TestCase("page up")]
    [TestCase("go back a page")]
    public async Task ClassifyOnlyAsync_PreviousPageVariants_ReturnsPreviousPage(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("previous-page"), $"Input: '{input}'");
    }

    // ── next-highlight ───────────────────────────────────────────────────────

    [TestCase("next highlight")]
    [TestCase("next highlighted word")]
    [TestCase("go to next highlight")]
    public async Task ClassifyOnlyAsync_NextHighlightVariants_ReturnsNextHighlight(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("next-highlight"), $"Input: '{input}'");
    }

    // ── previous-highlight ───────────────────────────────────────────────────

    [TestCase("previous highlight")]
    [TestCase("prev highlight")]
    [TestCase("go to previous highlight")]
    public async Task ClassifyOnlyAsync_PreviousHighlightVariants_ReturnsPreviousHighlight(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("previous-highlight"), $"Input: '{input}'");
    }

    // ── summarize-page ────────────────────────────────────────────────────────

    [TestCase("summarize this page")]
    [TestCase("summarize page")]
    [TestCase("what is on this page")]
    public async Task ClassifyOnlyAsync_SummarizePageVariants_ReturnsSummarizePage(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("summarize-page"), $"Input: '{input}'");
    }

    // ── document-query ────────────────────────────────────────────────────────

    [TestCase("ask the document about dosage")]
    [TestCase("what does the document say about side effects")]
    [TestCase("query document for instructions")]
    public async Task ClassifyOnlyAsync_DocumentQueryVariants_ReturnsDocumentQuery(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("document-query"), $"Input: '{input}'");
    }

    // ── highlight-word ────────────────────────────────────────────────────────

    [TestCase("highlight headache")]
    [TestCase("find the word dosage")]
    [TestCase("search for fever")]
    public async Task ClassifyOnlyAsync_HighlightWordVariants_ReturnsHighlightWord(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("highlight-word"), $"Input: '{input}'");
    }

    // ── open-admin-panel ──────────────────────────────────────────────────────

    [TestCase("open admin panel")]
    [TestCase("go to admin")]
    [TestCase("show admin")]
    public async Task ClassifyOnlyAsync_OpenAdminPanelVariants_ReturnsOpenAdminPanel(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("open-admin-panel"), $"Input: '{input}'");
    }

    // ── ocr-retry ─────────────────────────────────────────────────────────────

    [TestCase("retry ocr")]
    [TestCase("redo ocr")]
    [TestCase("try ocr again")]
    public async Task ClassifyOnlyAsync_OcrRetryVariants_ReturnsOcrRetry(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("ocr-retry"), $"Input: '{input}'");
    }

    // ── ocr ───────────────────────────────────────────────────────────────────

    [TestCase("scan text")]
    [TestCase("extract text from image")]
    [TestCase("perform ocr")]
    public async Task ClassifyOnlyAsync_OcrVariants_ReturnsOcr(string input)
    {
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("ocr"), $"Input: '{input}'");
    }

    // ── unknown → no external AI ──────────────────────────────────────────────

    [TestCase("what is the capital of France")]
    [TestCase("tell me a joke")]
    [TestCase("how do I fix a broken pipe")]
    public async Task ClassifyOnlyAsync_UnrecognisedInput_ReturnsUnknown_NoBedrock(string input)
    {
        // If Bedrock were called it would throw because the kernel has no chat service.
        // The test passing proves no external AI was invoked.
        var result = await _service.ClassifyOnlyAsync(input);
        Assert.That(result, Is.EqualTo("unknown"), $"Input: '{input}'");
    }

    // ── WillUseAdvancedModelAsync ──────────────────────────────────────────────

    [Test]
    public async Task WillUseAdvancedModelAsync_RecognisedCommand_ReturnsFalse()
    {
        var result = await _service.WillUseAdvancedModelAsync("remind me to call John");
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task WillUseAdvancedModelAsync_UnknownCommandAdvancedDisabled_ReturnsFalse()
    {
        // UseAdvancedModel = false in config
        var result = await _service.WillUseAdvancedModelAsync("tell me a joke");
        Assert.That(result, Is.False);
    }

    // ── ProcessRequestAsync routes unknown → advanced only ────────────────────

    [Test]
    public async Task ProcessRequestAsync_KnownCommand_HandledLocally_NoAdvancedModel()
    {
        _dispatcherMock
            .Setup(d => d.TryHandleAsync("reminder", It.IsAny<string>()))
            .ReturnsAsync((true, "Reminder saved."));

        var (task, response, usedAdvanced) = await _service.ProcessRequestAsync("remind me to call John");

        Assert.That(task, Is.EqualTo("reminder"));
        Assert.That(usedAdvanced, Is.False);
        Assert.That(response, Does.Contain("Reminder saved"));
        // Advanced handler must NOT have been called
        _dispatcherMock.Verify(d => d.TryHandleAsync("advanced", It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ProcessRequestAsync_UnknownCommand_AdvancedDisabled_ReturnsUnrecognisedMessage()
    {
        _dispatcherMock
            .Setup(d => d.TryMatchByKeyword(It.IsAny<string>()))
            .Returns((string?)null);

        var (task, response, usedAdvanced) = await _service.ProcessRequestAsync("what is the meaning of life");

        Assert.That(usedAdvanced, Is.False);
        Assert.That(response, Does.Contain("didn't recognise").IgnoreCase.Or.Contain("not recognised").IgnoreCase.Or.Contain("unrecognised").IgnoreCase);
        // Advanced handler must NOT have been called (it is disabled)
        _dispatcherMock.Verify(d => d.TryHandleAsync("advanced", It.IsAny<string>()), Times.Never);
    }
}
