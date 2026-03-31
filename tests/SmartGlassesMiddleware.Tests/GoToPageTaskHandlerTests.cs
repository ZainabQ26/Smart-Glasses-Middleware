using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class GoToPageTaskHandlerTests
    {
        private GoToPageTaskHandler _handler = null!;

        [SetUp]
        public void Setup()
        {
            _handler = new GoToPageTaskHandler();
        }

        [Test]
        public void Name_ShouldBeGoToPage()
        {
            Assert.That(_handler.Name, Is.EqualTo("go-to-page"));
        }

        [Test]
        public void Keywords_ShouldContainGoToPage()
        {
            Assert.That(_handler.Keywords, Does.Contain("go to page"));
        }

        [Test]
        public void Keywords_ShouldNotBeEmpty()
        {
            Assert.That(_handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public async Task HandleAsync_GoToPage5_ReturnsCorrectToken()
        {
            var result = await _handler.HandleAsync("go to page 5");
            Assert.That(result, Is.EqualTo("__ACTION:GO_TO_PAGE:5__"));
        }

        [Test]
        public async Task HandleAsync_JumpToPage12_ReturnsCorrectToken()
        {
            var result = await _handler.HandleAsync("jump to page 12");
            Assert.That(result, Is.EqualTo("__ACTION:GO_TO_PAGE:12__"));
        }

        [Test]
        public async Task HandleAsync_Page3_ReturnsCorrectToken()
        {
            var result = await _handler.HandleAsync("page 3");
            Assert.That(result, Is.EqualTo("__ACTION:GO_TO_PAGE:3__"));
        }

        [Test]
        public async Task HandleAsync_OrdinalSuffix_ReturnsCorrectToken()
        {
            var result = await _handler.HandleAsync("go to page 1st");
            Assert.That(result, Is.EqualTo("__ACTION:GO_TO_PAGE:1__"));
        }

        [Test]
        public async Task HandleAsync_EmptyInput_ReturnsErrorMessage()
        {
            var result = await _handler.HandleAsync(string.Empty);
            Assert.That(result, Does.Contain("Please say a page number"));
        }

        [Test]
        public async Task HandleAsync_NoNumber_ReturnsErrorMessage()
        {
            var result = await _handler.HandleAsync("go to next");
            Assert.That(result, Does.Contain("Please say a page number"));
        }
    }
}
