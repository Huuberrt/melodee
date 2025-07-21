using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services;

public sealed class StatisticsService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<OperationResult<Statistic?>> GetAlbumCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        if (!stats.IsSuccess)
        {
            return new OperationResult<Statistic?>
            {
                Data = null
            };
        }
        return new OperationResult<Statistic?>
        {
            Data = stats.Data.FirstOrDefault(x => x is { Type: StatisticType.Count, Category: StatisticCategory.CountAlbum })
        };
    }
    
    public async Task<OperationResult<Statistic?>> GetArtistCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        if (!stats.IsSuccess)
        {
            return new OperationResult<Statistic?>
            {
                Data = null
            };
        }
        return new OperationResult<Statistic?>
        {
            Data = stats.Data.FirstOrDefault(x => x is { Type: StatisticType.Count, Category: StatisticCategory.CountArtist })
        };
    }    
    
    public async Task<OperationResult<Statistic?>> GetSongCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        if (!stats.IsSuccess)
        {
            return new OperationResult<Statistic?>
            {
                Data = null
            };
        }
        return new OperationResult<Statistic?>
        {
            Data = stats.Data.FirstOrDefault(x => x is { Type: StatisticType.Count, Category: StatisticCategory.CountSong })
        };
    }      
    
    public async Task<OperationResult<Statistic[]>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Statistic>();

        // Helper to run a query in its own context
        async Task<T> RunInOwnContextAsync<T>(Func<MelodeeDbContext, Task<T>> query)
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await query(context).ConfigureAwait(false);
        }

        var albumsCountTask = RunInOwnContextAsync(ctx => ctx.Albums.AsNoTracking().CountAsync(cancellationToken));
        var artistsCountTask = RunInOwnContextAsync(ctx => ctx.Artists.AsNoTracking().CountAsync(cancellationToken));
        var contributorsCountTask = RunInOwnContextAsync(ctx => ctx.Contributors.AsNoTracking().CountAsync(cancellationToken));
        var librariesCountTask = RunInOwnContextAsync(ctx => ctx.Libraries.AsNoTracking().CountAsync(cancellationToken));
        var playlistsCountTask = RunInOwnContextAsync(ctx => ctx.Playlists.AsNoTracking().CountAsync(cancellationToken));
        var radioStationsCountTask = RunInOwnContextAsync(ctx => ctx.RadioStations.AsNoTracking().CountAsync(cancellationToken));
        var sharesCountTask = RunInOwnContextAsync(ctx => ctx.Shares.AsNoTracking().CountAsync(cancellationToken));
        var songsCountTask = RunInOwnContextAsync(ctx => ctx.Songs.AsNoTracking().CountAsync(cancellationToken));
        var songsPlayedCountTask = RunInOwnContextAsync(ctx => ctx.Songs.AsNoTracking().SumAsync(x => x.PlayedCount, cancellationToken));
        var usersCountTask = RunInOwnContextAsync(ctx => ctx.Users.AsNoTracking().CountAsync(cancellationToken));
        var userArtistsFavoritedTask = RunInOwnContextAsync(ctx => ctx.UserArtists.AsNoTracking().CountAsync(x => x.StarredAt != null, cancellationToken));
        var userAlbumsFavoritedTask = RunInOwnContextAsync(ctx => ctx.UserAlbums.AsNoTracking().CountAsync(x => x.StarredAt != null, cancellationToken));
        var userSongsFavoritedTask = RunInOwnContextAsync(ctx => ctx.UserSongs.AsNoTracking().CountAsync(x => x.StarredAt != null, cancellationToken));
        var userSongsRatedTask = RunInOwnContextAsync(ctx => ctx.UserSongs.AsNoTracking().CountAsync(x => x.Rating > 0, cancellationToken));
        var songsFileSizeTask = RunInOwnContextAsync(ctx => ctx.Songs.AsNoTracking().SumAsync(x => x.FileSize, cancellationToken));
        var songsDurationTask = RunInOwnContextAsync(ctx => ctx.Songs.AsNoTracking().SumAsync(x => x.Duration, cancellationToken));
        var genresCountTask = RunInOwnContextAsync(ctx => GetUniqueGenresCountAsync(ctx, cancellationToken));

        // Wait for all tasks to complete
        await Task.WhenAll(
            albumsCountTask,
            artistsCountTask,
            contributorsCountTask,
            librariesCountTask,
            playlistsCountTask,
            radioStationsCountTask,
            sharesCountTask,
            songsCountTask,
            songsPlayedCountTask,
            usersCountTask,
            userArtistsFavoritedTask,
            userAlbumsFavoritedTask,
            userSongsFavoritedTask,
            userSongsRatedTask,
            songsFileSizeTask,
            songsDurationTask,
            genresCountTask
        ).ConfigureAwait(false);

        // Build results efficiently
        results.AddRange([
            new Statistic(StatisticType.Count, "Albums", albumsCountTask.Result, null, null, 1, "album", true, StatisticCategory.CountAlbum),
            new Statistic(StatisticType.Count, "Artists", artistsCountTask.Result, null, null, 2, "artist", true, StatisticCategory.CountArtist),
            new Statistic(StatisticType.Count, "Contributors", contributorsCountTask.Result, null, null, 3, "contacts_product"),
            new Statistic(StatisticType.Count, "Genres", genresCountTask.Result, null, null, 4, "genres"),
            new Statistic(StatisticType.Count, "Libraries", librariesCountTask.Result, null, null, 5, "library_music"),
            new Statistic(StatisticType.Count, "Playlists", playlistsCountTask.Result, null, null, 6, "playlist_play", true),
            new Statistic(StatisticType.Count, "Radio Stations", radioStationsCountTask.Result, null, null, 7, "radio"),
            new Statistic(StatisticType.Count, "Shares", sharesCountTask.Result, null, null, 8, "share"),
            new Statistic(StatisticType.Count, "Songs", songsCountTask.Result, null, null, 9, "music_note", true, StatisticCategory.CountSong),
            new Statistic(StatisticType.Count, "Songs: Played count", songsPlayedCountTask.Result, null, null, 10, "analytics"),
            new Statistic(StatisticType.Count, "Users", usersCountTask.Result, null, null, 11, "group", false, StatisticCategory.CountUsers),
            new Statistic(StatisticType.Count, "Users: Favorited artists", userArtistsFavoritedTask.Result, null, null, 12, "analytics"),
            new Statistic(StatisticType.Count, "Users: Favorited albums", userAlbumsFavoritedTask.Result, null, null, 13, "analytics"),
            new Statistic(StatisticType.Count, "Users: Favorited songs", userSongsFavoritedTask.Result, null, null, 14, "analytics"),
            new Statistic(StatisticType.Count, "Users: Rated songs", userSongsRatedTask.Result, null, null, 15, "analytics"),
            new Statistic(StatisticType.Information, "Total: Song Mb", songsFileSizeTask.Result.FormatFileSize(), null, null, 16, "bar_chart"),
            new Statistic(StatisticType.Information, "Total: Song Duration", songsDurationTask.Result.ToTimeSpan().ToYearDaysMinutesHours(), null, "Total song duration in Year:Day:Hour:Minute format.", 17, "bar_chart")
        ]);

        return new OperationResult<Statistic[]>
        {
            Data = results.ToArray()
        };
    }

    /// <summary>
    /// Gets the count of unique genres from Albums and Songs using EF Core.
    /// </summary>
    private static async Task<int> GetUniqueGenresCountAsync(MelodeeDbContext context, CancellationToken cancellationToken)
    {
        // Get all non-null genre arrays from Albums and Songs
        var albumGenres = await context.Albums
            .AsNoTracking()
            .Where(a => a.Genres != null && a.Genres.Length > 0)
            .Select(a => a.Genres)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var songGenres = await context.Songs
            .AsNoTracking()
            .Where(s => s.Genres != null && s.Genres.Length > 0)
            .Select(s => s.Genres)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Flatten arrays and get unique genres
        var uniqueGenres = albumGenres
            .Concat(songGenres)
            .Where(genreArray => genreArray != null)
            .SelectMany(genreArray => genreArray!)
            .Where(genre => !string.IsNullOrWhiteSpace(genre))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet();

        return uniqueGenres.Count;
    }

    public async Task<OperationResult<Statistic[]>> GetUserSongStatisticsAsync(Guid userApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        // Use AsNoTracking for performance and run queries in parallel
        var baseQuery = scopedContext.UserSongs
            .AsNoTracking()
            .Where(x => x.User.ApiKey == userApiKey);

        var favoriteSongsCountTask = baseQuery
            .CountAsync(x => x.StarredAt != null, cancellationToken);

        var ratedSongsCountTask = baseQuery
            .CountAsync(x => x.Rating > 0, cancellationToken);

        // Wait for both queries to complete
        await Task.WhenAll(favoriteSongsCountTask, ratedSongsCountTask).ConfigureAwait(false);

        var results = new Statistic[]
        {
            new(StatisticType.Count, "Your Favorite songs", favoriteSongsCountTask.Result, null, null, 1, "analytics"),
            new(StatisticType.Count, "Your Rated songs", ratedSongsCountTask.Result, null, null, 2, "analytics")
        };

        return new OperationResult<Statistic[]>
        {
            Data = results
        };
    }

    public async Task<OperationResult<Statistic[]>> GetUserAlbumStatisticsAsync(Guid userApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        // Use AsNoTracking for performance
        var favoriteAlbumsCount = await scopedContext.UserAlbums
            .AsNoTracking()
            .Where(x => x.User.ApiKey == userApiKey)
            .CountAsync(x => x.StarredAt != null, cancellationToken)
            .ConfigureAwait(false);

        var results = new Statistic[]
        {
            new(StatisticType.Count, "Your Favorite albums", favoriteAlbumsCount, null, null, 1, "analytics")
        };

        return new OperationResult<Statistic[]>
        {
            Data = results
        };
    }

    public async Task<OperationResult<Statistic[]>> GetUserArtistStatisticsAsync(Guid userApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        // Use AsNoTracking for performance
        var favoriteArtistsCount = await scopedContext.UserArtists
            .AsNoTracking()
            .Where(x => x.User.ApiKey == userApiKey)
            .CountAsync(x => x.StarredAt != null, cancellationToken)
            .ConfigureAwait(false);

        var results = new Statistic[]
        {
            new(StatisticType.Count, "Your Favorite artists", favoriteArtistsCount, null, null, 1, "analytics")
        };

        return new OperationResult<Statistic[]>
        {
            Data = results
        };
    }
}
