using NUnit.Framework;
using Moq;
using SmartGlassesMiddleware.Services;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class OpenAdminPanelTaskHandlerTests
    {
        private AdminPanelTaskHandler _handler;

        [SetUp]
        public void Setup()
        {
            _handler = new AdminPanelTaskHandler();
        }

        [Test]
        public void Name_ShouldBeOpenAdminPanel()
        {
            Assert.That(_handler.Name, Is.EqualTo("open-admin-panel"));
        }

        [Test]
        public void Keywords_ShouldContainExpectedKeywords()
        {
            Assert.That(_handler.Keywords, Does.Contain("open admin panel"));
            Assert.That(_handler.Keywords, Does.Contain("go to admin"));
        }

        [Test]
        public async Task HandleAsync_ShouldReturnOpenAdminCommand()
        {
            var result = await _handler.HandleAsync("open the admin panel");
            Assert.That(result, Is.EqualTo("__ACTION:OPEN_ADMIN_PANEL__"));
        }
    }
}
