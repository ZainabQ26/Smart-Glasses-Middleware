using NUnit.Framework;
using Moq;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class TimeTaskHandlerTests
    {
        private TimeTaskHandler _handler;

        [SetUp]
        public void Setup()
        {
            _handler = new TimeTaskHandler();
        }

        [Test]
        public void Name_ShouldBeTime()
        {
            Assert.That(_handler.Name, Is.EqualTo("time"));
        }

        [Test]
        public void Keywords_ShouldContainExpectedKeywords()
        {
            Assert.That(_handler.Keywords, Does.Contain("what time"));
            Assert.That(_handler.Keywords, Does.Contain("current time"));
        }

        [Test]
        public async Task HandleAsync_ShouldReturnCurrentTimeMessage()
        {
            var result = await _handler.HandleAsync("what time is it");
            Assert.That(result, Does.Contain("current time is"));
        }
    }
}
