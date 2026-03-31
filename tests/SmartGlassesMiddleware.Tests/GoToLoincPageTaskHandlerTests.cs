using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class GoToLoincPageTaskHandlerTests
    {
        private GoToLoincPageTaskHandler _handler = null!;

        [SetUp]
        public void Setup()
        {
            _handler = new GoToLoincPageTaskHandler();
        }

        [Test]
        public void Name_ShouldBeGoToLoinc()
        {
            Assert.That(_handler.Name, Is.EqualTo("go-to-loinc"));
        }

        [Test]
        public void Keywords_ShouldContainLoinc()
        {
            Assert.That(_handler.Keywords, Does.Contain("loinc"));
        }

        [Test]
        public void Keywords_ShouldNotBeEmpty()
        {
            Assert.That(_handler.Keywords, Is.Not.Empty);
        }

        [Test]
        public async Task HandleAsync_GoToLoincCode8480_6_ReturnsCorrectToken()
        {
            var result = await _handler.HandleAsync("go to LOINC code 8480-6");
            Assert.That(result, Is.EqualTo("__ACTION:GO_TO_LOINC:8480-6__"));
        }

        [Test]
        public async Task HandleAsync_FindLoinc718_7_ReturnsCorrectToken()
        {
            var result = await _handler.HandleAsync("find LOINC 718-7");
            Assert.That(result, Is.EqualTo("__ACTION:GO_TO_LOINC:718-7__"));
        }

        [Test]
        public async Task HandleAsync_SpokenFormDash_ReturnsCorrectToken()
        {
            var result = await _handler.HandleAsync("show me LOINC 8480 dash 6");
            Assert.That(result, Is.EqualTo("__ACTION:GO_TO_LOINC:8480-6__"));
        }

        [Test]
        public async Task HandleAsync_EmptyInput_ReturnsErrorMessage()
        {
            var result = await _handler.HandleAsync(string.Empty);
            Assert.That(result, Does.Contain("Please say a LOINC code"));
        }

        [Test]
        public async Task HandleAsync_NoValidCode_ReturnsErrorMessage()
        {
            var result = await _handler.HandleAsync("go to loinc abc");
            Assert.That(result, Does.Contain("Please say a LOINC code"));
        }
    }
}
