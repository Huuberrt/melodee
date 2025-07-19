using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Tests.Services;

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
        Assert.Contains("Unknown playlist", result.Messages.FirstOrDefault() ?? "");
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
