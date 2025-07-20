using Melodee.Common.Enums;
using Melodee.Common.Services;

namespace Melodee.Tests.Common.Services;

public class StatisticsServiceTests : ServiceTestBase
{
    private StatisticsService GetStatisticsService()
    {
        return new StatisticsService(Logger, CacheManager, MockFactory());
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnAllStatistics_WhenDatabaseIsEmpty()
    {
        var service = GetStatisticsService();
        
        var result = await service.GetStatisticsAsync();
        
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(17, result.Data.Length);
        
        var albumsStatistic = result.Data.First(x => x.Title == "Albums");
        Assert.Equal(StatisticType.Count, albumsStatistic.Type);
        Assert.Equal(0, albumsStatistic.Data);
        Assert.Equal("album", albumsStatistic.Icon);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldHandleNullGenres_WhenNoGenresExist()
    {
        var service = GetStatisticsService();
        
        var result = await service.GetStatisticsAsync();
        
        Assert.True(result.IsSuccess);
        var genresStatistic = result.Data!.First(x => x.Title == "Genres");
        Assert.Equal(0, genresStatistic.Data);
    }

    [Fact]
    public async Task GetUserSongStatisticsAsync_ShouldReturnZero_WhenUserHasNoData()
    {
        var service = GetStatisticsService();
        var result = await service.GetUserSongStatisticsAsync(Guid.NewGuid());
        
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data!.Length);
        Assert.Equal(0, result.Data!.First(x => x.Title == "Your Favorite songs").Data);
        Assert.Equal(0, result.Data!.First(x => x.Title == "Your Rated songs").Data);
    }

    [Fact]
    public async Task GetUserAlbumStatisticsAsync_ShouldReturnZero_WhenUserHasNoData()
    {
        var service = GetStatisticsService();
        var result = await service.GetUserAlbumStatisticsAsync(Guid.NewGuid());
        
        Assert.True(result.IsSuccess);
        Assert.Single(result.Data!);
        Assert.Equal(0, result.Data!.First(x => x.Title == "Your Favorite albums").Data);
    }

    [Fact]
    public async Task GetUserArtistStatisticsAsync_ShouldReturnZero_WhenUserHasNoData()
    {
        var service = GetStatisticsService();
        var result = await service.GetUserArtistStatisticsAsync(Guid.NewGuid());
        
        Assert.True(result.IsSuccess);
        Assert.Single(result.Data!);
        Assert.Equal(0, result.Data!.First(x => x.Title == "Your Favorite artists").Data);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldHandleCancellationToken()
    {
        var service = GetStatisticsService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetStatisticsAsync(cts.Token));
    }

    [Fact]
    public async Task GetUserSongStatisticsAsync_ShouldHandleCancellationToken()
    {
        var service = GetStatisticsService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetUserSongStatisticsAsync(Guid.NewGuid(), cts.Token));
    }

    [Fact]
    public async Task GetUserAlbumStatisticsAsync_ShouldHandleCancellationToken()
    {
        var service = GetStatisticsService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetUserAlbumStatisticsAsync(Guid.NewGuid(), cts.Token));
    }

    [Fact]
    public async Task GetUserArtistStatisticsAsync_ShouldHandleCancellationToken()
    {
        var service = GetStatisticsService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetUserArtistStatisticsAsync(Guid.NewGuid(), cts.Token));
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldRetainSortOrder_OfStatistics()
    {
        var service = GetStatisticsService();
        
        var result = await service.GetStatisticsAsync();
        
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        
        var sortOrders = result.Data.Select(x => x.SortOrder).ToArray();
        var expectedOrder = Enumerable.Range(1, 17).Select(x => (short?)x).ToArray();
        
        Assert.Equal(expectedOrder, sortOrders);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldIncludeCorrectIcons_ForAllStatistics()
    {
        var service = GetStatisticsService();
        
        var result = await service.GetStatisticsAsync();
        
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        
        var albumsIcon = result.Data!.First(x => x.Title == "Albums").Icon;
        var artistsIcon = result.Data!.First(x => x.Title == "Artists").Icon;
        var songsIcon = result.Data!.First(x => x.Title == "Songs").Icon;
        
        Assert.Equal("album", albumsIcon);
        Assert.Equal("artist", artistsIcon);
        Assert.Equal("music_note", songsIcon);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnCorrectStatisticTypes()
    {
        var service = GetStatisticsService();
        
        var result = await service.GetStatisticsAsync();
        
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        
        var countStats = result.Data.Where(x => x.Type == StatisticType.Count).ToArray();
        var infoStats = result.Data.Where(x => x.Type == StatisticType.Information).ToArray();
        
        Assert.Equal(15, countStats.Length);
        Assert.Equal(2, infoStats.Length);
        
        Assert.Contains(countStats, x => x.Title == "Albums");
        Assert.Contains(countStats, x => x.Title == "Artists");
        Assert.Contains(countStats, x => x.Title == "Songs");
        Assert.Contains(infoStats, x => x.Title == "Total: Song Mb");
        Assert.Contains(infoStats, x => x.Title == "Total: Song Duration");
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldIncludeApiResultFlag_ForRelevantStatistics()
    {
        var service = GetStatisticsService();
        
        var result = await service.GetStatisticsAsync();
        
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        
        var albumsStat = result.Data.First(x => x.Title == "Albums");
        var artistsStat = result.Data.First(x => x.Title == "Artists");
        var songsStat = result.Data.First(x => x.Title == "Songs");
        var playlistsStat = result.Data.First(x => x.Title == "Playlists");
        
        Assert.True(albumsStat.IncludeInApiResult);
        Assert.True(artistsStat.IncludeInApiResult);
        Assert.True(songsStat.IncludeInApiResult);
        Assert.True(playlistsStat.IncludeInApiResult);
        
        var contributorsStat = result.Data.First(x => x.Title == "Contributors");
        Assert.Null(contributorsStat.IncludeInApiResult);
    }

    [Fact]
    public async Task GetUserSongStatisticsAsync_ShouldReturnCorrectStructure()
    {
        var service = GetStatisticsService();
        var userApiKey = Guid.NewGuid();
        
        var result = await service.GetUserSongStatisticsAsync(userApiKey);
        
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Length);
        
        var favoriteStat = result.Data.First(x => x.Title == "Your Favorite songs");
        var ratedStat = result.Data.First(x => x.Title == "Your Rated songs");
        
        Assert.Equal(StatisticType.Count, favoriteStat.Type);
        Assert.Equal(StatisticType.Count, ratedStat.Type);
        Assert.Equal((short?)1, favoriteStat.SortOrder);
        Assert.Equal((short?)2, ratedStat.SortOrder);
        Assert.Equal("analytics", favoriteStat.Icon);
        Assert.Equal("analytics", ratedStat.Icon);
    }

    [Fact]
    public async Task GetUserAlbumStatisticsAsync_ShouldReturnCorrectStructure()
    {
        var service = GetStatisticsService();
        var userApiKey = Guid.NewGuid();
        
        var result = await service.GetUserAlbumStatisticsAsync(userApiKey);
        
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        
        var favoriteStat = result.Data.First();
        Assert.Equal("Your Favorite albums", favoriteStat.Title);
        Assert.Equal(StatisticType.Count, favoriteStat.Type);
        Assert.Equal((short?)1, favoriteStat.SortOrder);
        Assert.Equal("analytics", favoriteStat.Icon);
    }

    [Fact]
    public async Task GetUserArtistStatisticsAsync_ShouldReturnCorrectStructure()
    {
        var service = GetStatisticsService();
        var userApiKey = Guid.NewGuid();
        
        var result = await service.GetUserArtistStatisticsAsync(userApiKey);
        
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        
        var favoriteStat = result.Data.First();
        Assert.Equal("Your Favorite artists", favoriteStat.Title);
        Assert.Equal(StatisticType.Count, favoriteStat.Type);
        Assert.Equal((short?)1, favoriteStat.SortOrder);
        Assert.Equal("analytics", favoriteStat.Icon);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnValidOperationResult()
    {
        var service = GetStatisticsService();
        
        var result = await service.GetStatisticsAsync();
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Messages);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Messages ?? []);
    }

    [Fact]
    public async Task GetUserSongStatisticsAsync_ShouldReturnValidOperationResult()
    {
        var service = GetStatisticsService();
        var userApiKey = Guid.NewGuid();
        
        var result = await service.GetUserSongStatisticsAsync(userApiKey);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Messages);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldHaveCorrectStatisticTitles()
    {
        var service = GetStatisticsService();
        
        var result = await service.GetStatisticsAsync();
        
        Assert.True(result.IsSuccess);
        var titles = result.Data!.Select(x => x.Title).ToArray();
        
        var expectedTitles = new[]
        {
            "Albums", "Artists", "Contributors", "Genres", "Libraries", 
            "Playlists", "Radio Stations", "Shares", "Songs", "Songs: Played count",
            "Users", "Users: Favorited artists", "Users: Favorited albums", 
            "Users: Favorited songs", "Users: Rated songs", 
            "Total: Song Mb", "Total: Song Duration"
        };
        
        foreach (var expectedTitle in expectedTitles)
        {
            Assert.Contains(expectedTitle, titles);
        }
    }
}