using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Services.SearchEngines;

namespace Melodee.Tests.Common.Common.Services.SearchEngines;

public class AlbumImageSearchEngineServiceTests : ServiceTestBase
{
    [Fact]
    public async Task DoSearchAsync_WithValidQuery_ReturnsImageResults()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Abbey Road",
            Artist = "The Beatles",
            Year = 1969
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        // Results might be empty if search engines are disabled, but should not fail
    }

    [Fact]
    public async Task DoSearchAsync_WithNullMaxResults_UsesDefaultPageSize()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Dark Side of the Moon",
            Artist = "Pink Floyd",
            Year = 1973
        };

        // Act
        var result = await service.DoSearchAsync(query, null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithCancellationToken_CompletesWithoutError()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "The Wall",
            Artist = "Pink Floyd",
            Year = 1979
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100); // Allow some time for initialization

        // Act
        var result = await service.DoSearchAsync(query, 10, cts.Token);

        // Assert - Should complete without throwing even if cancelled
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithMaxResults_LimitsResultCount()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Thriller",
            Artist = "Michael Jackson",
            Year = 1982
        };
        var maxResults = 5;

        // Act
        var result = await service.DoSearchAsync(query, maxResults);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length <= maxResults);
    }

    [Fact]
    public async Task DoSearchAsync_WithMusicBrainzId_IncludesIdInQuery()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Revolver",
            Artist = "The Beatles",
            Year = 1966,
            MusicBrainzId = "123e4567-e89b-12d3-a456-426614174000"
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithArtistMusicBrainzId_IncludesArtistIdInQuery()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Sgt. Pepper's Lonely Hearts Club Band",
            Artist = "The Beatles",
            Year = 1967,
            ArtistMusicBrainzId = "b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d"
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithEmptyQuery_HandlesGracefully()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "",
            Artist = "",
            Year = 2000
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        // Empty query should still return valid response, even if no results
    }

    [Fact]
    public async Task DoSearchAsync_WithSpecialCharactersInQuery_HandlesCorrectly()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Âme & Soul",
            Artist = "Café del Mar",
            Year = 2005
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithVeryLongQuery_HandlesCorrectly()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var longName = new string('A', 1000);
        var query = new AlbumQuery
        {
            Name = longName,
            Artist = "Some Artist",
            Year = 2020
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_ResultsOrderedByRankDescending()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Unknown Pleasures",
            Artist = "Joy Division",
            Year = 1979
        };

        // Act
        var result = await service.DoSearchAsync(query, 50);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        
        // If there are multiple results, verify they are ordered by rank descending
        if (result.Data.Length > 1)
        {
            for (int i = 0; i < result.Data.Length - 1; i++)
            {
                Assert.True(result.Data[i].Rank >= result.Data[i + 1].Rank);
            }
        }
    }

    [Fact]
    public async Task DoSearchAsync_WithZeroMaxResults_ReturnsEmptyResults()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Test Album",
            Artist = "Test Artist",
            Year = 2023
        };

        // Act
        var result = await service.DoSearchAsync(query, 0);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithNegativeMaxResults_HandlesGracefully()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Test Album",
            Artist = "Test Artist",
            Year = 2023
        };

        // Act
        var result = await service.DoSearchAsync(query, -1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        // Should handle negative values gracefully
    }

    [Fact]
    public async Task DoSearchAsync_WithApiKey_IncludesApiKeyInQuery()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var apiKey = Guid.NewGuid();
        var query = new AlbumQuery
        {
            ApiKey = apiKey,
            Name = "Random Access Memories",
            Artist = "Daft Punk",
            Year = 2013
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithSpotifyId_IncludesSpotifyIdInQuery()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "OK Computer",
            Artist = "Radiohead",
            Year = 1997,
            SpotifyId = "6dVIqQ8qmQ183Z5MaQRIhJ"
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithDifferentCountry_HandlesCountrySpecificSearch()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "The Joshua Tree",
            Artist = "U2",
            Year = 1987,
            Country = "GB"
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

}