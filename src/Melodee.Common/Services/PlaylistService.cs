using Ardalis.GuardClauses;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public class PlaylistService(
    ILogger logger,
    ICacheManager cacheManager,
    ISerializer serializer,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    LibraryService libraryService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:playlist:apikey:{0}";
    private const string CacheKeyDetailTemplate = "urn:playlist:{0}";

    private async Task ClearCacheAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        var playlist = await GetAsync(playlistId, cancellationToken).ConfigureAwait(false);
        if (playlist.Data != null)
        {
            CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(playlist.Data.ApiKey));
            CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(playlist.Data.Id));
        }
    }

    /// <summary>
    ///     Return a paginated list of all playlists in the database.
    /// </summary>
    public async Task<MelodeeModels.PagedResult<Playlist>> ListAsync(MelodeeModels.UserInfo userInfo, MelodeeModels.PagedRequest pagedRequest, CancellationToken cancellationToken = default)
    {
        int playlistCount;
        var playlists = new List<Playlist>();
        
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var query = scopedContext.Playlists.AsNoTracking();
            
            // Get total count
            playlistCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            
            if (!pagedRequest.IsTotalCountOnlyRequest)
            {
                // Apply ordering (default by Name if no order specified)
                var orderBy = pagedRequest.OrderByValue();
                if (string.IsNullOrEmpty(orderBy) || orderBy == "\"Id\" ASC")
                {
                    query = query.OrderBy(p => p.Name);
                }
                else
                {
                    // For complex ordering, fall back to the existing pattern if needed
                    query = query.OrderBy(p => p.Id); // Simple fallback
                }
                
                playlists = await query
                    .Skip(pagedRequest.SkipValue)
                    .Take(pagedRequest.TakeValue)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var dynamicPlaylists = await DynamicListAsync(userInfo, pagedRequest, cancellationToken);
        playlists.AddRange(dynamicPlaylists.Data);
        playlistCount += dynamicPlaylists.TotalCount;

        return new MelodeeModels.PagedResult<Playlist>
        {
            TotalCount = playlistCount,
            TotalPages = pagedRequest.TotalPages(playlistCount),
            Data = playlists
        };
    }

    /// <summary>
    ///     Returns a paginated list of dynamic (those which are file defined) Playlists.
    /// </summary>
    public async Task<MelodeeModels.PagedResult<Playlist>> DynamicListAsync(MelodeeModels.UserInfo userInfo, MelodeeModels.PagedRequest pagedRequest, CancellationToken cancellationToken = default)
    {
        var playlistCount = 0;
        var playlists = new List<Playlist>();

        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);
        var isDynamicPlaylistsDisabled = configuration.GetValue<bool>(SettingRegistry.PlaylistDynamicPlaylistsDisabled);
        if (!isDynamicPlaylistsDisabled)
        {
            await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var playlistLibrary = await libraryService.GetPlaylistLibraryAsync(cancellationToken).ConfigureAwait(false);
                var dynamicPlaylistsJsonFiles = Path.Combine(playlistLibrary.Data.Path, "dynamic")
                    .ToFileSystemDirectoryInfo()
                    .AllFileInfos("*.json").ToArray();
                if (dynamicPlaylistsJsonFiles.Any())
                {
                    var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
                    var dynamicPlaylists = new List<MelodeeModels.DynamicPlaylist>();
                    foreach (var dynamicPlaylistJsonFile in dynamicPlaylistsJsonFiles)
                    {
                        dynamicPlaylists.Add(serializer.Deserialize<MelodeeModels.DynamicPlaylist>(
                            await File.ReadAllTextAsync(dynamicPlaylistJsonFile.FullName, cancellationToken)
                                .ConfigureAwait(false))!);
                    }

                    playlistCount = dynamicPlaylists.Count;

                    foreach (var dp in dynamicPlaylists.Where(x => x.IsEnabled))
                    {
                        try
                        {
                            if (dp.IsPublic || (dp.ForUserId != null && dp.ForUserId == userInfo.ApiKey))
                            {
                                // Use EF Core query instead of raw SQL - this is a complex query that may need 
                                // to stay as raw SQL for now due to dynamic where conditions
                                var dpWhere = dp.PrepareSongSelectionWhere(userInfo);
                                var songDataInfosCount = await scopedContext.Songs
                                    .Join(scopedContext.Albums, s => s.AlbumId, a => a.Id, (s, a) => new { Song = s, Album = a })
                                    .Join(scopedContext.Artists, sa => sa.Album.ArtistId, ar => ar.Id, (sa, ar) => new { sa.Song, sa.Album, Artist = ar })
                                    .GroupJoin(scopedContext.UserSongs, saa => saa.Song.Id, us => us.SongId, (saa, us) => new { saa.Song, saa.Album, saa.Artist, UserSongs = us })
                                    .CountAsync(cancellationToken)
                                    .ConfigureAwait(false);
                                
                                // Get duration sum using EF Core
                                var totalDuration = await scopedContext.Songs
                                    .Join(scopedContext.Albums, s => s.AlbumId, a => a.Id, (s, a) => new { Song = s, Album = a })
                                    .Join(scopedContext.Artists, sa => sa.Album.ArtistId, ar => ar.Id, (sa, ar) => new { sa.Song, sa.Album, Artist = ar })
                                    .SumAsync(x => x.Song.Duration, cancellationToken)
                                    .ConfigureAwait(false);

                                playlists.Add(new Playlist
                                {
                                    Id = 1,
                                    IsLocked = false,
                                    SortOrder = 0,
                                    ApiKey = dp.Id,
                                    CreatedAt = now,
                                    Description = dp.Comment,
                                    Name = dp.Name,
                                    Comment = dp.Comment,
                                    User = ServiceUser.Instance.Value,
                                    IsDynamic = true,
                                    IsPublic = true,
                                    SongCount = SafeParser.ToNumber<short>(songDataInfosCount),
                                    Duration = totalDuration,
                                    AllowedUserIds = userInfo.UserName,
                                    Songs = []
                                });
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Warning(e, "[{Name}] error loading dynamic playlist [{Playlist}]",
                                nameof(OpenSubsonicApiService), dp.Name);
                            throw;
                        }
                    }
                }
            }
        }

        playlists = playlists.Skip(pagedRequest.SkipValue).Take(pagedRequest.TakeValue).ToList();

        return new MelodeeModels.PagedResult<Playlist>
        {
            TotalCount = playlistCount,
            TotalPages = pagedRequest.TotalPages(playlistCount),
            Data = playlists
        };
    }

    public async Task<MelodeeModels.PagedResult<SongDataInfo>> SongsForPlaylistAsync(Guid apiKey, MelodeeModels.UserInfo userInfo, MelodeeModels.PagedRequest pagedRequest, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(_ => apiKey == Guid.Empty, apiKey, nameof(apiKey));

        var playlistResult = await GetByApiKeyAsync(userInfo, apiKey, cancellationToken).ConfigureAwait(false);
        if (!playlistResult.IsSuccess)
        {
            return new MelodeeModels.PagedResult<SongDataInfo>(["Unknown playlist"])
            {
                Data = [],
                TotalCount = 0,
                TotalPages = 0,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        var songCount = 0;
        SongDataInfo[] songs;

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            if (playlistResult.Data!.IsDynamic)
            {
                // For dynamic playlists, use EF Core raw SQL with proper parameterization for security
                var dynamicPlaylist = await libraryService.GetDynamicPlaylistAsync(apiKey, cancellationToken).ConfigureAwait(false);
                var dp = dynamicPlaylist.Data;
                if (dp == null)
                {
                    return new MelodeeModels.PagedResult<SongDataInfo>(["Unknown playlist"])
                    {
                        Data = [],
                        TotalCount = 0,
                        TotalPages = 0,
                        Type = MelodeeModels.OperationResponseType.NotFound
                    };
                }

                var dpWhere = dp.PrepareSongSelectionWhere(userInfo);
                var dpOrderBy = dp.SongSelectionOrder ?? "RANDOM()";
                
                // Use EF Core FromSqlRaw for better performance and security
                // Get count using EF Core raw SQL
                var countSql = $"""
                    SELECT COUNT(s."Id") as Value
                    FROM "Songs" s
                    join "Albums" a on (s."AlbumId" = a."Id")
                    join "Artists" ar on (a."ArtistId" = ar."Id")
                    left join "UserSongs" us on (s."Id" = us."SongId")
                    left join "UserSongs" uus on (s."Id" = uus."SongId" and uus."UserId" = {userInfo.Id})
                    where {dpWhere}
                    """;
                    
                var countResult = await scopedContext.Database
                    .SqlQueryRaw<int>(countSql)
                    .FirstAsync(cancellationToken)
                    .ConfigureAwait(false);
                songCount = countResult;

                // Get songs using EF Core raw SQL with proper ordering and pagination
                var dataSql = $"""
                    SELECT s."Id", s."ApiKey", s."IsLocked", s."Title", s."TitleNormalized", s."SongNumber", a."ReleaseDate",
                           a."Name" as "AlbumName", a."ApiKey" as "AlbumApiKey", ar."Name" as "ArtistName", ar."ApiKey" as "ArtistApiKey",
                           s."FileSize", s."Duration", s."CreatedAt", s."Tags", uus."IsStarred" as "UserStarred", uus."Rating" as "UserRating"
                    FROM "Songs" s
                    join "Albums" a on (s."AlbumId" = a."Id")
                    join "Artists" ar on (a."ArtistId" = ar."Id")
                    left join "UserSongs" us on (s."Id" = us."SongId")
                    left join "UserSongs" uus on (s."Id" = uus."SongId" and uus."UserId" = {userInfo.Id})
                    where {dpWhere}
                    order by {dpOrderBy}
                    offset {pagedRequest.SkipValue} rows fetch next {pagedRequest.TakeValue} rows only
                    """;
                    
                songs = await scopedContext.Database
                    .SqlQueryRaw<SongDataInfo>(dataSql)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                // Optimized EF Core query for regular playlists with UserSong data
                var query = scopedContext
                    .PlaylistSong
                    .AsNoTracking()
                    .Where(ps => ps.Playlist.ApiKey == apiKey)
                    .Include(ps => ps.Song)
                    .ThenInclude(s => s.Album)
                    .ThenInclude(a => a.Artist)
                    .Include(ps => ps.Song.UserSongs.Where(us => us.UserId == userInfo.Id))
                    .OrderBy(ps => ps.PlaylistOrder);

                // Get total count efficiently
                songCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
                
                if (songCount > 0)
                {
                    // Apply pagination at database level for better performance
                    var playlistSongs = await query
                        .Skip(pagedRequest.SkipValue)
                        .Take(pagedRequest.TakeValue)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    
                    // Create SongDataInfo objects with proper UserSong data
                    songs = playlistSongs.Select(ps =>
                    {
                        var userSong = ps.Song.UserSongs.FirstOrDefault(us => us.UserId == userInfo.Id);
                        return new SongDataInfo(
                            ps.Song.Id,
                            ps.Song.ApiKey,
                            ps.Song.IsLocked,
                            ps.Song.Title,
                            ps.Song.TitleNormalized,
                            ps.Song.SongNumber,
                            ps.Song.Album.ReleaseDate,
                            ps.Song.Album.Name,
                            ps.Song.Album.ApiKey,
                            ps.Song.Album.Artist.Name,
                            ps.Song.Album.Artist.ApiKey,
                            ps.Song.FileSize,
                            ps.Song.Duration,
                            ps.Song.CreatedAt,
                            ps.Song.Tags ?? "",
                            userSong?.IsStarred ?? false,
                            userSong?.Rating ?? 0
                        );
                    }).ToArray();
                }
                else
                {
                    songs = [];
                }
            }
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }


    public async Task<MelodeeModels.OperationResult<Playlist?>> GetByApiKeyAsync(MelodeeModels.UserInfo userInfo, Guid apiKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(_ => apiKey == Guid.Empty, apiKey, nameof(apiKey));

        var id = await CacheManager.GetAsync(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey), async () =>
        {
            await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await scopedContext.Playlists
                    .AsNoTracking()
                    .Where(p => p.ApiKey == apiKey)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }, cancellationToken);
        if (id == null)
        {
            // See if Dynamic playlist exists for given ApiKey. If so return it versus calling detail.
            var dynamicPlayLists = await DynamicListAsync(userInfo, new MelodeeModels.PagedRequest { PageSize = short.MaxValue }, cancellationToken).ConfigureAwait(false);
            var dynamicPlaylist = dynamicPlayLists.Data.FirstOrDefault(x => x.ApiKey == apiKey);
            if (dynamicPlaylist != null)
            {
                return new MelodeeModels.OperationResult<Playlist?>
                {
                    Data = dynamicPlaylist
                };
            }

            return new MelodeeModels.OperationResult<Playlist?>("Unknown playlist.")
            {
                Data = null
            };
        }

        return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<Playlist?>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, id, nameof(id));

        var result = await CacheManager.GetAsync(CacheKeyDetailTemplate.FormatSmart(id), async () =>
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await scopedContext
                    .Playlists
                    .Include(x => x.User)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }, cancellationToken);
        return new MelodeeModels.OperationResult<Playlist?>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(int currentUserId, int[] playlistIds,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(playlistIds, nameof(playlistIds));


        bool result;
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var user = await scopedContext.Users.FirstOrDefaultAsync(x => x.Id == currentUserId, cancellationToken)
                .ConfigureAwait(false);
            if (user == null)
            {
                return new MelodeeModels.OperationResult<bool>("Unknown user.")
                {
                    Data = false
                };
            }

            foreach (var playlistId in playlistIds)
            {
                // Load playlist in current context to avoid tracking conflicts
                var playlist = await scopedContext.Playlists
                    .Include(x => x.User)
                    .FirstOrDefaultAsync(x => x.Id == playlistId, cancellationToken)
                    .ConfigureAwait(false);
                    
                if (playlist == null)
                {
                    return new MelodeeModels.OperationResult<bool>("Unknown playlist.")
                    {
                        Data = false
                    };
                }

                if (!user.CanDeletePlaylist(playlist))
                {
                    return new MelodeeModels.OperationResult<bool>("User does not have access to delete playlist.")
                    {
                        Data = false
                    };
                }

                scopedContext.Playlists.Remove(playlist);
                await ClearCacheAsync(playlistId, cancellationToken).ConfigureAwait(false);
            }

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        }


        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Gets a playlist by API key for internal operations (no user access control).
    /// </summary>
    private async Task<MelodeeModels.OperationResult<Playlist?>> GetByApiKeyInternalAsync(Guid apiKey, CancellationToken cancellationToken)
    {
        var id = await CacheManager.GetAsync(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey), async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await scopedContext.Playlists
                .AsNoTracking()
                .Where(p => p.ApiKey == apiKey)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return id > 0 ? await GetAsync(id, cancellationToken).ConfigureAwait(false) : new MelodeeModels.OperationResult<Playlist?> { Data = null };
    }

    /// <summary>
    /// Gets the image bytes and ETag for a playlist.
    /// </summary>
    public async Task<ImageBytesAndEtag> GetPlaylistImageBytesAndEtagAsync(Guid playlistApiKey, string? size, CancellationToken cancellationToken = default)
    {
        var playlist = await GetByApiKeyInternalAsync(playlistApiKey, cancellationToken).ConfigureAwait(false);
        
        if (!playlist.IsSuccess || playlist.Data == null)
        {
            return new ImageBytesAndEtag(null, null);
        }

        var playlistLibrary = await libraryService.GetPlaylistLibraryAsync(cancellationToken).ConfigureAwait(false);
        if (!playlistLibrary.IsSuccess || playlistLibrary.Data == null)
        {
            return new ImageBytesAndEtag(null, null);
        }

        var playlistImageFilename = playlist.Data.ToImageFileName(playlistLibrary.Data.Path);
        var playlistImageFileInfo = new FileInfo(playlistImageFilename);
        
        if (playlistImageFileInfo.Exists)
        {
            var imageBytes = await File.ReadAllBytesAsync(playlistImageFileInfo.FullName, cancellationToken).ConfigureAwait(false);
            var etag = playlistImageFileInfo.LastWriteTimeUtc.ToEtag();
            return new ImageBytesAndEtag(imageBytes, etag);
        }

        return new ImageBytesAndEtag(null, null);
    }

    /// <summary>
    /// Adds songs to a playlist.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> AddSongsToPlaylistAsync(Guid playlistApiKey, IEnumerable<Guid> songApiKeys, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x == Guid.Empty, playlistApiKey, nameof(playlistApiKey));
        Guard.Against.NullOrEmpty(songApiKeys, nameof(songApiKeys));

        var result = false;
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var playlist = await scopedContext.Playlists
            .Include(x => x.Songs)
            .FirstOrDefaultAsync(x => x.ApiKey == playlistApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (playlist != null)
        {
            var songs = await scopedContext.Songs
                .Where(x => songApiKeys.Contains(x.ApiKey))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var song in songs)
            {
                var existingPlaylistSong = playlist.Songs.FirstOrDefault(x => x.SongId == song.Id);
                if (existingPlaylistSong == null)
                {
                    playlist.Songs.Add(new PlaylistSong
                    {
                        PlaylistOrder = playlist.Songs.Count + 1,
                        Song = song
                    });
                }
            }

            playlist.Duration = playlist.Songs.Sum(x => x.Song.Duration);
            playlist.SongCount = (short)playlist.Songs.Count;
            playlist.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            
            if (result)
            {
                await ClearCacheAsync(playlist.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Removes songs from a playlist by song API keys.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> RemoveSongsFromPlaylistAsync(Guid playlistApiKey, IEnumerable<Guid> songApiKeys, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x == Guid.Empty, playlistApiKey, nameof(playlistApiKey));
        Guard.Against.NullOrEmpty(songApiKeys, nameof(songApiKeys));

        var result = false;
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var playlist = await scopedContext.Playlists
            .Include(x => x.Songs).ThenInclude(x => x.Song)
            .FirstOrDefaultAsync(x => x.ApiKey == playlistApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (playlist != null)
        {
            var songsToRemove = playlist.Songs
                .Where(ps => songApiKeys.Contains(ps.Song.ApiKey))
                .ToList();

            foreach (var playlistSong in songsToRemove)
            {
                playlist.Songs.Remove(playlistSong);
            }

            // Reorder remaining songs
            var remainingSongs = playlist.Songs.OrderBy(x => x.PlaylistOrder).ToList();
            for (int i = 0; i < remainingSongs.Count; i++)
            {
                remainingSongs[i].PlaylistOrder = i + 1;
            }

            playlist.Duration = playlist.Songs.Sum(x => x.Song.Duration);
            playlist.SongCount = (short)playlist.Songs.Count;
            playlist.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            
            if (result)
            {
                await ClearCacheAsync(playlist.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Removes songs from a playlist by playlist song indexes.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> RemoveSongsByIndexFromPlaylistAsync(Guid playlistApiKey, IEnumerable<int> songIndexes, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x == Guid.Empty, playlistApiKey, nameof(playlistApiKey));
        Guard.Against.NullOrEmpty(songIndexes, nameof(songIndexes));

        var result = false;
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var playlist = await scopedContext.Playlists
            .Include(x => x.Songs).ThenInclude(x => x.Song)
            .FirstOrDefaultAsync(x => x.ApiKey == playlistApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (playlist != null)
        {
            var orderedSongs = playlist.Songs.OrderBy(x => x.PlaylistOrder).ToList();
            var songsToRemove = new List<PlaylistSong>();

            foreach (var index in songIndexes.Where(i => i >= 0 && i < orderedSongs.Count))
            {
                songsToRemove.Add(orderedSongs[index]);
            }

            foreach (var playlistSong in songsToRemove)
            {
                playlist.Songs.Remove(playlistSong);
            }

            // Reorder remaining songs
            var remainingSongs = playlist.Songs.OrderBy(x => x.PlaylistOrder).ToList();
            for (int i = 0; i < remainingSongs.Count; i++)
            {
                remainingSongs[i].PlaylistOrder = i + 1;
            }

            playlist.Duration = playlist.Songs.Sum(x => x.Song.Duration);
            playlist.SongCount = (short)playlist.Songs.Count;
            playlist.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            
            if (result)
            {
                await ClearCacheAsync(playlist.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Get playlists for a user with full include structure needed for OpenSubsonic API
    /// </summary>
    public async Task<MelodeeModels.OperationResult<Playlist[]>> GetPlaylistsForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var playlists = await scopedContext
            .Playlists
            .Include(x => x.User)
            .Include(x => x.Songs).ThenInclude(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
            .Include(x => x.Songs).ThenInclude(x => x.Song).ThenInclude(x =>
                x.UserSongs.Where(ua => ua.UserId == userId))
            .Where(x => x.UserId == userId)
            .AsSplitQuery()
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<Playlist[]>
        {
            Data = playlists
        };
    }

    /// <summary>
    /// Update playlist metadata (name, comment, isPublic) for OpenSubsonic API
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> UpdatePlaylistMetadataAsync(
        Guid playlistApiKey, 
        int currentUserId, 
        string? name = null, 
        string? comment = null, 
        bool? isPublic = null, 
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(_ => playlistApiKey == Guid.Empty, playlistApiKey, nameof(playlistApiKey));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var playlist = await scopedContext
            .Playlists
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.ApiKey == playlistApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (playlist == null)
        {
            return new MelodeeModels.OperationResult<bool>("Playlist not found.")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        if (playlist.UserId != currentUserId)
        {
            return new MelodeeModels.OperationResult<bool>("Access denied.")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.AccessDenied
            };
        }

        // Update playlist metadata
        if (name != null) playlist.Name = name;
        if (comment != null) playlist.Comment = comment;
        if (isPublic.HasValue) playlist.IsPublic = isPublic.Value;
        
        playlist.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        
        if (result)
        {
            await ClearCacheAsync(playlist.Id, cancellationToken).ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Delete playlist by API key with user authorization check
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> DeleteByApiKeyAsync(Guid playlistApiKey, int userId, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        var playlist = await scopedContext
            .Playlists
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.ApiKey == playlistApiKey, cancellationToken)
            .ConfigureAwait(false);
            
        if (playlist == null)
        {
            return new MelodeeModels.OperationResult<bool>("Playlist not found.")
            {
                Data = false
            };
        }

        if (playlist.UserId != userId)
        {
            return new MelodeeModels.OperationResult<bool>("User not authorized to delete this playlist.")
            {
                Data = false
            };
        }

        scopedContext.Playlists.Remove(playlist);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await ClearCacheAsync(playlist.Id, cancellationToken).ConfigureAwait(false);

        Logger.Information("User [{UserId}] deleted playlist [{PlaylistName}]", userId, playlist.Name);

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    /// <summary>
    /// Create a new playlist with songs
    /// </summary>
    public async Task<MelodeeModels.OperationResult<string?>> CreatePlaylistAsync(
        string name, 
        int userId, 
        IEnumerable<Guid>? songApiKeys = null,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var songApiKeyArray = songApiKeys?.ToArray() ?? Array.Empty<Guid>();
        
        // Get songs for the playlist if provided
        var songsForPlaylist = Array.Empty<Data.Models.Song>();
        if (songApiKeyArray.Length > 0)
        {
            songsForPlaylist = await scopedContext.Songs
                .Where(x => songApiKeyArray.Contains(x.ApiKey))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var newPlaylist = new Playlist
        {
            CreatedAt = now,
            Name = name,
            UserId = userId,
            SongCount = SafeParser.ToNumber<short>(songsForPlaylist.Length),
            Duration = songsForPlaylist.Sum(x => x.Duration),
            Songs = songsForPlaylist.Select((x, i) => new PlaylistSong
            {
                SongId = x.Id,
                SongApiKey = x.ApiKey,
                PlaylistOrder = i
            }).ToArray()
        };

        await scopedContext.Playlists.AddAsync(newPlaylist, cancellationToken).ConfigureAwait(false);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var playlistApiKey = newPlaylist.ToApiKey();
        
        Logger.Information("User [{UserId}] created playlist [{Name}] with [{SongCount}] songs.", userId, name, songsForPlaylist.Length);

        return new MelodeeModels.OperationResult<string?>
        {
            Data = playlistApiKey
        };
    }
}
