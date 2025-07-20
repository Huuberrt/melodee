using System.Diagnostics;
using System.Globalization;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using Album = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Album;
using Artist = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Artist;
using Directory = System.IO.Directory;


namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
///     SQLite backend database created from MusicBrainz data dumps using Entity Framework Core.
///     <remarks>
///         See https://metabrainz.org/datasets/postgres-dumps#musicbrainz
///     </remarks>
/// </summary>
public class SQLiteMusicBrainzRepository(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MusicBrainzDbContext> dbContextFactory) : MusicBrainzRepositoryBase(logger, configurationFactory)
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

    public override async Task<Album?> GetAlbumByMusicBrainzId(Guid musicBrainzId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var musicBrainzIdRaw = musicBrainzId.ToString();
        
        return await context.Albums
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.MusicBrainzIdRaw == musicBrainzIdRaw, cancellationToken);
    }

    public override async Task<PagedResult<ArtistSearchResult>> SearchArtist(ArtistQuery query, int maxResults,
        CancellationToken cancellationToken = default)
    {
        var startTicks = Stopwatch.GetTimestamp();
        var data = new List<ArtistSearchResult>();

        var maxLuceneResults = 10;
        var totalCount = 0;

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var storagePath = configuration.GetValue<string>(SettingRegistry.SearchEngineMusicBrainzStoragePath) ??
                          throw new InvalidOperationException(
                              $"Invalid setting for [{SettingRegistry.SearchEngineMusicBrainzStoragePath}]");

        var musicBrainzIdsFromLucene = new List<string>();

        // For tests and when no Lucene index exists, use direct database search
        var shouldUseDirectSearch = query.MusicBrainzIdValue == null;
        
        if (query.MusicBrainzIdValue != null)
        {
            musicBrainzIdsFromLucene.Add(query.MusicBrainzIdValue.Value.ToString());
            shouldUseDirectSearch = false;
        }
        else
        {
            var lucenePath = Path.Combine(storagePath, "lucene");
            if (Directory.Exists(lucenePath) && Directory.GetFiles(lucenePath).Length > 0)
            {
                using (var dir = FSDirectory.Open(lucenePath))
                {
                    var analyzer = new StandardAnalyzer(AppLuceneVersion);
                    var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);
                    using (var writer = new IndexWriter(dir, indexConfig))
                    {
                        using (var reader = writer.GetReader(true))
                        {
                            var searcher = new IndexSearcher(reader);
                            BooleanQuery categoryQuery = [];
                            var catQuery1 = new TermQuery(new Term(nameof(Artist.NameNormalized), query.NameNormalized));
                            var catQuery2 =
                                new TermQuery(new Term(nameof(Artist.NameNormalized), query.NameNormalizedReversed));
                            var catQuery3 = new TermQuery(new Term(nameof(Artist.AlternateNames), query.NameNormalized));
                            categoryQuery.Add(new BooleanClause(catQuery1, Occur.SHOULD));
                            categoryQuery.Add(new BooleanClause(catQuery2, Occur.SHOULD));
                            categoryQuery.Add(new BooleanClause(catQuery3, Occur.SHOULD));
                            ScoreDoc[] hits = searcher.Search(categoryQuery, maxLuceneResults).ScoreDocs;
                            musicBrainzIdsFromLucene.AddRange(hits.Select(t => searcher.Doc(t.Doc))
                                .Select(hitDoc => hitDoc.Get(nameof(Artist.MusicBrainzIdRaw))));
                            shouldUseDirectSearch = musicBrainzIdsFromLucene.Count == 0;
                        }
                    }
                }
            }
        }

        try
        {
            using (Operation.At(LogEventLevel.Debug).Time("[{Name}] SearchArtist [{ArtistQuery}]",
                       nameof(SQLiteMusicBrainzRepository), query))
            {
                await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                
                // Use direct search when Lucene is not available or found no results
                if (shouldUseDirectSearch && !string.IsNullOrEmpty(query.NameNormalized))
                {
                    var directArtists = await context.Artists
                        .AsNoTracking()
                        .Where(a => a.NameNormalized == query.NameNormalized ||
                                   a.NameNormalized.Contains(query.NameNormalized) ||
                                   (a.AlternateNames != null && a.AlternateNames.Contains(query.NameNormalized)))
                        .OrderBy(a => a.SortName)
                        .ToArrayAsync(cancellationToken);
                    
                    foreach (var artist in directArtists)
                    {
                        var rank = artist.NameNormalized == query.NameNormalized ? 10 : 1;
                        if (artist.AlternateNamesValues.Contains(query.NameNormalized))
                        {
                            rank++;
                        }

                        if (artist.AlternateNamesValues.Contains(query.Name.CleanString().ToNormalizedString()))
                        {
                            rank++;
                        }

                        if (artist.AlternateNamesValues.Contains(query.NameNormalizedReversed))
                        {
                            rank++;
                        }

                        // Get artist albums like in the normal path
                        var artistAlbums = await context.Albums
                            .AsNoTracking()
                            .Where(a => a.MusicBrainzArtistId == artist.MusicBrainzArtistId && 
                                       a.ReleaseDate > DateTime.MinValue) 
                            .ToArrayAsync(cancellationToken);

                        if (artistAlbums.Length > 0)
                        {
                            // Group by release group and take the earliest release date from each group
                            var newArtistAlbums = artistAlbums
                                .GroupBy(x => x.ReleaseGroupMusicBrainzIdRaw)
                                .Select(group => group.OrderBy(x => x.ReleaseDate).First())
                                .ToArray();

                            artistAlbums = newArtistAlbums;
                        }

                        rank += artistAlbums.Length;

                        if (query.AlbumKeyValues != null)
                        {
                            rank += artistAlbums.Length;
                            foreach (var albumKeyValues in query.AlbumKeyValues)
                            {
                                rank += artistAlbums.Count(x =>
                                    x.ReleaseDate.Year.ToString() == albumKeyValues.Key &&
                                    x.NameNormalized == albumKeyValues.Value.ToNormalizedString());
                            }
                        }

                        data.Add(new ArtistSearchResult
                        {
                            AlternateNames = artist.AlternateNames?.ToTags()?.ToArray() ?? [],
                            FromPlugin =
                                $"{nameof(MusicBrainzArtistSearchEnginePlugin)}:{nameof(SQLiteMusicBrainzRepository)}",
                            UniqueId = SafeParser.Hash(artist.MusicBrainzId.ToString()),
                            Rank = rank,
                            Name = artist.Name,
                            SortName = artist.SortName,
                            MusicBrainzId = artist.MusicBrainzId,
                            AlbumCount = artistAlbums.Count(x => x.ReleaseDate > DateTime.MinValue),
                            Releases = artistAlbums
                                .Where(x => x.ReleaseDate > DateTime.MinValue)
                                .OrderBy(x => x.ReleaseDate)
                                .ThenBy(x => x.SortName).Select(x => new AlbumSearchResult
                                {
                                    AlbumType = SafeParser.ToEnum<AlbumType>(x.ReleaseType),
                                    ReleaseDate = x.ReleaseDate.ToString("o", CultureInfo.InvariantCulture),
                                    UniqueId = SafeParser.Hash(x.MusicBrainzId.ToString()),
                                    Name = x.Name,
                                    NameNormalized = x.NameNormalized,
                                    MusicBrainzResourceGroupId = x.ReleaseGroupMusicBrainzId,
                                    SortName = x.SortName,
                                    MusicBrainzId = x.MusicBrainzId
                                }).ToArray()
                        });
                    }
                    
                    totalCount = directArtists.Length;
                }
                else
                {
                    // Optimized EF Core query with no tracking for read-only operations
                    var artists = await context.Artists
                        .AsNoTracking()
                        .Where(a => musicBrainzIdsFromLucene.Contains(a.MusicBrainzIdRaw))
                        .OrderBy(a => a.SortName)
                        .ToArrayAsync(cancellationToken);

                    foreach (var artist in artists)
                    {
                        var rank = artist.NameNormalized == query.NameNormalized ? 10 : 1;
                        if (artist.AlternateNamesValues.Contains(query.NameNormalized))
                        {
                            rank++;
                        }

                        if (artist.AlternateNamesValues.Contains(query.Name.CleanString().ToNormalizedString()))
                        {
                            rank++;
                        }

                        if (artist.AlternateNamesValues.Contains(query.NameNormalizedReversed))
                        {
                            rank++;
                        }

                        // Optimized EF Core query for albums with proper joins and filtering
                        var artistAlbums = await context.Albums
                            .AsNoTracking()
                            .Where(a => a.MusicBrainzArtistId == artist.MusicBrainzArtistId && 
                                       a.ReleaseDate > DateTime.MinValue) // DoIncludeInArtistSearch condition
                            .ToArrayAsync(cancellationToken);

                        if (artistAlbums.Length > 0)
                        {
                            // Group by release group and take the earliest release date from each group
                            var newArtistAlbums = artistAlbums
                                .GroupBy(x => x.ReleaseGroupMusicBrainzIdRaw)
                                .Select(group => group.OrderBy(x => x.ReleaseDate).First())
                                .ToArray();

                            artistAlbums = newArtistAlbums;
                        }

                        rank += artistAlbums.Length;

                        if (query.AlbumKeyValues != null)
                        {
                            rank += artistAlbums.Length;
                            foreach (var albumKeyValues in query.AlbumKeyValues)
                            {
                                rank += artistAlbums.Count(x =>
                                    x.ReleaseDate.Year.ToString() == albumKeyValues.Key &&
                                    x.NameNormalized == albumKeyValues.Value.ToNormalizedString());
                            }
                        }

                        data.Add(new ArtistSearchResult
                        {
                            AlternateNames = artist.AlternateNames?.ToTags()?.ToArray() ?? [],
                            FromPlugin =
                                $"{nameof(MusicBrainzArtistSearchEnginePlugin)}:{nameof(SQLiteMusicBrainzRepository)}",
                            UniqueId = SafeParser.Hash(artist.MusicBrainzId.ToString()),
                            Rank = rank,
                            Name = artist.Name,
                            SortName = artist.SortName,
                            MusicBrainzId = artist.MusicBrainzId,
                            AlbumCount = artistAlbums.Count(x => x.ReleaseDate > DateTime.MinValue),
                            Releases = artistAlbums
                                .Where(x => x.ReleaseDate > DateTime.MinValue)
                                .OrderBy(x => x.ReleaseDate)
                                .ThenBy(x => x.SortName).Select(x => new AlbumSearchResult
                                {
                                    AlbumType = SafeParser.ToEnum<AlbumType>(x.ReleaseType),
                                    ReleaseDate = x.ReleaseDate.ToString("o", CultureInfo.InvariantCulture),
                                    UniqueId = SafeParser.Hash(x.MusicBrainzId.ToString()),
                                    Name = x.Name,
                                    NameNormalized = x.NameNormalized,
                                    MusicBrainzResourceGroupId = x.ReleaseGroupMusicBrainzId,
                                    SortName = x.SortName,
                                    MusicBrainzId = x.MusicBrainzId
                                }).ToArray()
                        });
                    }

                    totalCount = artists.Length;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Let cancellation exceptions propagate
            throw;
        }
        catch (Exception e)
        {
            Logger.Error(e, "[MusicBrainzRepository] Search Engine Exception AritistQuery [{Query}]", query.ToString());
        }

        return new PagedResult<ArtistSearchResult>
        {
            OperationTime = Stopwatch.GetElapsedTime(startTicks).Microseconds,
            TotalCount = totalCount,
            TotalPages = maxResults > 0 ? SafeParser.ToNumber<int>((totalCount + maxResults - 1) / maxResults) : 0,
            Data = data.OrderByDescending(x => x.Rank).Take(Math.Max(0, maxResults)).ToArray()
        };
    }

    public override async Task<OperationResult<bool>> ImportData(CancellationToken cancellationToken = default)
    {
        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: ImportData"))
        {
            var configuration =
                await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            var batchSize = configuration.GetValue<int>(SettingRegistry.SearchEngineMusicBrainzImportBatchSize);
            var maxToProcess =
                configuration.GetValue<int>(SettingRegistry.SearchEngineMusicBrainzImportMaximumToProcess);
            if (maxToProcess == 0)
            {
                maxToProcess = int.MaxValue;
            }

            var storagePath = configuration.GetValue<string>(SettingRegistry.SearchEngineMusicBrainzStoragePath);
            if (storagePath == null || !Directory.Exists(storagePath))
            {
                Logger.Warning("MusicBrainz storage path is invalid [{KeyNam}]",
                    SettingRegistry.SearchEngineMusicBrainzStoragePath);
                return new OperationResult<bool>
                {
                    Data = false
                };
            }

            await LoadDataFromMusicBrainzFiles(cancellationToken).ConfigureAwait(false);
            
            // Create Lucene Index
            using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Created Lucene Index"))
            {
                var lucenePath = Path.Combine(storagePath, "lucene");
                if (Directory.Exists(lucenePath))
                {
                    Directory.Delete(lucenePath, true);
                }

                using (var dir = FSDirectory.Open(lucenePath))
                {
                    var analyzer = new StandardAnalyzer(AppLuceneVersion);
                    var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);
                    using (var writer = new IndexWriter(dir, indexConfig))
                    {
                        foreach (var artist in LoadedMaterializedArtists)
                        {
                            var artistDoc = new Document
                            {
                                new StringField(nameof(Artist.MusicBrainzIdRaw),
                                    artist.MusicBrainzIdRaw,
                                    Field.Store.YES),
                                new StringField(nameof(Artist.NameNormalized),
                                    artist.NameNormalized,
                                    Field.Store.YES),
                                new TextField(nameof(Artist.AlternateNames),
                                    artist.AlternateNames ?? string.Empty,
                                    Field.Store.YES)
                            };
                            writer.AddDocument(artistDoc);
                        }

                        writer.Flush(false, false);
                    }
                }
            }

            // Import data using EF Core with optimized bulk operations
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            
            // Ensure database is created
            await context.Database.EnsureCreatedAsync(cancellationToken);

            using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Inserted loaded artists"))
            {
                var batches = batchSize > 0 && LoadedMaterializedArtists.Count > 0 ? (LoadedMaterializedArtists.Count + batchSize - 1) / batchSize : 0;
                Logger.Debug("MusicBrainzRepository: Importing [{BatchCount}] Artist batches...", batches);
                
                for (var batch = 0; batch < batches; batch++)
                {
                    var batchItems = LoadedMaterializedArtists.Skip(batch * batchSize).Take(batchSize);
                    
                    await context.Artists.AddRangeAsync(batchItems, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    
                    // Clear change tracker to free memory for large imports
                    context.ChangeTracker.Clear();
                    
                    if (batch * batchSize > maxToProcess)
                    {
                        break;
                    }
                }

                var artistCount = await context.Artists.CountAsync(cancellationToken);
                Logger.Debug(
                    "MusicBrainzRepository: Imported [{Count}] artists of [{Loaded}] in [{BatchCount}] batches.",
                    artistCount, LoadedMaterializedArtists.Count, batches);
            }

            using (Operation.At(LogEventLevel.Debug)
                       .Time("MusicBrainzRepository: Inserted loaded artist relations"))
            {
                var batches = batchSize > 0 && LoadedMaterializedArtistRelations.Count > 0 ? (LoadedMaterializedArtistRelations.Count + batchSize - 1) / batchSize : 0;
                Logger.Debug("MusicBrainzRepository: Importing [{BatchCount}] Artist Relations batches...", batches);
                
                for (var batch = 0; batch < batches; batch++)
                {
                    var batchItems = LoadedMaterializedArtistRelations.Skip(batch * batchSize).Take(batchSize);
                    
                    await context.ArtistRelations.AddRangeAsync(batchItems, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    
                    // Clear change tracker to free memory for large imports
                    context.ChangeTracker.Clear();
                    
                    if (batch * batchSize > maxToProcess)
                    {
                        break;
                    }
                }

                var relationCount = await context.ArtistRelations.CountAsync(cancellationToken);
                Logger.Debug(
                    "MusicBrainzRepository: Imported [{Count}] artist relations of [{Loaded}] in [{BatchCount}] batches.",
                    relationCount, LoadedMaterializedArtistRelations.Count, batches);
            }

            using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Inserted loaded albums"))
            {
                var batches = batchSize > 0 && LoadedMaterializedAlbums.Count > 0 ? (LoadedMaterializedAlbums.Count + batchSize - 1) / batchSize : 0;
                Logger.Debug("MusicBrainzRepository: Importing [{BatchCount}] Album batches...", batches);
                
                for (var batch = 0; batch < batches; batch++)
                {
                    var batchItems = LoadedMaterializedAlbums.Skip(batch * batchSize).Take(batchSize);
                    
                    await context.Albums.AddRangeAsync(batchItems, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    
                    // Clear change tracker to free memory for large imports
                    context.ChangeTracker.Clear();
                    
                    if (batch * batchSize > maxToProcess)
                    {
                        break;
                    }
                }

                var albumCount = await context.Albums.CountAsync(cancellationToken);
                Logger.Debug(
                    "MusicBrainzRepository: Imported [{Count}] albums of [{Loaded}] in [{BatchCount}] batches.",
                    albumCount, LoadedMaterializedAlbums.Count, batches);
            }

            return new OperationResult<bool>
            {
                Data = LoadedMaterializedArtists.Count > 0 &&
                       LoadedMaterializedAlbums.Count > 0
            };
        }
    }
}
