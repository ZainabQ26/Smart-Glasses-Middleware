using NUnit.Framework;
using Moq;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class TemplateTaskHandlerTests
    {
        private TemplateTaskHandler _handler;

        [SetUp]
        public void Setup()
        {
            _handler = new TemplateTaskHandler();
        }

        [Test]
        public void Name_ShouldBeTemplate()
        {
            Assert.That(_handler.Name, Is.EqualTo("template"));
        }

        [Test]
        public void Keywords_ShouldBeEmpty()
        {
            Assert.That(_handler.Keywords, Is.Empty);
        }

        [Test]
        public async Task HandleAsync_ShouldReturnTemplateHandlerReceived()
        {
            var result = await _handler.HandleAsync("test");
            Assert.That(result, Does.Contain("Template handler received"));
        }
    }
}
