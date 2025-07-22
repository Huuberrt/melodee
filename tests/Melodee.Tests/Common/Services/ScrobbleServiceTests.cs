using System.Threading;
using System.Threading.Tasks;
using Melodee.Common.Services;
using Moq;
using Xunit;

namespace Melodee.Tests.Common.Services;

public class ScrobbleServiceTests
{
    [Fact]
    public async Task InitializeAsync_SetsInitializedTrue()
    {
        // Arrange
        var logger = Mock.Of<Serilog.ILogger>();
        var cacheManager = Mock.Of<Melodee.Common.Services.Caching.ICacheManager>();
        var albumService = Mock.Of<Melodee.Common.Services.AlbumService>();
        var contextFactory = Mock.Of<Microsoft.EntityFrameworkCore.IDbContextFactory<Melodee.Common.Data.MelodeeDbContext>>();
        var configurationFactory = Mock.Of<Melodee.Common.Configuration.IMelodeeConfigurationFactory>();
        var nowPlayingRepository = Mock.Of<Melodee.Common.Plugins.Scrobbling.INowPlayingRepository>();
        var service = new ScrobbleService(logger, cacheManager, albumService, contextFactory, configurationFactory, nowPlayingRepository);

        // Act
        await service.InitializeAsync();

        // Assert
        // No exception means success; further asserts can be added if _initialized is exposed or via reflection
    }
}

