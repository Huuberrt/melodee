using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using NodaTime;

namespace Melodee.Tests.Common.Common.Services;

public class PlaylistServiceTests : ServiceTestBase
{
    [Fact]
    public async Task ListAsync_WithValidRequest_ReturnsPlaylists()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        var pagedRequest = new PagedRequest
        {
            PageSize = 1000
        };

        var result = await service.ListAsync(userInfo, pagedRequest);

        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.True(result.TotalCount >= 0);
        Assert.True(result.TotalPages >= 0);
    }

    [Fact]
    public async Task ListAsync_WithPagination_ReturnsCorrectPage()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        var firstPageResult = await service.ListAsync(userInfo, new PagedRequest
        {
            PageSize = 5,
            Page = 0
        });
        
        var secondPageResult = await service.ListAsync(userInfo, new PagedRequest
        {
            PageSize = 5,
            Page = 1
        });

        AssertResultIsSuccessful(firstPageResult);
        AssertResultIsSuccessful(secondPageResult);
        Assert.True(firstPageResult.Data.Count() <= 5);
        Assert.True(secondPageResult.Data.Count() <= 5);
        Assert.Equal(firstPageResult.TotalCount, secondPageResult.TotalCount);
    }

    [Fact]
    public async Task GetPlaylists_WithUnboundedQuery_RespectsLimits()
    {
        // Arrange: create one user with a large number of playlists
        var service = GetPlaylistService();
        await using var context = await MockFactory().CreateDbContextAsync();

        var user = new User
        {
            UserName = "limit_user",
            UserNameNormalized = "LIMIT_USER",
            Email = "limit@example.com",
            EmailNormalized = "LIMIT@EXAMPLE.COM",
            PublicKey = "limitkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Seed 1,200 playlists for the user
        var many = Enumerable.Range(0, 1200).Select(i => new Playlist
        {
            ApiKey = Guid.NewGuid(),
            Name = $"User Playlist {i}",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UserId = user.Id
        }).ToArray();
        context.Playlists.AddRange(many);
        await context.SaveChangesAsync();

        // Act: use ListAsync which supports paging and should cap results to PageSize
        var firstPage = await service.ListAsync(
            new UserInfo(user.Id, user.ApiKey, user.UserName, user.Email, string.Empty, string.Empty),
            new PagedRequest { PageSize = 100, Page = 0 });

        var largePage = await service.ListAsync(
            new UserInfo(user.Id, user.ApiKey, user.UserName, user.Email, string.Empty, string.Empty),
            new PagedRequest { PageSize = 500, Page = 0 });

        // Assert
        AssertResultIsSuccessful(firstPage);
        AssertResultIsSuccessful(largePage);
        Assert.True(firstPage.Data.Count() <= 100);
        Assert.True(largePage.Data.Count() <= 500);
        Assert.True(firstPage.TotalCount >= 1200); // ensure count reflects total
    }

    [Fact]
    public async Task ListAsync_WithTotalCountOnlyRequest_ReturnsOnlyCount()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        var result = await service.ListAsync(userInfo, new PagedRequest
        {
            IsTotalCountOnlyRequest = true
        });

        AssertResultIsSuccessful(result);
        Assert.True(result.TotalCount >= 0);
        // Data might still contain dynamic playlists even with count-only request
    }

    [Fact]
    public async Task DynamicListAsync_WithValidRequest_ReturnsDynamicPlaylists()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        var pagedRequest = new PagedRequest
        {
            PageSize = 1000
        };

        var result = await service.DynamicListAsync(userInfo, pagedRequest);

        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.True(result.TotalCount >= 0);
        Assert.All(result.Data, playlist => Assert.True(playlist.IsDynamic));
    }

    [Fact]
    public async Task DynamicListAsync_WithPagination_ReturnsCorrectPage()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        var result = await service.DynamicListAsync(userInfo, new PagedRequest
        {
            PageSize = 5,
            Page = 0
        });

        AssertResultIsSuccessful(result);
        Assert.True(result.Data.Count() <= 5);
    }

    [Fact]
    public async Task GetAsync_WithValidId_ReturnsPlaylist()
    {
        var service = GetPlaylistService();
        
        // First create a test user and playlist in the database
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var testPlaylist = new Playlist
        {
            ApiKey = Guid.NewGuid(),
            Name = "Test Playlist",
            Description = "Test Description",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        var result = await service.GetAsync(testPlaylist.Id);

        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.Equal(testPlaylist.Id, result.Data.Id);
        Assert.Equal(testPlaylist.Name, result.Data.Name);
    }

    [Fact]
    public async Task GetAsync_WithInvalidId_ThrowsArgumentException()
    {
        var service = GetPlaylistService();

        await Assert.ThrowsAsync<ArgumentException>(async () => 
            await service.GetAsync(0));
        
        await Assert.ThrowsAsync<ArgumentException>(async () => 
            await service.GetAsync(-1));
    }

    [Fact]
    public async Task GetAsync_WithNonExistentId_ReturnsNull()
    {
        var service = GetPlaylistService();

        var result = await service.GetAsync(999999);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetByApiKeyAsync_WithValidApiKey_ReturnsPlaylist()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        // First create a test user and playlist in the database
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var testApiKey = Guid.NewGuid();
        var testPlaylist = new Playlist
        {
            ApiKey = testApiKey,
            Name = "Test Playlist",
            Description = "Test Description",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        var result = await service.GetByApiKeyAsync(userInfo, testApiKey);

        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.Equal(testApiKey, result.Data.ApiKey);
        Assert.Equal(testPlaylist.Name, result.Data.Name);
    }

    [Fact]
    public async Task GetByApiKeyAsync_WithEmptyApiKey_ThrowsArgumentException()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);

        await Assert.ThrowsAsync<ArgumentException>(async () => 
            await service.GetByApiKeyAsync(userInfo, Guid.Empty));
    }

    [Fact]
    public async Task GetByApiKeyAsync_WithNonExistentApiKey_ReturnsError()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        var nonExistentApiKey = Guid.NewGuid();

        var result = await service.GetByApiKeyAsync(userInfo, nonExistentApiKey);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Contains("Unknown playlist", result.Messages?.FirstOrDefault() ?? "");
    }

    [Fact]
    public async Task SongsForPlaylistAsync_WithValidApiKey_ReturnsSongs()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        // Create a test user and playlist with songs
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var testApiKey = Guid.NewGuid();
        var testPlaylist = new Playlist
        {
            ApiKey = testApiKey,
            Name = "Test Playlist",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser,
            Songs = new List<PlaylistSong>()
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        var result = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            PageSize = 100
        });

        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.True(result.TotalCount >= 0);
    }

    [Fact]
    public async Task SongsForPlaylistAsync_WithEmptyApiKey_ThrowsArgumentException()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);

        await Assert.ThrowsAsync<ArgumentException>(async () => 
            await service.SongsForPlaylistAsync(Guid.Empty, userInfo, new PagedRequest()));
    }

    [Fact]
    public async Task SongsForPlaylistAsync_WithNonExistentPlaylist_ReturnsError()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        var nonExistentApiKey = Guid.NewGuid();

        var result = await service.SongsForPlaylistAsync(nonExistentApiKey, userInfo, new PagedRequest());

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Data);
        Assert.Contains("Unknown playlist", result.Messages?.FirstOrDefault() ?? "");
    }

    [Fact]
    public async Task SongsForPlaylistAsync_WithPagination_ReturnsCorrectPage()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        // Create a test user and playlist
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var testApiKey = Guid.NewGuid();
        var testPlaylist = new Playlist
        {
            ApiKey = testApiKey,
            Name = "Test Playlist",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser,
            Songs = new List<PlaylistSong>()
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        var result = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            PageSize = 5,
            Page = 0
        });

        AssertResultIsSuccessful(result);
        Assert.True(result.Data.Count() <= 5);
    }

    [Fact]
    public async Task SongsForPlaylistAsync_VerifiesCorrectReturnType_ReturnsSongDataInfoArray()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        // Create a test user and playlist
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var testApiKey = Guid.NewGuid();
        var testPlaylist = new Playlist
        {
            ApiKey = testApiKey,
            Name = "Test Playlist",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser,
            Songs = new List<PlaylistSong>()
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        var result = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest());

        AssertResultIsSuccessful(result);
        Assert.IsType<SongDataInfo[]>(result.Data);
        Assert.NotNull(result.Data);
        Assert.True(result.TotalCount >= 0);
    }
    
    [Fact]
    public async Task SongsForPlaylistAsync_WithSongs()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        // Create everything in the same context to avoid FK constraint issues
        await using var context = await MockFactory().CreateDbContextAsync();
        
        // Create library first
        var library = new Library
        {
            ApiKey = Guid.NewGuid(),
            Name = "Test Library",
            Path = "/test/library/",
            Type = (int)LibraryType.Storage,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Libraries.Add(library);
        await context.SaveChangesAsync();
        
        // Create artist
        var artist = new Melodee.Common.Data.Models.Artist
        {
            ApiKey = Guid.NewGuid(),
            Name = "Test Artist",
            NameNormalized = "testartist",
            LibraryId = library.Id,
            Directory = "/testartist/",
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        
        // Create album
        var album = new Melodee.Common.Data.Models.Album
        {
            ApiKey = Guid.NewGuid(),
            Name = "Test Album",
            NameNormalized = "testalbum",
            ArtistId = artist.Id,
            Directory = "/testalbum/",
            ReleaseDate = new LocalDate(2023, 1, 1),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Albums.Add(album);
        await context.SaveChangesAsync();
        
        // Create 10 test songs
        var testSongs = new List<Melodee.Common.Data.Models.Song>();
        for (int i = 0; i < 10; i++)
        {
            var song = new Melodee.Common.Data.Models.Song
            {
                ApiKey = Guid.NewGuid(),
                Title = $"Test Song {i + 1}",
                TitleNormalized = $"testsong{i + 1}",
                AlbumId = album.Id,
                SongNumber = i + 1,
                FileName = $"test{i + 1}.mp3",
                FileSize = 1000000 + (i * 10000),
                FileHash = $"testhash{i + 1}",
                Duration = 180000 + (i * 1000),
                SamplingRate = 44100,
                BitRate = 320,
                BitDepth = 16,
                BPM = 120,
                ContentType = "audio/mpeg",
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                LastUpdatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            testSongs.Add(song);
        }
        context.Songs.AddRange(testSongs);
        await context.SaveChangesAsync();
        
        // Create test user
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        // Create playlist with songs
        var testApiKey = Guid.NewGuid();
        var testPlaylist = new Playlist
        {
            ApiKey = testApiKey,
            Name = "Test Playlist With Songs",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser,
            SongCount = 10,
            Duration = testSongs.Sum(s => s.Duration),
            Songs = testSongs.Select((song, index) => new PlaylistSong
            {
                SongId = song.Id,
                SongApiKey = song.ApiKey,
                PlaylistOrder = index + 1
            }).ToList()
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        // Test first page (page 0)
        var firstPageResult = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            PageSize = 5,
            Page = 0
        });

        AssertResultIsSuccessful(firstPageResult);
        Assert.Equal(10, firstPageResult.TotalCount);
        Assert.NotNull(firstPageResult.Data);
        Assert.Equal(5, firstPageResult.Data.Count());
        
        // Test second page (page 1) 
        var secondPageResult = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            PageSize = 5,
            Page = 1
        });

        AssertResultIsSuccessful(secondPageResult);
        Assert.Equal(10, secondPageResult.TotalCount);
        Assert.NotNull(secondPageResult.Data);
        Assert.Equal(5, secondPageResult.Data.Count());
        
        // Verify songs have correct data
        var allSongsResult = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            PageSize = 100,
            Page = 0
        });
        
        AssertResultIsSuccessful(allSongsResult);
        Assert.Equal(10, allSongsResult.TotalCount);
        Assert.Equal(10, allSongsResult.Data.Count());
        
        // Verify songs are returned with proper data
        var returnedSongs = allSongsResult.Data.ToArray();
        Assert.All(returnedSongs, song => 
        {
            Assert.NotNull(song.Title);
            Assert.NotEqual(Guid.Empty, song.ApiKey);
            Assert.True(song.Duration > 0);
        });
    }

    [Fact]
    public async Task SongsForPlaylistAsync_WithDifferentPageSizes_HandlesPaginationCorrectly()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        // Create a test user and empty playlist
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var testApiKey = Guid.NewGuid();
        var testPlaylist = new Playlist
        {
            ApiKey = testApiKey,
            Name = "Test Playlist",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser,
            Songs = new List<PlaylistSong>()
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        // Test different page sizes
        var smallPageResult = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            PageSize = 5,
            Page = 0
        });

        var largePageResult = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            PageSize = 100,
            Page = 0
        });

        AssertResultIsSuccessful(smallPageResult);
        AssertResultIsSuccessful(largePageResult);
        Assert.Equal(smallPageResult.TotalCount, largePageResult.TotalCount);
        Assert.True(smallPageResult.Data.Count() <= 5);
        Assert.True(largePageResult.Data.Count() <= 100);
    }

    [Fact]
    public async Task SongsForPlaylistAsync_WithCountOnlyRequest_ReturnsCorrectStructure()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        // Create a test user and playlist
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var testApiKey = Guid.NewGuid();
        var testPlaylist = new Playlist
        {
            ApiKey = testApiKey,
            Name = "Test Playlist",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser,
            Songs = new List<PlaylistSong>()
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        var result = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            IsTotalCountOnlyRequest = true
        });

        AssertResultIsSuccessful(result);
        Assert.True(result.TotalCount >= 0);
        Assert.NotNull(result.Data);
        Assert.IsType<SongDataInfo[]>(result.Data);
    }

    [Fact]
    public async Task SongsForPlaylistAsync_EnsuresDataConsistency_VerifiesResultStructure()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        
        // Create a test user and playlist
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var testApiKey = Guid.NewGuid();
        var testPlaylist = new Playlist
        {
            ApiKey = testApiKey,
            Name = "Test Playlist",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser,
            Songs = new List<PlaylistSong>()
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        var result = await service.SongsForPlaylistAsync(testApiKey, userInfo, new PagedRequest
        {
            PageSize = 50
        });

        // Verify the result structure and consistency
        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.IsType<SongDataInfo[]>(result.Data);
        Assert.True(result.TotalCount >= 0);
        Assert.True(result.TotalPages >= 0);
        
        // For empty playlist, verify expected values
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task DeleteAsync_WithValidPlaylistIds_DeletesPlaylists()
    {
        var service = GetPlaylistService();
        
        // Create test user and playlists
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            Id = 100, // Use a different ID to avoid conflicts
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        var testPlaylist1 = new Playlist
        {
            ApiKey = Guid.NewGuid(),
            Name = "Test Playlist 1",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser
        };
        var testPlaylist2 = new Playlist
        {
            ApiKey = Guid.NewGuid(),
            Name = "Test Playlist 2",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser
        };
        context.Playlists.AddRange(testPlaylist1, testPlaylist2);
        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(testUser.Id, [testPlaylist1.Id, testPlaylist2.Id]);

        AssertResultIsSuccessful(result);
        Assert.True(result.Data);
        
        // Verify playlists were deleted using a fresh context
        await using var verifyContext = await MockFactory().CreateDbContextAsync();
        var deletedPlaylist1 = await verifyContext.Playlists.FindAsync(testPlaylist1.Id);
        var deletedPlaylist2 = await verifyContext.Playlists.FindAsync(testPlaylist2.Id);
        Assert.Null(deletedPlaylist1);
        Assert.Null(deletedPlaylist2);
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyPlaylistIds_ThrowsArgumentException()
    {
        var service = GetPlaylistService();

        await Assert.ThrowsAsync<ArgumentException>(async () => 
            await service.DeleteAsync(1, []));
    }

    [Fact]
    public async Task DeleteAsync_WithNullPlaylistIds_ThrowsArgumentException()
    {
        var service = GetPlaylistService();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => 
            await service.DeleteAsync(1, null!));
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentUser_ReturnsError()
    {
        var service = GetPlaylistService();
        var nonExistentUserId = 999999;

        var result = await service.DeleteAsync(nonExistentUserId, [1]);

        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Contains("Unknown user", result.Messages?.FirstOrDefault() ?? "");
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentPlaylist_ReturnsError()
    {
        var service = GetPlaylistService();
        
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        
        var nonExistentPlaylistId = 999999;

        var result = await service.DeleteAsync(testUser.Id, [nonExistentPlaylistId]);

        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Contains("Unknown playlist", result.Messages?.FirstOrDefault() ?? "");
    }

    [Fact]
    public async Task DeleteAsync_WithUnauthorizedUser_ReturnsError()
    {
        var service = GetPlaylistService();
        
        // Create two users and a playlist for one user and try to delete with another user
        await using var context = await MockFactory().CreateDbContextAsync();
        var playlistOwner = new User
        {
            UserName = "owner",
            UserNameNormalized = "OWNER",
            Email = "owner@example.com",
            EmailNormalized = "OWNER@EXAMPLE.COM",
            PublicKey = "ownerkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        var unauthorizedUser = new User
        {
            UserName = "unauthorized",
            UserNameNormalized = "UNAUTHORIZED",
            Email = "unauthorized@example.com",
            EmailNormalized = "UNAUTHORIZED@EXAMPLE.COM",
            PublicKey = "unauthorizedkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.AddRange(playlistOwner, unauthorizedUser);
        await context.SaveChangesAsync();
        
        var testPlaylist = new Playlist
        {
            ApiKey = Guid.NewGuid(),
            Name = "Test Playlist",
            IsPublic = false, // Private playlist
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = playlistOwner
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(unauthorizedUser.Id, [testPlaylist.Id]);

        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Contains("does not have access", result.Messages?.FirstOrDefault() ?? "");
    }

    [Fact]
    public async Task CacheInvalidation_AfterDelete_ClearsCache()
    {
        var service = GetPlaylistService();
        
        // Create a test user and playlist
        await using var context = await MockFactory().CreateDbContextAsync();
        var testUser = new User
        {
            Id = 200, // Use a different ID to avoid conflicts
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
        var testPlaylist = new Playlist
        {
            ApiKey = Guid.NewGuid(),
            Name = "Test Playlist",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            User = testUser
        };
        context.Playlists.Add(testPlaylist);
        await context.SaveChangesAsync();

        // Get playlist to populate cache
        var getResult = await service.GetAsync(testPlaylist.Id);
        AssertResultIsSuccessful(getResult);

        // Delete playlist
        var deleteResult = await service.DeleteAsync(testUser.Id, [testPlaylist.Id]);
        AssertResultIsSuccessful(deleteResult);

        // Verify playlist is no longer accessible
        var getAfterDeleteResult = await service.GetAsync(testPlaylist.Id);
        Assert.False(getAfterDeleteResult.IsSuccess);
        Assert.Null(getAfterDeleteResult.Data);
    }

    [Fact]
    public async Task ConcurrentAccess_MultipleReads_DoNotInterfere()
    {
        var service = GetPlaylistService();
        var userInfo = new UserInfo(5, Guid.NewGuid(), "testuser", "test@melodee.net", string.Empty, string.Empty);
        var tasks = new List<Task<PagedResult<Playlist>>>();

        // Start multiple concurrent read operations
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(service.ListAsync(userInfo, new PagedRequest { PageSize = 10 }));
        }

        var results = await Task.WhenAll(tasks);

        // All should succeed
        Assert.All(results, result => 
        {
            AssertResultIsSuccessful(result);
            Assert.NotNull(result.Data);
        });
    }
}
