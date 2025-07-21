using System.Diagnostics;
using System.Web;
using Melodee.Common.Data;
using Melodee.Common.Extensions;
using Melodee.Common.Filtering;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Search;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Bus;
using Serilog;

namespace Melodee.Common.Services;

public sealed class SearchService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    UserService userService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    IMusicBrainzRepository musicBrainzRepository,
    IBus bus)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<OperationResult<SearchResult>> DoSearchAsync(Guid userApiKey,
        string? userAgent,
        string? searchTerm,
        short albumPage,
        short artistPage,
        short songPage,
        short pageSize,
        SearchInclude include,
        CancellationToken cancellationToken = default)
    {
        var totalArtists = 0;
        var totalAlbums = 0;
        var totalSongs = 0;
        var totalPlaylists = 0;
        var totalMusicBrainzArtists = 0;

        List<ArtistDataInfo> artists = new();
        List<AlbumDataInfo> albums = new();
        List<SongDataInfo> songs = new();
        List<PlaylistDataInfo> playlists = new();
        List<ArtistDataInfo> musicBrainzArtists = new();

        if (searchTerm.Nullify() == null)
        {
            return new OperationResult<SearchResult>("No Search Term Provided")
            {
                Data = new SearchResult([], 0, [], 0, [], 0, [], 0, [], 0)
            };
        }

        var user = await userService.GetByApiKeyAsync(userApiKey, cancellationToken).ConfigureAwait(false);

        var startTicks = Stopwatch.GetTimestamp();

        var searchTermNormalized = searchTerm?.ToNormalizedString() ?? searchTerm ?? string.Empty;

        if (include.HasFlag(SearchInclude.Artists))
        {
            var artistResult = await artistService.ListAsync(new PagedRequest
            {
                Page = artistPage,
                PageSize = pageSize,
                FilterBy =
                [
                    new FilterOperatorInfo(nameof(ArtistDataInfo.NameNormalized), FilterOperator.Contains, searchTermNormalized),
                    new FilterOperatorInfo(nameof(ArtistDataInfo.AlternateNames), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator)
                ]
            }, cancellationToken);
            artists = artistResult.Data.OrderBy(x => x.Name).ToList();
        }

        if (include.HasFlag(SearchInclude.Albums))
        {
            var albumResult = await albumService.ListAsync(new PagedRequest
            {
                Page = albumPage,
                PageSize = pageSize,
                FilterBy =
                [
                    new FilterOperatorInfo(nameof(AlbumDataInfo.NameNormalized), FilterOperator.Contains, searchTermNormalized),
                    new FilterOperatorInfo(nameof(AlbumDataInfo.AlternateNames), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator)
                ]
            }, null, cancellationToken);
            totalAlbums = albumResult.TotalCount;
            albums = albumResult.Data.OrderBy(x => x.Name).ToList();

            if (include.HasFlag(SearchInclude.Contributors))
            {
                var contributorAlbumsResult = await albumService.ListForContributorsAsync(new PagedRequest
                {
                    Page = albumPage,
                    PageSize = pageSize
                }, HttpUtility.UrlDecode(searchTerm) ?? Guid.NewGuid().ToString(), cancellationToken);
                if (contributorAlbumsResult.TotalCount > 0)
                {
                    albums = albums.Union(contributorAlbumsResult.Data).Distinct().ToList();
                }
            }
        }

        if (include.HasFlag(SearchInclude.Songs))
        {
            var songResult = await songService.ListAsync(new PagedRequest
            {
                Page = songPage,
                PageSize = pageSize,
                FilterBy =
                [
                    new FilterOperatorInfo(nameof(SongDataInfo.TitleNormalized), FilterOperator.Contains, searchTermNormalized)
                ]
            }, user.Data!.Id, cancellationToken);
            totalSongs = songResult.TotalCount;
            songs = songResult.Data.OrderBy(x => x.ArtistName).ThenBy(x => x.AlbumName).ToList();

            if (include.HasFlag(SearchInclude.Contributors))
            {
                var contributorSongResult = await songService.ListForContributorsAsync(new PagedRequest
                {
                    Page = songPage,
                    PageSize = pageSize
                }, HttpUtility.UrlDecode(searchTerm) ?? Guid.NewGuid().ToString(), cancellationToken);
                if (contributorSongResult.TotalCount > 0)
                {
                    songs = songs.Union(contributorSongResult.Data).Distinct().ToList();
                }
            }
        }

        if (include.HasFlag(SearchInclude.MusicBrainz))
        {
            var searchResult = await musicBrainzRepository.SearchArtist(new ArtistQuery
            {
                Name = searchTerm ?? string.Empty
            }, artistPage * pageSize, cancellationToken);
            totalMusicBrainzArtists = searchResult.TotalCount;
            musicBrainzArtists = searchResult.Data
                .Where(x => x.MusicBrainzId != null)
                .Select(x => ArtistDataInfo.BlankArtistDataInfo with
                {
                    ApiKey = x.MusicBrainzId!.Value,
                    Name = x.Name,
                    NameNormalized = x.Name.ToNormalizedString() ?? x.Name
                })
                .ToList();
        }

        var elapsedTime = Stopwatch.GetElapsedTime(startTicks);

        await bus.SendLocal(new SearchHistoryEvent
        {
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
            ByUserApiKey = userApiKey,
            ByUserAgent = userAgent,
            SearchQuery = searchTerm?.ToBase64(),
            FoundArtistsCount = artists.Count,
            FoundAlbumsCount = albums.Count,
            FoundSongsCount = songs.Count,
            FoundOtherItems = musicBrainzArtists.Count,
            SearchDurationInMs = elapsedTime.TotalMilliseconds
        }).ConfigureAwait(false);
        return new OperationResult<SearchResult>
        {
            Data = new SearchResult(artists.ToArray(),
                totalArtists,
                albums.ToArray(),
                totalAlbums,
                songs.ToArray(),
                totalSongs,
                playlists.ToArray(),
                totalPlaylists,
                musicBrainzArtists.ToArray(),
                totalMusicBrainzArtists)
        };
    }

    public async Task<OperationResult<OpenSubsonicSearchResult>> DoOpenSubsonicSearchAsync(
        Guid userApiKey,
        string? searchQuery,
        int artistOffset = 0,
        int artistCount = 20,
        int albumOffset = 0,
        int albumCount = 20,
        int songOffset = 0,
        int songCount = 20,
        CancellationToken cancellationToken = default)
    {
        var user = await userService.GetByApiKeyAsync(userApiKey, cancellationToken).ConfigureAwait(false);
        if (!user.IsSuccess || user.Data == null)
        {
            return new OperationResult<OpenSubsonicSearchResult>("User not found")
            {
                Type = OperationResponseType.NotFound,
                Data = new OpenSubsonicSearchResult([], [], [])
            };
        }

        var searchTermNormalized = searchQuery?.ToNormalizedString() ?? searchQuery ?? string.Empty;
        var artistResults = new List<ArtistDataInfo>();
        var albumResults = new List<AlbumDataInfo>();
        var songResults = new List<SongDataInfo>();

        // Handle empty query case - return all data as per OpenSubsonic spec
        if (searchQuery.Nullify() == null)
        {
            searchTermNormalized = string.Empty;
        }

        // Search artists
        if (artistCount > 0)
        {
            var artistRequest = new PagedRequest
            {
                Page = (artistOffset / artistCount) + 1,
                PageSize = (short)artistCount
            };

            if (searchQuery.Nullify() != null)
            {
                artistRequest.FilterBy = 
                [
                    new FilterOperatorInfo(nameof(ArtistDataInfo.NameNormalized), FilterOperator.Contains, searchTermNormalized),
                    new FilterOperatorInfo(nameof(ArtistDataInfo.AlternateNames), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator)
                ];
            }

            var artistResult = await artistService.ListAsync(artistRequest, cancellationToken).ConfigureAwait(false);
            artistResults = artistResult.Data.OrderBy(x => x.Name).ToList();
        }

        // Search albums
        if (albumCount > 0)
        {
            var albumRequest = new PagedRequest
            {
                Page = (albumOffset / albumCount) + 1,
                PageSize = (short)albumCount
            };

            if (searchQuery.Nullify() != null)
            {
                albumRequest.FilterBy = 
                [
                    new FilterOperatorInfo(nameof(AlbumDataInfo.NameNormalized), FilterOperator.Contains, searchTermNormalized),
                    new FilterOperatorInfo(nameof(AlbumDataInfo.AlternateNames), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator)
                ];
            }

            var albumResult = await albumService.ListAsync(albumRequest, null, cancellationToken).ConfigureAwait(false);
            albumResults = albumResult.Data.OrderBy(x => x.Name).ToList();
        }

        // Search songs
        if (songCount > 0)
        {
            var songRequest = new PagedRequest
            {
                Page = (songOffset / songCount) + 1,
                PageSize = (short)songCount
            };

            if (searchQuery.Nullify() != null)
            {
                songRequest.FilterBy = 
                [
                    new FilterOperatorInfo(nameof(SongDataInfo.TitleNormalized), FilterOperator.Contains, searchTermNormalized)
                ];
            }

            var songResult = await songService.ListAsync(songRequest, user.Data.Id, cancellationToken).ConfigureAwait(false);
            songResults = songResult.Data.OrderBy(x => x.ArtistName).ThenBy(x => x.AlbumName).ToList();
        }

        return new OperationResult<OpenSubsonicSearchResult>
        {
            Data = new OpenSubsonicSearchResult(artistResults.ToArray(), albumResults.ToArray(), songResults.ToArray())
        };
    }
}

public record OpenSubsonicSearchResult(
    ArtistDataInfo[] Artists,
    AlbumDataInfo[] Albums,
    SongDataInfo[] Songs);
