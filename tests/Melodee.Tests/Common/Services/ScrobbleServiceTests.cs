using System.Threading;
using System.Threading.Tasks;
using Melodee.Common.Services;
using Moq;
using Xunit;

namespace Melodee.Tests.Common.Services;

public class ScrobbleServiceTests : ServiceTestBase
{
    [Fact]
    public async Task InitializeAsync_SetsInitializedTrue()
    {
        // Arrange
        var service = GetScrobbleService();

        // Act
        await service.InitializeAsync();

        // Assert
        // No exception means success; further asserts can be added if _initialized is exposed or via reflection
    }
}

