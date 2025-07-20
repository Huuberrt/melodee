using System.Diagnostics;
using Melodee.Common.Data;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;


namespace Melodee.Common.Plugins.SearchEngine;

/// <summary>
///     Searches for Artist using the Melodee database
/// </summary>
public class MelodeeArtistSearchEnginePlugin(IDbContextFactory<MelodeeDbContext> contextFactory)
    : IArtistSearchEnginePlugin, IArtistTopSongsSearchEnginePlugin
{
    public bool StopProcessing { get; } = false;

    public string Id => "018A798D-7B68-4F3E-80CD-1BAF03998C0B";

    public string DisplayName => "Melodee Database";

    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; } = 0;

    public async Task<PagedResult<ArtistSearchResult>> DoArtistSearchAsync(
        ArtistQuery query, 
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await using (var scopedContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var startTicks = Stopwatch.GetTimestamp();
            var data = new List<ArtistSearchResult>();

            if (query.MusicBrainzId.Nullify() != null)
            {
                // Optimized: Single query with Include to get artist and albums together
                var artistWithAlbums = await scopedContext.Artists
                    .AsNoTracking()
                    .Where(x => x.MusicBrainzId == query.MusicBrainzIdValue)
                    .Select(x => new
                    {
                        x.Id, x.Name, x.ApiKey, x.MusicBrainzId, x.SortName, x.RealName, x.AlbumCount, x.AlternateNames,
                        Albums = x.Albums.Select(a => new
                        {
                            a.ApiKey, a.AlbumType, a.ReleaseDate, a.MusicBrainzId, a.Name, a.NameNormalized, a.SortName
                        })
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (artistWithAlbums != null)
                {
                    data.Add(new ArtistSearchResult
                    {
                        Id = artistWithAlbums.Id,
                        AlternateNames = artistWithAlbums.AlternateNames?.ToTags()?.ToArray() ?? [],
                        FromPlugin = DisplayName,
                        UniqueId = SafeParser.Hash(artistWithAlbums.MusicBrainzId.ToString()),
                        Rank = short.MaxValue,
                        Name = artistWithAlbums.Name,
                        SortName = artistWithAlbums.SortName,
                        MusicBrainzId = artistWithAlbums.MusicBrainzId,
                        AlbumCount = artistWithAlbums.AlbumCount,
                        Releases = artistWithAlbums.Albums
                            .OrderBy(x => x.ReleaseDate)
                            .ThenBy(x => x.SortName)
                            .Select(x => new AlbumSearchResult
                            {
                                ApiKey = x.ApiKey,
                                AlbumType = SafeParser.ToEnum<AlbumType>(x.AlbumType),
                                ReleaseDate = x.ReleaseDate.ToString(),
                                UniqueId = SafeParser.Hash(x.MusicBrainzId.ToString()),
                                Name = x.Name,
                                NameNormalized = x.NameNormalized,
                                SortName = x.SortName ?? x.Name,
                                MusicBrainzId = x.MusicBrainzId
                            }).ToArray()
                    });
                }
            }

            // Return first artist that matches and has album that matches any of the album names - the more matches ranks higher
            if (data.Count == 0 && query.AlbumKeyValues?.Length > 0)
            {
                var artistsByNamedNormalizedWithMatchingAlbums = await scopedContext.Artists
                    .AsNoTracking()
                    .Where(x => x.NameNormalized == query.NameNormalized ||
                                (x.AlternateNames != null && x.AlternateNames.Contains(query.NameNormalized)))
                    .OrderBy(x => x.SortName)
                    .Take(maxResults)
                    .Select(x => new
                    {
                        x.Id, x.Name, x.ApiKey, x.MusicBrainzId, x.SortName, x.RealName, x.AlbumCount, x.AlternateNames,
                        x.NameNormalized,
                        Albums = x.Albums.Select(a => new
                        {
                            a.AlbumType, a.AlternateNames, a.ReleaseDate, a.MusicBrainzId, a.NameNormalized, a.Name,
                            a.SortName, a.ApiKey
                        })
                    })
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (artistsByNamedNormalizedWithMatchingAlbums.Length > 0)
                {
                    var matchingWithAlbums = artistsByNamedNormalizedWithMatchingAlbums
                        .Where(x => query.AlbumNamesNormalized != null &&
                                    (x.Albums.Any(a => query.AlbumNamesNormalized.Contains(a.NameNormalized)) ||
                                     x.Albums.Any(a =>
                                         a.AlternateNames != null &&
                                         a.AlternateNames.ContainsAny(query.AlbumNamesNormalized))))
                        .ToArray();

                    if (matchingWithAlbums.Length != 0)
                    {
                        data.AddRange(matchingWithAlbums.Select(x => new ArtistSearchResult
                        {
                            Id = x.Id,
                            AlternateNames = x.AlternateNames?.ToTags()?.ToArray() ?? [],
                            FromPlugin = DisplayName,
                            UniqueId = SafeParser.Hash(x.MusicBrainzId.ToString()),
                            Rank = matchingWithAlbums.Length + 5,
                            Name = x.Name,
                            SortName = x.SortName,
                            MusicBrainzId = x.MusicBrainzId,
                            AlbumCount = x.AlbumCount,
                            Releases = x.Albums
                                .Where(a => query.AlbumNamesNormalized != null &&
                                            (query.AlbumNamesNormalized.Contains(a.NameNormalized) ||
                                             (a.AlternateNames != null &&
                                              a.AlternateNames.ContainsAny(query.AlbumNamesNormalized))))
                                .OrderBy(a => a.ReleaseDate).ThenBy(a => a.SortName).Select(a => new AlbumSearchResult
                                {
                                    ApiKey = a.ApiKey,
                                    AlbumType = SafeParser.ToEnum<AlbumType>(a.AlbumType),
                                    ReleaseDate = a.ReleaseDate.ToString(),
                                    UniqueId = SafeParser.Hash(a.MusicBrainzId.ToString()),
                                    Name = a.Name,
                                    NameNormalized = a.NameNormalized,
                                    SortName = a.SortName ?? x.Name,
                                    MusicBrainzId = a.MusicBrainzId
                                }).ToArray()
                        }));
                    }
                }
            }

            if (data.Count == 0)
            {
                var artistsByNamedNormalized = await scopedContext.Artists
                    .AsNoTracking()
                    .Where(x => x.NameNormalized == query.NameNormalized ||
                                (x.AlternateNames != null && x.AlternateNames.Contains(query.NameNormalized)))
                    .OrderBy(x => x.SortName)
                    .Take(maxResults)
                    .Select(x => new
                    {
                        x.Id, x.Name, x.ApiKey, x.MusicBrainzId, x.SortName, x.RealName, x.AlbumCount, x.AlternateNames,
                        x.NameNormalized,
                        Albums = x.Albums.Select(a => new
                        {
                            a.AlbumType, a.AlternateNames, a.ReleaseDate, a.MusicBrainzId, a.NameNormalized, a.Name,
                            a.SortName, a.ApiKey
                        })
                    })
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (artistsByNamedNormalized.Length > 0)
                {
                    data.AddRange(artistsByNamedNormalized.Select(x => new ArtistSearchResult
                    {
                        Id = x.Id,
                        AlternateNames = x.AlternateNames?.ToTags()?.ToArray() ?? [],
                        FromPlugin = DisplayName,
                        UniqueId = SafeParser.Hash(x.MusicBrainzId.ToString()),
                        Rank = 1,
                        Name = x.Name,
                        SortName = x.SortName,
                        MusicBrainzId = x.MusicBrainzId,
                        AlbumCount = x.AlbumCount,
                        Releases = x.Albums.OrderBy(a => a.ReleaseDate).ThenBy(a => a.SortName).Select(a =>
                            new AlbumSearchResult
                            {
                                ApiKey = a.ApiKey,
                                AlbumType = SafeParser.ToEnum<AlbumType>(a.AlbumType),
                                ReleaseDate = a.ReleaseDate.ToString(),
                                UniqueId = SafeParser.Hash(a.MusicBrainzId.ToString()),
                                Name = a.Name,
                                NameNormalized = a.NameNormalized,
                                SortName = a.SortName ?? x.Name,
                                MusicBrainzId = a.MusicBrainzId
                            }).ToArray()
                    }));
                }
            }

            return new PagedResult<ArtistSearchResult>
            {
                OperationTime = Stopwatch.GetElapsedTime(startTicks).Microseconds,
                TotalCount = 0,
                TotalPages = 0,
                Data = data
            };
        }
    }

    public async Task<PagedResult<SongSearchResult>> DoArtistTopSongsSearchAsync(
        int forArtist, 
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await using (var scopedContext =
                     await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var startTicks = Stopwatch.GetTimestamp();
            SongSearchResult[] data = [];

            // Check if artist exists first
            var artistExists = await scopedContext.Artists
                .AsNoTracking()
                .AnyAsync(x => x.Id == forArtist, cancellationToken)
                .ConfigureAwait(false);
                
            if (artistExists)
            {
                // Use EF Core to get top songs with proper ordering and ranking
                var songs = await scopedContext.Songs
                    .AsNoTracking()
                    .Where(s => s.Album.ArtistId == forArtist)
                    .OrderByDescending(s => s.PlayedCount)
                    .ThenByDescending(s => s.LastPlayedAt)
                    .ThenBy(s => s.SortOrder)
                    .ThenBy(s => s.TitleSort)
                    .ThenBy(s => s.Album.SortOrder)
                    .Take(maxResults)
                    .Select(s => new
                    {
                        s.Id,
                        s.ApiKey,
                        s.Title,
                        s.MusicBrainzId,
                        s.TitleSort,
                        s.PlayedCount
                    })
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Convert to search results with ranking
                data = songs.Select((song, index) => new SongSearchResult
                {
                    Id = song.Id,
                    ApiKey = song.ApiKey,
                    Name = song.Title,
                    MusicBrainzId = song.MusicBrainzId,
                    SortName = song.TitleSort ?? song.Title,
                    SortOrder = index + 1, // 1-based ranking
                    PlayCount = song.PlayedCount
                }).ToArray();
            }

            return new PagedResult<SongSearchResult>
            {
                OperationTime = Stopwatch.GetElapsedTime(startTicks).Microseconds,
                TotalCount = data.Length,
                TotalPages = 1,
                Data = data
            };
        }
    }
}
