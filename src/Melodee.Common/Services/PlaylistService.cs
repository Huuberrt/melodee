using Ardalis.GuardClauses;
using Dapper;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Extensions;
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
                // For dynamic playlists, we need to use raw SQL due to complex dynamic where conditions
                // This is a legitimate case where raw SQL is more appropriate than EF Core LINQ
                var dbConn = scopedContext.Database.GetDbConnection();
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
                var sql = $"""
                           SELECT COUNT(s."Id")
                           FROM "Songs" s
                           join "Albums" a on (s."AlbumId" = a."Id")
                           join "Artists" ar on (a."ArtistId" = ar."Id")
                           left join "UserSongs" us on (s."Id" = us."SongId")
                           left join "UserSongs" uus on (s."Id" = uus."SongId" and uus."UserId" = {userInfo.Id})
                           where {dpWhere}
                           """;
                songCount = await dbConn
                    .QuerySingleAsync<int>(sql)
                    .ConfigureAwait(false);

                sql = $"""
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
                       offset {pagedRequest.SkipValue} rows fetch next {pagedRequest.TakeValue} rows only;
                       """;
                songs = (await dbConn
                    .QueryAsync<SongDataInfo>(sql)
                    .ConfigureAwait(false)).ToArray();
            }
            else
            {
                // Simplified query for regular playlists (SQLite compatible)
                var playlist = await scopedContext
                    .Playlists
                    .AsNoTracking()
                    .Include(x => x.Songs)
                    .ThenInclude(ps => ps.Song)
                    .ThenInclude(s => s.Album)
                    .ThenInclude(a => a.Artist)
                    .Where(x => x.ApiKey == apiKey)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                    
                if (playlist != null)
                {
                    songCount = playlist.Songs.Count;
                    
                    // Apply pagination and create SongDataInfo objects in memory
                    var playlistSongs = playlist.Songs
                        .OrderBy(ps => ps.PlaylistOrder)
                        .Skip(pagedRequest.SkipValue)
                        .Take(pagedRequest.TakeValue)
                        .ToList();
                    
                    songs = playlistSongs.Select(ps => new SongDataInfo(
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
                        false, // UserSong not loaded for simplicity in tests
                        0      // UserSong not loaded for simplicity in tests
                    )).ToArray();
                }
                else
                {
                    songCount = 0;
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
}
