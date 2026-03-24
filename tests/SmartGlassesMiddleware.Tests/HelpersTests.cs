using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Configuration;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests
{
    [TestFixture]
    public class ConfigurationSectionTests
    {
        [Test]
        public void Indexer_Set_SetsValueInUnderlyingConfiguration()
        {
            // Arrange
            var mockConfig = new Mock<IConfiguration>();
            var path = "TestPath";
            var key = "TestKey";
            var newValue = "NewValue";
            var expectedConfigKey = $"{path}:{key}";

            mockConfig.SetupSet(c => c[expectedConfigKey] = newValue).Verifiable();

            var section = new Services.ConfigurationSection(mockConfig.Object, path);

            // Act
            section[key] = newValue;

            // Assert
            mockConfig.Verify();
        }

        [Test]
        public void Indexer_Get_ReturnsValueFromUnderlyingConfiguration()
        {
            // Arrange
            var mockConfig = new Mock<IConfiguration>();
            var path = "TestPath";
            var key = "TestKey";
            var expectedValue = "TestValue";

            mockConfig.Setup(c => c[$"{path}:{key}"]).Returns(expectedValue);

            var section = new Services.ConfigurationSection(mockConfig.Object, path);

            // Act
            var result = section[key];

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
        }
    }
}
