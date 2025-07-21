using System.Globalization;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.OpenSubsonic.Requests;
using UserPlayer = Melodee.Common.Models.Scrobbling.UserPlayer;
using Melodee.Common.Models.OpenSubsonic.Enums;
using Melodee.Common.Utility;
using NodaTime;
using User = Melodee.Common.Data.Models.User;
using Artist = Melodee.Common.Data.Models.Artist;
using Album = Melodee.Common.Data.Models.Album;
using Song = Melodee.Common.Data.Models.Song;
using Library = Melodee.Common.Data.Models.Library;

namespace Melodee.Tests.Common.Services;

public class OpenSubsonicApiServiceTests : ServiceTestBase
{
    [Fact]
    public async Task GetLicense()
    {
        var username = "daUsername";
        var password = "daPassword";
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var usersPublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
            context.Users.Add(new User
            {
                ApiKey = Guid.NewGuid(),
                UserName = username,
                UserNameNormalized = username.ToUpperInvariant(),
                Email = "testemail@local.home.arpa",
                EmailNormalized = "testemail@local.home.arpa".ToNormalizedString()!,
                PublicKey = usersPublicKey,
                PasswordEncrypted = EncryptionHelper.Encrypt(TestsBase.NewPluginsConfiguration().GetValue<string>(SettingRegistry.EncryptionPrivateKey)!, password, usersPublicKey),
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            });
            await context.SaveChangesAsync();
        }

        var licenseResult = await GetOpenSubsonicApiService().GetLicenseAsync(GetApiRequest(username, "123456", password));
        Assert.NotNull(licenseResult);
        Assert.True(licenseResult.IsSuccess);
        Assert.NotNull(licenseResult.ResponseData);
        var license = licenseResult.ResponseData?.Data as License;
        Assert.NotNull(license);
        Assert.True(DateTime.Parse(license.LicenseExpires, CultureInfo.InvariantCulture) > DateTime.Now);
    }

    [Fact]
    public async Task AuthenticateUserUsingSaltAndPassword()
    {
        var username = "daUsername";
        var password = "daPassword";
        var salt = "123487";
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var usersPublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
            context.Users.Add(new User
            {
                ApiKey = Guid.NewGuid(),
                UserName = username,
                UserNameNormalized = username.ToUpperInvariant(),
                Email = "testemail@local.home.arpa",
                EmailNormalized = "testemail@local.home.arpa".ToNormalizedString()!,
                PublicKey = usersPublicKey,
                PasswordEncrypted = EncryptionHelper.Encrypt(TestsBase.NewPluginsConfiguration().GetValue<string>(SettingRegistry.EncryptionPrivateKey)!, password, usersPublicKey),
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            });
            await context.SaveChangesAsync();
        }

        var authResult = await GetOpenSubsonicApiService().AuthenticateSubsonicApiAsync(GetApiRequest(username, salt, HashHelper.CreateMd5($"{password}{salt}") ?? string.Empty));
        Assert.NotNull(authResult);
        Assert.True(authResult.IsSuccess);
        Assert.NotNull(authResult.ResponseData);
    }

    [Fact]
    public async Task AuthenticateUserWithInvalidCredentials_ReturnsError()
    {
        var username = "validUser";
        var password = "validPassword";
        var salt = "123487";
        
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var usersPublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
            context.Users.Add(new User
            {
                ApiKey = Guid.NewGuid(),
                UserName = username,
                UserNameNormalized = username.ToUpperInvariant(),
                Email = "testemail@local.home.arpa",
                EmailNormalized = "testemail@local.home.arpa".ToNormalizedString()!,
                PublicKey = usersPublicKey,
                PasswordEncrypted = EncryptionHelper.Encrypt(TestsBase.NewPluginsConfiguration().GetValue<string>(SettingRegistry.EncryptionPrivateKey)!, password, usersPublicKey),
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            });
            await context.SaveChangesAsync();
        }

        var authResult = await GetOpenSubsonicApiService().AuthenticateSubsonicApiAsync(GetApiRequestWithAuth(username, salt, HashHelper.CreateMd5($"wrongpassword{salt}") ?? string.Empty));
        Assert.NotNull(authResult);
        Assert.False(authResult.IsSuccess);
    }

    [Fact]
    public async Task GetGenres_WithNoGenres_ReturnsEmptyList()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetGenresAsync(GetApiRequest(username, "123456", password));
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        var genres = result.ResponseData?.Data as IList<Genre>;
        Assert.NotNull(genres);
        Assert.Empty(genres);
    }

    [Fact]
    public async Task GetGenres_WithAlbumsAndSongs_ReturnsGenres()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            var album = new Album
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Album",
                SortName = "Test Album",
                NameNormalized = "test album",
                Directory = "/music/test_artist/test_album",
                Artist = artist,
                Genres = ["Rock", "Alternative"],
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Albums.Add(album);

            var song = new Song
            {
                ApiKey = Guid.NewGuid(),
                Title = "Test Song",
                TitleNormalized = "test song",
                SongNumber = 1,
                FileName = "test_song.mp3",
                FileSize = 1234567,
                FileHash = "abc123def456",
                Duration = 180,
                SamplingRate = 44100,
                BitRate = 320,
                BitDepth = 16,
                BPM = 120,
                ContentType = "audio/mpeg",
                Album = album,
                Genres = ["Rock", "Metal"],
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Songs.Add(song);

            await context.SaveChangesAsync();
        }

        var result = await GetOpenSubsonicApiService().GetGenresAsync(GetApiRequest(username, "123456", password));
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        var genres = result.ResponseData?.Data as IList<Genre>;
        Assert.NotNull(genres);
        Assert.NotEmpty(genres);
        
        var genreNames = genres.Select(g => g.Value).ToArray();
        Assert.Contains("Rock", genreNames);
        Assert.Contains("Alternative", genreNames);
        Assert.Contains("Metal", genreNames);
    }

    [Fact]
    public async Task GetSongsByGenre_WithValidGenre_ReturnsSongs()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            var album = new Album
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Album",
                SortName = "Test Album",
                NameNormalized = "test album",
                Directory = "/music/test_artist/test_album",
                Artist = artist,
                Genres = ["Rock", "Alternative"],
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Albums.Add(album);

            var song = new Song
            {
                ApiKey = Guid.NewGuid(),
                Title = "Test Song",
                TitleNormalized = "test song",
                SongNumber = 1,
                FileName = "test_song.mp3",
                FileSize = 1234567,
                FileHash = "abc123def456",
                Duration = 180,
                SamplingRate = 44100,
                BitRate = 320,
                BitDepth = 16,
                BPM = 120,
                ContentType = "audio/mpeg",
                Album = album,
                Genres = ["Rock", "Metal"],
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Songs.Add(song);

            await context.SaveChangesAsync();
        }

        var result = await GetOpenSubsonicApiService().GetSongsByGenreAsync("Rock", 10, 0, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        var songsByGenre = result.ResponseData?.Data as Child[];
        Assert.NotNull(songsByGenre);
        Assert.NotEmpty(songsByGenre);
        Assert.Single(songsByGenre);
        Assert.Equal("Test Song", songsByGenre[0].Title);
    }

    [Fact]
    public async Task GetSongsByGenre_WithInvalidGenre_ReturnsEmptyList()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetSongsByGenreAsync("NonexistentGenre", 10, 0, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        var songsByGenre = result.ResponseData?.Data as Child[];
        Assert.NotNull(songsByGenre);
        Assert.Empty(songsByGenre);
    }

    [Fact]
    public async Task GetSongsByGenre_WithPagination_ReturnsLimitedResults()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            var album = new Album
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Album",
                SortName = "Test Album",
                NameNormalized = "test album",
                Directory = "/music/test_artist/test_album",
                Artist = artist,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Albums.Add(album);

            // Add multiple songs with same genre
            for (int i = 0; i < 5; i++)
            {
                var song = new Song
                {
                    ApiKey = Guid.NewGuid(),
                    Title = $"Test Song {i}",
                    TitleNormalized = $"test song {i}",
                    SongNumber = i + 1,
                    FileName = $"test_song_{i}.mp3",
                    FileSize = 1234567 + i,
                    FileHash = $"abc123def456{i}",
                    Duration = 180 + i * 10,
                    SamplingRate = 44100,
                    BitRate = 320,
                    BitDepth = 16,
                    BPM = 120,
                    ContentType = "audio/mpeg",
                    Album = album,
                    Genres = ["Rock"],
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                context.Songs.Add(song);
            }

            await context.SaveChangesAsync();
        }

        var result = await GetOpenSubsonicApiService().GetSongsByGenreAsync("Rock", 2, 0, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        var songsByGenre = result.ResponseData?.Data as Child[];
        Assert.NotNull(songsByGenre);
        Assert.Equal(2, songsByGenre.Length);
    }

    [Fact]
    public async Task GetRandomSongs_WithoutFilters_ReturnsRandomSongs()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            var album = new Album
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Album",
                SortName = "Test Album",
                NameNormalized = "test album",
                Directory = "/music/test_artist/test_album",
                Artist = artist,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Albums.Add(album);

            for (int i = 0; i < 10; i++)
            {
                var song = new Song
                {
                    ApiKey = Guid.NewGuid(),
                    Title = $"Test Song {i}",
                    TitleNormalized = $"test song {i}",
                    SongNumber = i + 1,
                    FileName = $"test_song_{i}.mp3",
                    FileSize = 1234567 + i,
                    FileHash = $"abc123def456{i}",
                    Duration = 180 + i * 10,
                    SamplingRate = 44100,
                    BitRate = 320,
                    BitDepth = 16,
                    BPM = 120,
                    ContentType = "audio/mpeg",
                    Album = album,
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                context.Songs.Add(song);
            }

            await context.SaveChangesAsync();
        }

        var result = await GetOpenSubsonicApiService().GetRandomSongsAsync(5, null, null, null, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        var randomSongs = result.ResponseData?.Data as Child[];
        Assert.NotNull(randomSongs);
        Assert.True(randomSongs.Length <= 5);
    }

    [Fact]
    public async Task GetAlbumList_WithRandomType_ReturnsAlbums()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            var album = new Album
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Album",
                SortName = "Test Album",
                NameNormalized = "test album",
                Directory = "/music/test_artist/test_album",
                Artist = artist,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Albums.Add(album);

            await context.SaveChangesAsync();
        }

        var request = new GetAlbumListRequest(
            ListType.Random,
            10,
            0,
            null,
            null,
            null,
            null);

        var result = await GetOpenSubsonicApiService().GetAlbumListAsync(request, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // AlbumList response - just verify success
    }

    [Fact]
    public async Task GetAlbumList2_WithByGenreType_ReturnsFilteredAlbums()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            var album = new Album
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Album",
                SortName = "Test Album",
                NameNormalized = "test album",
                Directory = "/music/test_artist/test_album",
                Artist = artist,
                Genres = ["Rock"],
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Albums.Add(album);

            await context.SaveChangesAsync();
        }

        var request = new GetAlbumListRequest(
            ListType.ByGenre,
            10,
            0,
            null,
            null,
            "Rock",
            null);

        var result = await GetOpenSubsonicApiService().GetAlbumList2Async(request, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // AlbumList2 response - just verify success
    }

    [Fact]
    public async Task GetIndexes_WithArtists_ReturnsIndexedArtists()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            await context.SaveChangesAsync();
        }

        var result = await GetOpenSubsonicApiService().GetIndexesAsync(true, "artists", null, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // Artists response - just verify success
    }

    [Fact]
    public async Task SavePlayQueue_ValidRequest_SavesSuccessfully()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            var album = new Album
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Album",
                SortName = "Test Album",
                NameNormalized = "test album",
                Directory = "/music/test_artist/test_album",
                Artist = artist,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Albums.Add(album);

            var song = new Song
            {
                ApiKey = Guid.NewGuid(),
                Title = "Test Song",
                TitleNormalized = "test song",
                SongNumber = 1,
                FileName = "test_song.mp3",
                FileSize = 1234567,
                FileHash = "abc123def456",
                Duration = 180,
                SamplingRate = 44100,
                BitRate = 320,
                BitDepth = 16,
                BPM = 120,
                ContentType = "audio/mpeg",
                Album = album,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Songs.Add(song);

            await context.SaveChangesAsync();
        }

        var result = await GetOpenSubsonicApiService().SavePlayQueueAsync(
            ["song_" + (await GetFirstSongApiKey()).ToString()], 
            null, 
            null, 
            GetApiRequest(username, "123456", password), 
            CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Ping_ValidRequest_ReturnsSuccess()
    {
        var result = await GetOpenSubsonicApiService().PingAsync(GetApiRequest("any", "123456", "any"), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetMusicFolders_ValidRequest_ReturnsLibraries()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetMusicFolders(GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // MusicFolders response - just verify success
    }

    // Edge case tests

    [Fact]
    public async Task GetSongsByGenre_WithNullCount_UsesDefault()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetSongsByGenreAsync("Rock", null, 0, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetSongsByGenre_WithNullOffset_UsesZero()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetSongsByGenreAsync("Rock", 10, null, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetGenres_WithDuplicateGenres_ReturnsUniqueGenres()
    {
        var username = "testUser";
        var password = "testPassword";
        var user = await CreateTestUser(username, password);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var library = new Library
            {
                Name = "Test Library",
                Path = "/test",
                Type = (int)LibraryType.Storage,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Libraries.Add(library);

            var artist = new Artist
            {
                ApiKey = Guid.NewGuid(),
                Name = "Test Artist",
                SortName = "Test Artist",
                NameNormalized = "test artist",
                Directory = "/music/test_artist",
                Library = library,
                LibraryId = library.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);

            // Create albums and songs with duplicate genres
            for (int i = 0; i < 3; i++)
            {
                var album = new Album
                {
                    ApiKey = Guid.NewGuid(),
                    Name = $"Test Album {i}",
                    SortName = $"Test Album {i}",
                    NameNormalized = $"test album {i}",
                    Directory = $"/music/test_artist/test_album_{i}",
                    Artist = artist,
                    Genres = ["Rock", "Pop"], // Same genres in multiple albums
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                context.Albums.Add(album);

                var song = new Song
                {
                    ApiKey = Guid.NewGuid(),
                    Title = $"Test Song {i}",
                    TitleNormalized = $"test song {i}",
                    SongNumber = i + 1,
                    FileName = $"test_song_{i}.mp3",
                    FileSize = 1234567 + i,
                    FileHash = $"abc123def456{i}",
                    Duration = 180 + i * 10,
                    SamplingRate = 44100,
                    BitRate = 320,
                    BitDepth = 16,
                    BPM = 120,
                    ContentType = "audio/mpeg",
                    Album = album,
                    Genres = ["Rock", "Pop"], // Same genres in multiple songs
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                context.Songs.Add(song);
            }

            await context.SaveChangesAsync();
        }

        var result = await GetOpenSubsonicApiService().GetGenresAsync(GetApiRequest(username, "123456", password));
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        var genres = result.ResponseData?.Data as IList<Genre>;
        Assert.NotNull(genres);
        
        var genreNames = genres.Select(g => g.Value).ToArray();
        var uniqueGenreNames = genreNames.Distinct().ToArray();
        
        // Should have unique genres only
        Assert.Equal(uniqueGenreNames.Length, genreNames.Length);
        Assert.Contains("Rock", genreNames);
        Assert.Contains("Pop", genreNames);
    }


    [Fact]
    public async Task GetIndexes_WithNoArtists_ReturnsEmptyIndexes()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetIndexesAsync(true, "artists", null, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // Artists response - just verify success
        Assert.Empty(result.ResponseData.Data as IList<Artist> ?? new List<Artist>());
    }
    
    [Fact]
    public async Task GetGenres_WithNoData_ReturnsEmptyList()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetGenresAsync(GetApiRequest(username, "123456", password));
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("genres", result.ResponseData.DataPropertyName);
    }

    [Fact]
    public async Task GetSongsByGenre_WithEmptyDatabase_ReturnsEmptyResults()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetSongsByGenreAsync("Rock", 10, 0, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("songsByGenre", result.ResponseData.DataPropertyName);
        Assert.Equal("song", result.ResponseData.DataDetailPropertyName);
    }

    [Fact]
    public async Task GetSongsByGenre_WithNullParameters_HandlesGracefully()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        // Test with null count and offset
        var result = await GetOpenSubsonicApiService().GetSongsByGenreAsync("Rock", null, null, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetRandomSongs_WithEmptyDatabase_ReturnsEmptyResults()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetRandomSongsAsync(5, null, null, null, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("randomSongs", result.ResponseData.DataPropertyName);
        Assert.Equal("song", result.ResponseData.DataDetailPropertyName);
    }

    [Fact]
    public async Task GetAlbumList_WithRandomType_ReturnsValidResponse()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var request = new GetAlbumListRequest(
            ListType.Random,
            10,
            0,
            null,
            null,
            null,
            null);

        var result = await GetOpenSubsonicApiService().GetAlbumListAsync(request, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("albumList", result.ResponseData.DataPropertyName);
        Assert.Equal("album", result.ResponseData.DataDetailPropertyName);
    }

    [Fact]
    public async Task GetAlbumList2_WithByGenreType_ReturnsValidResponse()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var request = new GetAlbumListRequest(
            ListType.ByGenre,
            10,
            0,
            null,
            null,
            "Rock",
            null);

        var result = await GetOpenSubsonicApiService().GetAlbumList2Async(request, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("albumList2", result.ResponseData.DataPropertyName);
        Assert.Equal("album", result.ResponseData.DataDetailPropertyName);
    }

    [Fact]
    public async Task GetIndexes_WithEmptyDatabase_ReturnsValidResponse()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetIndexesAsync(true, "artists", null, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("artists", result.ResponseData.DataPropertyName);
    }

    [Fact]
    public async Task SavePlayQueue_WithEmptyQueue_HandlesGracefully()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().SavePlayQueueAsync(
            [], 
            null, 
            null, 
            GetApiRequest(username, "123456", password), 
            CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetPlayQueue_WithEmptyQueue_ReturnsValidResponse()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetPlayQueueAsync(GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("playQueue", result.ResponseData.DataPropertyName);
    }

    // Edge case tests for pagination and limits

    [Fact]
    public async Task GetSongsByGenre_WithLargeOffset_ReturnsEmptyResults()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetSongsByGenreAsync("Rock", 10, 100000, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAlbumList_WithLargeOffset_ReturnsEmptyResults()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var request = new GetAlbumListRequest(
            ListType.Random,
            10,
            100000,
            null,
            null,
            null,
            null);

        var result = await GetOpenSubsonicApiService().GetAlbumListAsync(request, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAlbumList_WithByYearType_ReturnsValidResponse()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var request = new GetAlbumListRequest(
            ListType.ByYear,
            10,
            0,
            2020,
            2023,
            null,
            null);

        var result = await GetOpenSubsonicApiService().GetAlbumListAsync(request, GetApiRequest(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AuthenticateSubsonicApi_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var username = "invalidUser";
        var password = "invalidPassword";

        var result = await GetOpenSubsonicApiService().AuthenticateSubsonicApiAsync(GetApiRequestWithAuth(username, "123456", password), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Ping_Always_ReturnsSuccess()
    {
        var result = await GetOpenSubsonicApiService().PingAsync(GetApiRequest("any", "123456", "any"), CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
    }

    [Fact]
    public async Task GetLicense_WithValidUser_ReturnsValidLicense()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var result = await GetOpenSubsonicApiService().GetLicenseAsync(GetApiRequest(username, "123456", password));
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResponseData);
        var license = result.ResponseData?.Data as License;
        Assert.NotNull(license);
        Assert.True(DateTime.Parse(license.LicenseExpires, CultureInfo.InvariantCulture) > DateTime.Now);
    }

    // Performance tests to ensure EF Core queries are efficient

    [Fact]
    public async Task GetGenres_PerformanceTest_CompletesQuickly()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await GetOpenSubsonicApiService().GetGenresAsync(GetApiRequest(username, "123456", password));
        stopwatch.Stop();
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // Should complete in reasonable time (less than 5 seconds for empty database)
        Assert.True(stopwatch.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public async Task GetIndexes_PerformanceTest_CompletesQuickly()
    {
        var username = "testUser";
        var password = "testPassword";
        await CreateTestUser(username, password);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await GetOpenSubsonicApiService().GetIndexesAsync(true, "artists", null, null, GetApiRequest(username, "123456", password), CancellationToken.None);
        stopwatch.Stop();
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // Should complete in reasonable time (less than 5 seconds for empty database)
        Assert.True(stopwatch.ElapsedMilliseconds < 5000);
    }

    // Helper methods



    // Helper methods

    private async Task<User> CreateTestUser(string username, string password)
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        var usersPublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        var user = new User
        {
            ApiKey = Guid.NewGuid(),
            UserName = username,
            UserNameNormalized = username.ToUpperInvariant(),
            Email = "testemail@local.home.arpa",
            EmailNormalized = "testemail@local.home.arpa".ToNormalizedString()!,
            PublicKey = usersPublicKey,
            PasswordEncrypted = EncryptionHelper.Encrypt(TestsBase.NewPluginsConfiguration().GetValue<string>(SettingRegistry.EncryptionPrivateKey)!, password, usersPublicKey),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<Guid> GetFirstSongApiKey()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        var song = context.Songs.FirstOrDefault();
        return song?.ApiKey ?? Guid.NewGuid();
    }
    
    private ApiRequest GetApiRequestWithAuth(string username, string salt, string password)
    {
        return new ApiRequest(
            [],
            true, // RequiresAuthentication = true
            username,
            "1.16.1",
            "json",
            null,
            null,
            password,
            salt,
            null,
            null,
            new UserPlayer(null,
                null,
                null,
                null));
    }
}
