using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine;
using Melodee.Common.Utility;
using Melodee.Tests.Common.Services;
using NodaTime;
using Artist = Melodee.Common.Data.Models.Artist;
using Album = Melodee.Common.Data.Models.Album;
using Song = Melodee.Common.Data.Models.Song;

namespace Melodee.Tests.Common.Plugins;

public class MelodeeArtistSearchEnginePluginTests : ServiceTestBase
{
    private MelodeeArtistSearchEnginePlugin GetMelodeeArtistSearchEnginePlugin()
    {
        return new MelodeeArtistSearchEnginePlugin(MockFactory());
    }

    private Task<Artist> CreateTestArtistAsync(string name = "Test Artist", Guid? musicBrainzId = null, string? alternateNames = null)
    {
        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
        var artist = new Artist
        {
            Name = $"{name}_{uniqueId}",
            NameNormalized = $"{name}_{uniqueId}".ToNormalizedString()!,
            SortName = $"{name}_{uniqueId}",
            MusicBrainzId = musicBrainzId ?? Guid.NewGuid(),
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            AlternateNames = alternateNames,
            Directory = $"/music/{name}_{uniqueId}",
            LibraryId = 1
        };
        return Task.FromResult(artist);
    }

    private Task<Album> CreateTestAlbumAsync(int artistId, string name = "Test Album", AlbumType albumType = AlbumType.Album)
    {
        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
        var album = new Album
        {
            Name = $"{name}_{uniqueId}",
            NameNormalized = $"{name}_{uniqueId}".ToNormalizedString()!,
            SortName = $"{name}_{uniqueId}",
            ArtistId = artistId,
            AlbumType = (short)albumType,
            MusicBrainzId = Guid.NewGuid(),
            ReleaseDate = new LocalDate(2020, 1, 1),
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            Directory = $"/music/album_{uniqueId}"
        };
        return Task.FromResult(album);
    }

    private Task<Song> CreateTestSongAsync(int albumId, string title = "Test Song", int playedCount = 0, int songNumber = 1)
    {
        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
        var song = new Song
        {
            Title = $"{title}_{uniqueId}",
            TitleNormalized = $"{title}_{uniqueId}".ToNormalizedString()!,
            TitleSort = $"{title}_{uniqueId}",
            AlbumId = albumId,
            SongNumber = songNumber,
            PlayedCount = playedCount,
            LastPlayedAt = playedCount > 0 ? SystemClock.Instance.GetCurrentInstant() : null,
            Duration = 180000, // 3 minutes
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            FileName = $"{title}_{uniqueId}.mp3",
            FileSize = 5000000, // 5MB
            FileHash = SafeParser.Hash($"{title}_{uniqueId}").ToString(),
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 120,
            ContentType = "audio/mpeg"
        };
        return Task.FromResult(song);
    }

    #region Plugin Properties Tests

    [Fact]
    public void Plugin_ShouldHaveCorrectProperties()
    {
        var plugin = GetMelodeeArtistSearchEnginePlugin();

        Assert.Equal("018A798D-7B68-4F3E-80CD-1BAF03998C0B", plugin.Id);
        Assert.Equal("Melodee Database", plugin.DisplayName);
        Assert.True(plugin.IsEnabled);
        Assert.False(plugin.StopProcessing);
        Assert.Equal(0, plugin.SortOrder);
    }

    #endregion

    #region DoArtistSearchAsync Tests

    [Fact]
    public async Task DoArtistSearchAsync_ShouldReturnEmpty_WhenNoMatchingArtists()
    {
        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = "NonExistentArtist" };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.True(result.OperationTime >= 0);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldFindArtistByMusicBrainzId_WhenMusicBrainzIdProvided()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var musicBrainzId = Guid.NewGuid();
        var artist = await CreateTestArtistAsync("Beatles", musicBrainzId);
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        // Add an album for the artist
        var album = await CreateTestAlbumAsync(artist.Id, "Abbey Road");
        context.Albums.Add(album);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery 
        { 
            Name = "Beatles", 
            MusicBrainzId = musicBrainzId.ToString() 
        };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        Assert.Equal(artist.Id, foundArtist.Id);
        Assert.Equal(artist.Name, foundArtist.Name);
        Assert.Equal(musicBrainzId, foundArtist.MusicBrainzId);
        Assert.Equal(short.MaxValue, foundArtist.Rank); // MusicBrainzId match gets highest rank
        Assert.Single(foundArtist.Releases!);
        Assert.Equal(album.Name, foundArtist.Releases!.First().Name);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldFindArtistByNameWithMatchingAlbums_WhenAlbumKeyValuesProvided()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("The Beatles");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album1 = await CreateTestAlbumAsync(artist.Id, "Abbey Road");
        var album2 = await CreateTestAlbumAsync(artist.Id, "Sgt. Pepper's");
        context.Albums.AddRange(album1, album2);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery 
        { 
            Name = artist.Name,
            AlbumKeyValues = new[] 
            {
                new KeyValue("1967", album1.NameNormalized),
                new KeyValue("1969", album2.NameNormalized)
            }
        };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        Assert.Equal(artist.Id, foundArtist.Id);
        Assert.Equal(artist.Name, foundArtist.Name);
        Assert.Equal(2, foundArtist.Releases!.Length);
        Assert.Contains(foundArtist.Releases!, r => r.Name == album1.Name);
        Assert.Contains(foundArtist.Releases!, r => r.Name == album2.Name);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldFindArtistByNameNormalized_WhenNoMusicBrainzIdOrAlbums()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Radiohead");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album = await CreateTestAlbumAsync(artist.Id, "OK Computer");
        context.Albums.Add(album);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = artist.NameNormalized };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        Assert.Equal(artist.Id, foundArtist.Id);
        Assert.Equal(artist.Name, foundArtist.Name);
        Assert.Equal(1, foundArtist.Rank); // Name-only match gets lowest rank
        Assert.Single(foundArtist.Releases!);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldFindArtistByAlternateName_WhenAlternateNamesMatch()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var alternateName = "Alternative Name";
        var artist = await CreateTestArtistAsync("Main Artist Name", alternateNames: alternateName.ToNormalizedString());
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album = await CreateTestAlbumAsync(artist.Id, "Test Album");
        context.Albums.Add(album);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = alternateName };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        Assert.Equal(artist.Id, foundArtist.Id);
        Assert.Equal(artist.Name, foundArtist.Name);
        Assert.Contains(alternateName.ToNormalizedString(), foundArtist.AlternateNames!);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldRespectMaxResults_WhenMultipleArtistsMatch()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        // Create multiple artists with similar names
        var artists = new List<Artist>();
        for (int i = 1; i <= 5; i++)
        {
            var artist = await CreateTestArtistAsync($"TestArtist{i:D2}");
            artists.Add(artist);
            context.Artists.Add(artist);
        }
        await context.SaveChangesAsync();
        
        // Add albums for each artist
        foreach (var artist in artists)
        {
            var album = await CreateTestAlbumAsync(artist.Id, "Test Album");
            context.Albums.Add(album);
        }
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = "TestArtist" };
        const int maxResults = 3;

        var result = await plugin.DoArtistSearchAsync(query, maxResults);

        Assert.NotNull(result);
        Assert.True(result.Data.Count() <= maxResults);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldHandleCancellation_WhenCancellationRequested()
    {
        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = "Test Artist" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => plugin.DoArtistSearchAsync(query, 10, cts.Token));
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldReturnCorrectAlbumType_WhenAlbumsHaveDifferentTypes()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album = await CreateTestAlbumAsync(artist.Id, "Test Album", AlbumType.Other);
        context.Albums.Add(album);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = artist.NameNormalized };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        Assert.Single(foundArtist.Releases!);
        Assert.Equal(AlbumType.Other, foundArtist.Releases!.First().AlbumType);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldOrderReleasesByDateThenSortName()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album1 = await CreateTestAlbumAsync(artist.Id, "Z Album");
        album1.ReleaseDate = new LocalDate(2020, 1, 1);
        album1.SortName = "Z Album";
        
        var album2 = await CreateTestAlbumAsync(artist.Id, "A Album");
        album2.ReleaseDate = new LocalDate(2020, 1, 1);
        album2.SortName = "A Album";
        
        var album3 = await CreateTestAlbumAsync(artist.Id, "B Album");
        album3.ReleaseDate = new LocalDate(2019, 1, 1);
        album3.SortName = "B Album";
        
        context.Albums.AddRange(album1, album2, album3);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = artist.NameNormalized };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        Assert.Equal(3, foundArtist.Releases!.Length);
        
        // Should be ordered by release date first, then by sort name
        Assert.Equal(album3.Name, foundArtist.Releases![0].Name); // 2019
        Assert.Equal(album2.Name, foundArtist.Releases![1].Name); // 2020, A Album
        Assert.Equal(album1.Name, foundArtist.Releases![2].Name); // 2020, Z Album
    }

    #endregion

    #region DoArtistTopSongsSearchAsync Tests

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_ShouldReturnEmpty_WhenArtistNotFound()
    {
        var plugin = GetMelodeeArtistSearchEnginePlugin();
        const int nonExistentArtistId = 999999;

        var result = await plugin.DoArtistTopSongsSearchAsync(nonExistentArtistId, 10);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.True(result.OperationTime >= 0);
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_ShouldReturnTopSongs_WhenArtistExists()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album = await CreateTestAlbumAsync(artist.Id, "Test Album");
        context.Albums.Add(album);
        await context.SaveChangesAsync();
        
        var song1 = await CreateTestSongAsync(album.Id, "Popular Song", playedCount: 100, songNumber: 1);
        var song2 = await CreateTestSongAsync(album.Id, "Less Popular Song", playedCount: 50, songNumber: 2);
        var song3 = await CreateTestSongAsync(album.Id, "Unpopular Song", playedCount: 10, songNumber: 3);
        
        context.Songs.AddRange(song1, song2, song3);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var result = await plugin.DoArtistTopSongsSearchAsync(artist.Id, 10);

        Assert.NotNull(result);
        Assert.Equal(3, result.Data.Count());
        Assert.Equal(1, result.TotalPages);
        
        var dataArray = result.Data.ToArray();
        // Should be ordered by play count descending
        Assert.Equal(song1.Title, dataArray[0].Name);
        Assert.Equal(song2.Title, dataArray[1].Name);
        Assert.Equal(song3.Title, dataArray[2].Name);
        
        // Verify the ranking (SortOrder)
        Assert.Equal(1, dataArray[0].SortOrder);
        Assert.Equal(2, dataArray[1].SortOrder);
        Assert.Equal(3, dataArray[2].SortOrder);
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_ShouldRespectMaxResults_WhenManysongsExist()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album = await CreateTestAlbumAsync(artist.Id, "Test Album");
        context.Albums.Add(album);
        await context.SaveChangesAsync();
        
        // Create more songs than maxResults
        var songs = new List<Song>();
        for (int i = 1; i <= 10; i++)
        {
            var song = await CreateTestSongAsync(album.Id, $"Song {i}", playedCount: 100 - i, songNumber: i);
            songs.Add(song);
            context.Songs.Add(song);
        }
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        const int maxResults = 5;
        var result = await plugin.DoArtistTopSongsSearchAsync(artist.Id, maxResults);

        Assert.NotNull(result);
        Assert.Equal(maxResults, result.Data.Count());
        
        var dataArray = result.Data.ToArray();
        // Verify correct ordering (highest played count first)
        for (int i = 0; i < maxResults; i++)
        {
            Assert.Equal(songs[i].Title, dataArray[i].Name);
        }
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_ShouldHandleCancellation_WhenCancellationRequested()
    {
        var plugin = GetMelodeeArtistSearchEnginePlugin();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => plugin.DoArtistTopSongsSearchAsync(1, 10, cts.Token));
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_ShouldOrderByCriteria_WhenPlayCountsAreSame()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album = await CreateTestAlbumAsync(artist.Id, "Test Album");
        album.SortOrder = 1;
        context.Albums.Add(album);
        await context.SaveChangesAsync();
        
        var baseTime = SystemClock.Instance.GetCurrentInstant();
        
        var song1 = await CreateTestSongAsync(album.Id, "Song A", playedCount: 50, songNumber: 1);
        song1.LastPlayedAt = baseTime.Plus(Duration.FromMinutes(10));
        song1.SortOrder = 2;
        song1.TitleSort = "Song A";
        
        var song2 = await CreateTestSongAsync(album.Id, "Song B", playedCount: 50, songNumber: 2);
        song2.LastPlayedAt = baseTime.Plus(Duration.FromMinutes(5));
        song2.SortOrder = 1;
        song2.TitleSort = "Song B";
        
        context.Songs.AddRange(song1, song2);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var result = await plugin.DoArtistTopSongsSearchAsync(artist.Id, 10);

        Assert.NotNull(result);
        Assert.Equal(2, result.Data.Count());
        
        var dataArray = result.Data.ToArray();
        // Should be ordered by LastPlayedAt desc when PlayedCount is same
        Assert.Equal(song1.Title, dataArray[0].Name);
        Assert.Equal(song2.Title, dataArray[1].Name);
    }

    #endregion

    #region Edge Cases and Error Handling Tests

    [Fact]
    public async Task DoArtistSearchAsync_ShouldHandleNullAlbumKeyValues_Gracefully()
    {
        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery 
        { 
            Name = "Test Artist",
            AlbumKeyValues = null
        };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldHandleEmptyAlbumKeyValues_Gracefully()
    {
        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery 
        { 
            Name = "Test Artist",
            AlbumKeyValues = Array.Empty<KeyValue>()
        };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldHandleInvalidMusicBrainzId_Gracefully()
    {
        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery 
        { 
            Name = "Test Artist",
            MusicBrainzId = "invalid-guid"
        };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldHandleZeroMaxResults_Gracefully()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = artist.NameNormalized };

        var result = await plugin.DoArtistSearchAsync(query, 0);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_ShouldHandleZeroMaxResults_Gracefully()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();

        var result = await plugin.DoArtistTopSongsSearchAsync(artist.Id, 0);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldHandleArtistWithNoAlbums_Gracefully()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        // Note: No albums added

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = artist.NameNormalized };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        Assert.Empty(foundArtist.Releases!);
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_ShouldHandleArtistWithNoSongs_Gracefully()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        // Note: No albums or songs added

        var plugin = GetMelodeeArtistSearchEnginePlugin();

        var result = await plugin.DoArtistTopSongsSearchAsync(artist.Id, 10);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldHandleAlbumsWithNullProperties_Gracefully()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var artist = await CreateTestArtistAsync("Test Artist");
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        var album = await CreateTestAlbumAsync(artist.Id, "Test Album");
        album.SortName = null; // Test null SortName
        album.AlternateNames = null; // Test null AlternateNames
        context.Albums.Add(album);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery { Name = artist.NameNormalized };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        Assert.Single(foundArtist.Releases!);
        
        // Should use Name when SortName is null
        Assert.Equal(artist.Name, foundArtist.Releases!.First().SortName);
    }

    [Fact]
    public async Task DoArtistSearchAsync_ShouldGenerateCorrectUniqueId_FromMusicBrainzId()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var musicBrainzId = Guid.NewGuid();
        var artist = await CreateTestArtistAsync("Test Artist", musicBrainzId);
        context.Artists.Add(artist);
        await context.SaveChangesAsync();

        var plugin = GetMelodeeArtistSearchEnginePlugin();
        var query = new ArtistQuery 
        { 
            Name = "Test Artist", 
            MusicBrainzId = musicBrainzId.ToString() 
        };

        var result = await plugin.DoArtistSearchAsync(query, 10);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        var foundArtist = result.Data.First();
        
        var expectedUniqueId = SafeParser.Hash(musicBrainzId.ToString());
        Assert.Equal(expectedUniqueId, foundArtist.UniqueId);
    }

    #endregion
}
