using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Melodee.Common.Services;
using Moq;
using Xunit;

namespace Melodee.Tests.Common.Services;

public class ImageConversionServiceTests
{
    [Fact]
    public async Task ConvertImageAsync_ReturnsOperationResult()
    {
        // Arrange
        var logger = Mock.Of<Serilog.ILogger>();
        var cacheManager = Mock.Of<Melodee.Common.Services.Caching.ICacheManager>();
        var contextFactory = Mock.Of<Microsoft.EntityFrameworkCore.IDbContextFactory<Melodee.Common.Data.MelodeeDbContext>>();
        var configurationFactory = Mock.Of<Melodee.Common.Configuration.IMelodeeConfigurationFactory>();
        var service = new ImageConversionService(logger, cacheManager, contextFactory, configurationFactory);
        var fileInfo = new FileInfo("test.jpg");
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await service.ConvertImageAsync(fileInfo, cancellationToken);

        // Assert
        Assert.NotNull(result);
    }
}

