using Melodee.Common.Data;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using NodaTime;
using Microsoft.EntityFrameworkCore;
using DataModels = Melodee.Common.Data.Models;

namespace Melodee.Tests.Common.Common.Services;

public class UserQueueServiceTests : ServiceTestBase
{
    [Fact]
    public async Task GetPlayQueueForUserAsync_WithValidUser_ReturnsPlayQueue()
    {
        var service = GetUserQueueService();
        var username = "testuser";
        
        // Create test data
        await using var context = await MockFactory().CreateDbContextAsync();
        var library = await CreateTestLibrary(context);
        var artist = await CreateTestArtist(context, library);
        var album = await CreateTestAlbum(context, artist);
        var song = await CreateTestSong(context, album);
        var user = await CreateTestUser(context, username);
        
        // Create play queue entry
        var playQueue = new DataModels.PlayQueue
        {
            PlayQueId = 1,
            UserId = user.Id,
            SongId = song.Id,
            SongApiKey = song.ApiKey,
            IsCurrentSong = true,
            Position = 30.5,
            ChangedBy = username,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PlayQues.Add(playQueue);
        await context.SaveChangesAsync();

        var result = await service.GetPlayQueueForUserAsync(username);

        Assert.NotNull(result);
        Assert.Equal(username, result.Username);
        Assert.Equal(1, result.Current);
        Assert.Equal(30.5, result.Position);
        Assert.Equal(username, result.ChangedBy);
        Assert.Single(result.Entry ?? []);
        Assert.Equal(song.ToApiKey(), result.Entry![0].Id);
    }

    [Fact]
    public async Task GetPlayQueueForUserAsync_WithNonExistentUser_ReturnsNull()
    {
        var service = GetUserQueueService();
        var nonExistentUsername = "nonexistent";

        var result = await service.GetPlayQueueForUserAsync(nonExistentUsername);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPlayQueueForUserAsync_WithUserHavingNoQueue_ReturnsNull()
    {
        var service = GetUserQueueService();
        var username = "testuser";
        
        // Create user but no play queue
        await using var context = await MockFactory().CreateDbContextAsync();
        await CreateTestUser(context, username);

        var result = await service.GetPlayQueueForUserAsync(username);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPlayQueueForUserAsync_WithMultipleSongsInQueue_ReturnsAllSongs()
    {
        var service = GetUserQueueService();
        var username = "testuser";
        
        await using var context = await MockFactory().CreateDbContextAsync();
        var library = await CreateTestLibrary(context);
        var artist = await CreateTestArtist(context, library);
        var album = await CreateTestAlbum(context, artist);
        var song1 = await CreateTestSong(context, album);
        var song2 = await CreateTestSong(context, album);
        var user = await CreateTestUser(context, username);
        
        // Create multiple play queue entries
        var playQueue1 = new DataModels.PlayQueue
        {
            PlayQueId = 1,
            UserId = user.Id,
            SongId = song1.Id,
            SongApiKey = song1.ApiKey,
            IsCurrentSong = true,
            Position = 0,
            ChangedBy = username,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        var playQueue2 = new DataModels.PlayQueue
        {
            PlayQueId = 2,
            UserId = user.Id,
            SongId = song2.Id,
            SongApiKey = song2.ApiKey,
            IsCurrentSong = false,
            Position = 0,
            ChangedBy = username,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PlayQues.AddRange(playQueue1, playQueue2);
        await context.SaveChangesAsync();

        var result = await service.GetPlayQueueForUserAsync(username);

        Assert.NotNull(result);
        Assert.Equal(2, result.Entry?.Length ?? 0);
        Assert.Equal(1, result.Current); // First song is current
    }

    [Fact]
    public async Task SavePlayQueueForUserAsync_WithValidData_SavesSuccessfully()
    {
        var service = GetUserQueueService();
        var username = "testuser";
        
        await using var context = await MockFactory().CreateDbContextAsync();
        var library = await CreateTestLibrary(context);
        var artist = await CreateTestArtist(context, library);
        var album = await CreateTestAlbum(context, artist);
        var song = await CreateTestSong(context, album);
        var user = await CreateTestUser(context, username);
        
        var apiIds = new[] { song.ToApiKey() };
        var currentApiId = song.ToApiKey();
        var position = 15.5;
        var client = "TestClient";

        var result = await service.SavePlayQueueForUserAsync(username, apiIds, currentApiId, position, client);

        Assert.True(result);
        
        // Verify data was saved
        var savedQueue = await context.PlayQues.Where(pq => pq.UserId == user.Id).ToListAsync();
        Assert.Single(savedQueue);
        Assert.Equal(song.Id, savedQueue[0].SongId);
        Assert.True(savedQueue[0].IsCurrentSong);
        Assert.Equal(position, savedQueue[0].Position);
        Assert.Equal(client, savedQueue[0].ChangedBy);
    }

    [Fact]
    public async Task SavePlayQueueForUserAsync_WithNullApiIds_ClearsExistingQueue()
    {
        var service = GetUserQueueService();
        var username = "testuser";
        
        await using var context = await MockFactory().CreateDbContextAsync();
        var library = await CreateTestLibrary(context);
        var artist = await CreateTestArtist(context, library);
        var album = await CreateTestAlbum(context, artist);
        var song = await CreateTestSong(context, album);
        var user = await CreateTestUser(context, username);
        
        // Create existing queue entry
        var existingQueue = new DataModels.PlayQueue
        {
            PlayQueId = 1,
            UserId = user.Id,
            SongId = song.Id,
            SongApiKey = song.ApiKey,
            IsCurrentSong = true,
            Position = 0,
            ChangedBy = username,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PlayQues.Add(existingQueue);
        await context.SaveChangesAsync();

        var result = await service.SavePlayQueueForUserAsync(username, null, null, null, null);

        Assert.True(result);
        
        // Verify queue was cleared
        var remainingQueue = await context.PlayQues.Where(pq => pq.UserId == user.Id).ToListAsync();
        Assert.Empty(remainingQueue);
    }

    [Fact]
    public async Task SavePlayQueueForUserAsync_WithNonExistentUser_ReturnsFalse()
    {
        var service = GetUserQueueService();
        var nonExistentUsername = "nonexistent";
        var apiIds = new[] { "test-id" };

        var result = await service.SavePlayQueueForUserAsync(nonExistentUsername, apiIds, null, null, null);

        Assert.False(result);
    }

    [Fact]
    public async Task SavePlayQueueForUserAsync_WithInvalidSongIds_IgnoresInvalidIds()
    {
        var service = GetUserQueueService();
        var username = "testuser";
        
        await using var context = await MockFactory().CreateDbContextAsync();
        var library = await CreateTestLibrary(context);
        var artist = await CreateTestArtist(context, library);
        var album = await CreateTestAlbum(context, artist);
        var validSong = await CreateTestSong(context, album);
        var user = await CreateTestUser(context, username);
        
        var apiIds = new[] { validSong.ToApiKey(), "invalid-song-id" };
        var currentApiId = validSong.ToApiKey();

        var result = await service.SavePlayQueueForUserAsync(username, apiIds, currentApiId, null, null);

        Assert.True(result);
        
        // Verify only valid song was saved
        var savedQueue = await context.PlayQues.Where(pq => pq.UserId == user.Id).ToListAsync();
        Assert.Single(savedQueue);
        Assert.Equal(validSong.Id, savedQueue[0].SongId);
    }

    [Fact]
    public async Task SavePlayQueueForUserAsync_UpdatesExistingQueue_ModifiesExistingEntries()
    {
        var service = GetUserQueueService();
        var username = "testuser";
        
        await using var context = await MockFactory().CreateDbContextAsync();
        var library = await CreateTestLibrary(context);
        var artist = await CreateTestArtist(context, library);
        var album = await CreateTestAlbum(context, artist);
        var song1 = await CreateTestSong(context, album);
        var song2 = await CreateTestSong(context, album);
        var user = await CreateTestUser(context, username);
        
        // Create initial queue with song1
        var initialQueue = new DataModels.PlayQueue
        {
            PlayQueId = 1,
            UserId = user.Id,
            SongId = song1.Id,
            SongApiKey = song1.ApiKey,
            IsCurrentSong = true,
            Position = 10,
            ChangedBy = username,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PlayQues.Add(initialQueue);
        await context.SaveChangesAsync();
        
        // Update queue to include song2 as current and remove song1
        var newApiIds = new[] { song2.ToApiKey() };
        var newCurrentApiId = song2.ToApiKey();
        var newPosition = 25.0;
        var newClient = "UpdatedClient";

        var result = await service.SavePlayQueueForUserAsync(username, newApiIds, newCurrentApiId, newPosition, newClient);

        Assert.True(result);
        
        // Verify old queue was removed and new queue was added
        var updatedQueue = await context.PlayQues.Where(pq => pq.UserId == user.Id).ToListAsync();
        Assert.Single(updatedQueue);
        Assert.Equal(song2.Id, updatedQueue[0].SongId);
        Assert.True(updatedQueue[0].IsCurrentSong);
        Assert.Equal(newPosition, updatedQueue[0].Position);
        Assert.Equal(newClient, updatedQueue[0].ChangedBy);
    }

    [Fact]
    public async Task SavePlayQueueForUserAsync_WithMultipleSongs_SetsCorrectCurrentSong()
    {
        var service = GetUserQueueService();
        var username = "testuser";
        
        await using var context = await MockFactory().CreateDbContextAsync();
        var library = await CreateTestLibrary(context);
        var artist = await CreateTestArtist(context, library);
        var album = await CreateTestAlbum(context, artist);
        var song1 = await CreateTestSong(context, album);
        var song2 = await CreateTestSong(context, album);
        var song3 = await CreateTestSong(context, album);
        var user = await CreateTestUser(context, username);
        
        var apiIds = new[] { song1.ToApiKey(), song2.ToApiKey(), song3.ToApiKey() };
        var currentApiId = song2.ToApiKey(); // Set song2 as current
        var position = 42.0;

        var result = await service.SavePlayQueueForUserAsync(username, apiIds, currentApiId, position, username);

        Assert.True(result);
        
        // Verify correct current song and position
        var savedQueue = await context.PlayQues.Where(pq => pq.UserId == user.Id).OrderBy(pq => pq.PlayQueId).ToListAsync();
        Assert.Equal(3, savedQueue.Count);
        
        var currentSong = savedQueue.Single(sq => sq.IsCurrentSong);
        Assert.Equal(song2.Id, currentSong.SongId);
        Assert.Equal(position, currentSong.Position);
        
        // Verify other songs are not current and have position 0
        var nonCurrentSongs = savedQueue.Where(sq => !sq.IsCurrentSong).ToList();
        Assert.Equal(2, nonCurrentSongs.Count);
        Assert.All(nonCurrentSongs, sq => Assert.Equal(0, sq.Position));
    }

    #region Helper Methods

    private async Task<DataModels.Library> CreateTestLibrary(MelodeeDbContext context)
    {
        var library = new DataModels.Library
        {
            ApiKey = Guid.NewGuid(),
            Name = $"Test Library {Guid.NewGuid()}",
            Path = "/test/library/",
            Type = (int)LibraryType.Storage,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Libraries.Add(library);
        await context.SaveChangesAsync();
        return library;
    }

    private async Task<DataModels.Artist> CreateTestArtist(MelodeeDbContext context, DataModels.Library library)
    {
        var artist = new DataModels.Artist
        {
            ApiKey = Guid.NewGuid(),
            Name = $"Test Artist {Guid.NewGuid()}",
            NameNormalized = $"testartist{Guid.NewGuid()}".Replace("-", ""),
            LibraryId = library.Id,
            Directory = "/testartist/",
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        return artist;
    }

    private async Task<DataModels.Album> CreateTestAlbum(MelodeeDbContext context, DataModels.Artist artist)
    {
        var album = new DataModels.Album
        {
            ApiKey = Guid.NewGuid(),
            Name = $"Test Album {Guid.NewGuid()}",
            NameNormalized = $"testalbum{Guid.NewGuid()}".Replace("-", ""),
            ArtistId = artist.Id,
            Directory = "/testalbum/",
            ReleaseDate = new LocalDate(2023, 1, 1),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Albums.Add(album);
        await context.SaveChangesAsync();
        return album;
    }

    private async Task<DataModels.Song> CreateTestSong(MelodeeDbContext context, DataModels.Album album)
    {
        // Get the next song number for this album to avoid unique constraint violations
        var existingSongCount = await context.Songs.CountAsync(s => s.AlbumId == album.Id);
        var songNumber = existingSongCount + 1;
        
        var song = new DataModels.Song
        {
            ApiKey = Guid.NewGuid(),
            Title = $"Test Song {songNumber}",
            TitleNormalized = $"testsong{songNumber}",
            AlbumId = album.Id,
            SongNumber = songNumber,
            FileName = $"test{songNumber}.mp3",
            FileSize = 1000000,
            FileHash = $"testhash{songNumber}",
            Duration = 180000,
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 120,
            ContentType = "audio/mpeg",
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Songs.Add(song);
        await context.SaveChangesAsync();
        return song;
    }

    private async Task<DataModels.User> CreateTestUser(MelodeeDbContext context, string username)
    {
        var user = new DataModels.User
        {
            UserName = username,
            UserNameNormalized = username.ToUpper(),
            Email = $"{username}@test.com",
            EmailNormalized = $"{username.ToUpper()}@TEST.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    #endregion
}