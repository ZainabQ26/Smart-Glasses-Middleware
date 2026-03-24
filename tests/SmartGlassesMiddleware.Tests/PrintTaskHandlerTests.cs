using NUnit.Framework;
using Moq;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class PrintTaskHandlerTests
    {
        private PrintTaskHandler _handler;

        [SetUp]
        public void Setup()
        {
            _handler = new PrintTaskHandler();
        }

        [Test]
        public void Name_ShouldBePrint()
        {
            Assert.That(_handler.Name, Is.EqualTo("print"));
        }

        [Test]
        public void Keywords_ShouldContainExpectedKeywords()
        {
            Assert.That(_handler.Keywords, Does.Contain("print document"));
        }

        [Test]
        public async Task HandleAsync_ShouldReturnPrintDocumentCommand()
        {
            var result = await _handler.HandleAsync("please print document");
            Assert.That(result, Is.EqualTo("__ACTION:PRINT_DOCUMENT__"));
        }
    }
}
