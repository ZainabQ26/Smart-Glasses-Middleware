using NUnit.Framework;
using SmartGlassesMiddleware.Services;
using System.Net.Http;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class HttpClientFactoryTests
    {
        private HttpClientFactory _factory;

        [SetUp]
        public void Setup()
        {
            _factory = new HttpClientFactory();
        }

        [Test]
        public void CreateClient_ShouldReturnNonNullHttpClient()
        {
            // Act
            var client = _factory.CreateClient("testName");

            // Assert
            Assert.That(client, Is.Not.Null);
            Assert.That(client, Is.InstanceOf<HttpClient>());
        }

        [Test]
        public void CreateClient_ShouldReturnNewInstanceEachTime()
        {
            // Act
            var client1 = _factory.CreateClient("test1");
            var client2 = _factory.CreateClient("test2");

            // Assert
            Assert.That(client1, Is.Not.SameAs(client2));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void CreateClient_WithVariousNames_ShouldStillReturnClient(string? name)
        {
            // Act
            var client = _factory.CreateClient(name!);

            // Assert
            Assert.That(client, Is.Not.Null);
            Assert.That(client, Is.InstanceOf<HttpClient>());
        }
    }
}
