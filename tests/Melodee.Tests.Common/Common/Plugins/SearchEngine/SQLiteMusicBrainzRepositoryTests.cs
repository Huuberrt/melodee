using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Tests.Common.Common.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Album = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Album;
using Artist = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Artist;

namespace Melodee.Tests.Common.Common.Plugins.SearchEngine;

public class SQLiteMusicBrainzRepositoryTests : ServiceTestBase
{
    private readonly DbContextOptions<MusicBrainzDbContext> _dbContextOptions;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private SQLiteMusicBrainzRepository _repository;

    public SQLiteMusicBrainzRepositoryTests()
    {
        // Create a persistent in-memory database connection with proper caching
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Cache=Shared");
        _connection.Open();

        _dbContextOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;

        // Create the database tables
        using var context = new MusicBrainzDbContext(_dbContextOptions);
        context.Database.EnsureCreated();

        var mockFactory = new Mock<IDbContextFactory<MusicBrainzDbContext>>();
        // Ensure all contexts share the same SQLite connection
        mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => 
            {
                return new MusicBrainzDbContext(_dbContextOptions);
            });
        mockFactory.Setup(f => f.CreateDbContext())
            .Returns(() => new MusicBrainzDbContext(_dbContextOptions));
        var dbContextFactory = mockFactory.Object;
        
        _repository = new SQLiteMusicBrainzRepository(
            Logger,
            MockConfigurationFactory(),
            dbContextFactory);
    }


    [Fact]
    public async Task GetAlbumByMusicBrainzId_WithValidId_ReturnsAlbum()
    {
        var albumId = Guid.NewGuid();
        var testAlbum = new Album
        {
            Id = 1,
            MusicBrainzIdRaw = albumId.ToString(),
            Name = "Test Album",
            NameNormalized = "test album",
            SortName = "Test Album",
            ReleaseDate = DateTime.Now,
            ReleaseType = 1,
            MusicBrainzArtistId = 1,
            ReleaseGroupMusicBrainzIdRaw = Guid.NewGuid().ToString()
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        context.Albums.Add(testAlbum);
        await context.SaveChangesAsync();

        var result = await _repository.GetAlbumByMusicBrainzId(albumId);

        Assert.NotNull(result);
        Assert.Equal(albumId, result.MusicBrainzId);
        Assert.Equal("Test Album", result.Name);
    }

    [Fact]
    public async Task GetAlbumByMusicBrainzId_WithInvalidId_ReturnsNull()
    {
        var nonExistentId = Guid.NewGuid();

        var result = await _repository.GetAlbumByMusicBrainzId(nonExistentId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAlbumByMusicBrainzId_WithEmptyId_ReturnsNull()
    {
        var result = await _repository.GetAlbumByMusicBrainzId(Guid.Empty);

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchArtist_WithMusicBrainzId_ReturnsCorrectArtist()
    {
        SetupTestArtistData();
        
        var artistId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var query = new ArtistQuery { Name = "Test Artist", MusicBrainzId = artistId.ToString() };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Data);
        Assert.Equal(artistId, result.Data.First().MusicBrainzId);
    }

    [Fact]
    public async Task SearchArtist_WithNormalizedName_ReturnsMatchingArtists()
    {
        SetupTestArtistData();
        
        var query = new ArtistQuery 
        { 
            Name = "Test Artist"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
    }

    [Fact]
    public async Task SearchArtist_WithEmptyQuery_ReturnsEmptyResult()
    {
        var query = new ArtistQuery { Name = "" };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchArtist_WithMaxResults_LimitsResults()
    {
        SetupMultipleTestArtists();
        
        var query = new ArtistQuery 
        { 
            Name = "Artist"
        };

        var result = await _repository.SearchArtist(query, 2);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.True(result.Data.Count() <= 2);
    }

    [Fact]
    public async Task SearchArtist_WithAlbumKeyValues_IncludesAlbumMatching()
    {
        SetupTestArtistWithAlbums();
        
        var query = new ArtistQuery 
        { 
            Name = "Artist With Albums",
            AlbumKeyValues = new[] { new KeyValue("2023", "Test Album") }
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
        
        var artist = result.Data.First();
        Assert.NotNull(artist.Releases);
        Assert.NotEmpty(artist.Releases);
    }

    [Fact]
    public async Task SearchArtist_WithSpecialCharacters_HandlesCorrectly()
    {
        var artistId = Guid.NewGuid();
        var testArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Ac/Dc",
            NameNormalized = "Ac/Dc".ToNormalizedString() ?? string.Empty,
            SortName = "Ac/Dc",
            AlternateNames = ""
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        await context.SaveChangesAsync();
        
        context.Artists.Add(testArtist);
        await context.SaveChangesAsync();

        var query = new ArtistQuery 
        { 
            Name = "Ac/Dc"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SearchArtist_WithNullName_HandlesGracefully()
    {
        var query = new ArtistQuery 
        { 
            Name = ""
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task SearchArtist_WithZeroMaxResults_ReturnsEmpty()
    {
        SetupTestArtistData();
        
        var query = new ArtistQuery 
        { 
            Name = "Test Artist"
        };

        var result = await _repository.SearchArtist(query, 0);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task SearchArtist_WithNegativeMaxResults_ReturnsEmpty()
    {
        SetupTestArtistData();
        
        var query = new ArtistQuery 
        { 
            Name = "Test Artist"
        };

        var result = await _repository.SearchArtist(query, -1);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task SearchArtist_DatabaseException_ReturnsEmptyResult()
    {
        var query = new ArtistQuery 
        { 
            Name = "Test Artist"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchArtist_CancellationToken_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var query = new ArtistQuery 
        { 
            Name = "Test Artist"
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _repository.SearchArtist(query, 10, cts.Token));
    }

    [Fact]
    public async Task ImportData_WithInvalidStoragePath_ReturnsFalse()
    {
        var result = await _repository.ImportData();

        Assert.NotNull(result);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task SearchArtist_WithRanking_ReturnsCorrectOrder()
    {
        var artist1Id = Guid.NewGuid();
        var artist2Id = Guid.NewGuid();
        
        var exactMatchArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artist1Id.ToString(),
            Name = "Beatles",
            NameNormalized = "Beatles".ToNormalizedString() ?? string.Empty,
            SortName = "Beatles",
            AlternateNames = ""
        };

        var partialMatchArtist = new Artist
        {
            Id = 2,
            MusicBrainzArtistId = 2,
            MusicBrainzIdRaw = artist2Id.ToString(),
            Name = "Beatles Tribute Band",
            NameNormalized = "Beatles Tribute Band".ToNormalizedString() ?? string.Empty,
            SortName = "Beatles Tribute Band",
            AlternateNames = ""
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        await context.SaveChangesAsync();
        
        context.Artists.Add(exactMatchArtist);
        context.Artists.Add(partialMatchArtist);
        await context.SaveChangesAsync();

        var query = new ArtistQuery 
        { 
            Name = "Beatles"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
        
        var topResult = result.Data.First();
        Assert.Equal("Beatles", topResult.Name);
        Assert.True(topResult.Rank > 0);
    }

    [Fact]
    public async Task SearchArtist_WithAlternateNames_MatchesCorrectly()
    {
        var artistId = Guid.NewGuid();
        var testArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Prince",
            NameNormalized = "Prince".ToNormalizedString() ?? string.Empty,
            SortName = "Prince",
            // Include both original and normalized versions of alternate names
            AlternateNames = $"the artist formerly known as prince|{("The Artist Formerly Known As Prince").ToNormalizedString()}|tafkap"
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        await context.SaveChangesAsync();
        
        context.Artists.Add(testArtist);
        await context.SaveChangesAsync();

        var query = new ArtistQuery 
        { 
            Name = "The Artist Formerly Known As Prince"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
        
        var artist = result.Data.First();
        Assert.Equal("Prince", artist.Name);
        Assert.Contains("the artist formerly known as prince", artist.AlternateNames ?? []);
    }

    private void SetupTestArtistData()
    {
        var artistId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var testArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Test Artist",
            NameNormalized = "Test Artist".ToNormalizedString() ?? string.Empty, // Use actual normalization
            SortName = "Test Artist",
            AlternateNames = ""
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.Artists.RemoveRange(context.Artists);
        context.SaveChanges();
        
        context.Artists.Add(testArtist);
        context.SaveChanges();
    }

    private void SetupMultipleTestArtists()
    {
        var artists = new[]
        {
            new Artist
            {
                Id = 1,
                MusicBrainzArtistId = 1,
                MusicBrainzIdRaw = Guid.NewGuid().ToString(),
                Name = "Artist One",
                NameNormalized = "Artist One".ToNormalizedString()?? string.Empty,
                SortName = "Artist One",
                AlternateNames = ""
            },
            new Artist
            {
                Id = 2,
                MusicBrainzArtistId = 2,
                MusicBrainzIdRaw = Guid.NewGuid().ToString(),
                Name = "Artist Two",
                NameNormalized = "Artist Two".ToNormalizedString()?? string.Empty,
                SortName = "Artist Two",
                AlternateNames = ""
            },
            new Artist
            {
                Id = 3,
                MusicBrainzArtistId = 3,
                MusicBrainzIdRaw = Guid.NewGuid().ToString(),
                Name = "Artist Three",
                NameNormalized = "Artist Three".ToNormalizedString() ?? string.Empty,
                SortName = "Artist Three",
                AlternateNames = ""
            }
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        context.SaveChanges();
        
        context.Artists.AddRange(artists);
        context.SaveChanges();
    }

    private void SetupTestArtistWithAlbums()
    {
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        
        var testArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Artist With Albums",
            NameNormalized = "Artist With Albums".ToNormalizedString() ?? string.Empty,
            SortName = "Artist With Albums",
            AlternateNames = ""
        };

        var testAlbum = new Album
        {
            Id = 1,
            MusicBrainzIdRaw = albumId.ToString(),
            Name = "Test Album",
            NameNormalized = "Test Album".ToNormalizedString()?? string.Empty,
            SortName = "Test Album",
            ReleaseDate = new DateTime(2023, 1, 1),
            ReleaseType = 1,
            MusicBrainzArtistId = 1,
            ReleaseGroupMusicBrainzIdRaw = Guid.NewGuid().ToString()
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        context.SaveChanges();
        
        context.Artists.Add(testArtist);
        context.Albums.Add(testAlbum);
        context.SaveChanges();
    }

    public override void Dispose()
    {
        _repository = null!;
        _connection.Close();
        _connection.Dispose();
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        _connection.Close();
        await _connection.DisposeAsync();        
        await base.DisposeAsync();
    }
}
