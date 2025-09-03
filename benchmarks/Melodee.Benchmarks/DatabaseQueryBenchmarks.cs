using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Enums;
using NodaTime;

namespace Melodee.Benchmarks;

/// <summary>
/// Benchmarks for database query operations addressing PERFORMANCE_REVIEW.md requirements
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class DatabaseQueryBenchmarks
{
    private ServiceProvider _serviceProvider = null!;
    private IDbContextFactory<MelodeeDbContext> _contextFactory = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Add in-memory database
        services.AddDbContextFactory<MelodeeDbContext>(options =>
            options.UseInMemoryDatabase($"BenchmarkDb_{Guid.NewGuid()}"));

        _serviceProvider = services.BuildServiceProvider();
        _contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();

        await SeedTestData();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    private static User CreateTestUser(int id, string username, string email)
    {
        return new User
        {
            Id = id,
            ApiKey = Guid.NewGuid(),
            UserName = username,
            UserNameNormalized = username.ToNormalizedString() ?? username.ToUpperInvariant(),
            Email = email,
            EmailNormalized = email.ToNormalizedString() ?? email.ToUpperInvariant(),
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };
    }

    private async Task SeedTestData()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Create test user
        var user = CreateTestUser(1, "benchmarkuser", "benchmark@test.com");
        context.Users.Add(user);

        // Create test library
        var library = new Library
        {
            Id = 1,
            ApiKey = Guid.NewGuid(),
            Name = "Benchmark Library",
            Path = "/benchmark",
            Type = (int)LibraryType.Storage,
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };
        context.Libraries.Add(library);

        // Create test artists (20)
        var artists = new List<Artist>();
        for (int i = 1; i <= 20; i++)
        {
            var artist = new Artist
            {
                Id = i,
                ApiKey = Guid.NewGuid(),
                Name = $"Artist {i}",
                NameNormalized = $"artist{i}",
                LibraryId = library.Id,
                Directory = $"/artist/{i}",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            artists.Add(artist);
            context.Artists.Add(artist);
        }

        // Create test albums (80 - 4 per artist)
        var albums = new List<Album>();
        for (int artistIndex = 0; artistIndex < artists.Count; artistIndex++)
        {
            for (int albumIndex = 1; albumIndex <= 4; albumIndex++)
            {
                var albumId = (artistIndex * 4) + albumIndex;
                var album = new Album
                {
                    Id = albumId,
                    ApiKey = Guid.NewGuid(),
                    Name = $"Album {albumId}",
                    NameNormalized = $"album{albumId}",
                    ArtistId = artists[artistIndex].Id,
                    Directory = $"/artist/{artistIndex + 1}/album/{albumIndex}",
                    ReleaseDate = new LocalDate(2020 + (albumIndex % 5), (albumIndex % 12) + 1, 1),
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                albums.Add(album);
                context.Albums.Add(album);
            }
        }

        // Create test songs (800 - 10 per album)
        var songs = new List<Song>();
        for (int albumIndex = 0; albumIndex < albums.Count; albumIndex++)
        {
            for (int songIndex = 1; songIndex <= 10; songIndex++)
            {
                var songId = (albumIndex * 10) + songIndex;
                var song = new Song
                {
                    Id = songId,
                    ApiKey = Guid.NewGuid(),
                    Title = $"Song {songId}",
                    TitleNormalized = $"song{songId}",
                    AlbumId = albums[albumIndex].Id,
                    SongNumber = songIndex,
                    Duration = 180000 + (songIndex * 1000), // 3+ minutes
                    FileSize = 5 * 1024 * 1024, // 5MB
                    FileName = $"song{songId}.mp3",
                    FileHash = Guid.NewGuid().ToString(),
                    SamplingRate = 44100,
                    BitRate = 320,
                    BitDepth = 16,
                    BPM = 120 + (songIndex % 40),
                    ContentType = "audio/mpeg",
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                songs.Add(song);
                context.Songs.Add(song);
            }
        }

        // Create test playlists (10) - simplified without PlaylistSongs for now
        for (int i = 1; i <= 10; i++)
        {
            var playlist = new Playlist
            {
                Id = i,
                ApiKey = Guid.NewGuid(),
                Name = $"Playlist {i}",
                Comment = $"Test playlist {i}",
                UserId = user.Id,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Playlists.Add(playlist);
        }

        await context.SaveChangesAsync();
    }

    [Benchmark(Baseline = true)]
    [Arguments(10)]
    [Arguments(25)]
    [Arguments(50)]
    public async Task PlaylistQuery_BasicPagination(int pageSize)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var playlists = await context.Playlists
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Take(pageSize)
            .ToListAsync();
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(25)]
    [Arguments(50)]
    public async Task PlaylistQuery_WithIncludes(int pageSize)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var playlists = await context.Playlists
            .Include(p => p.User)
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Take(pageSize)
            .ToListAsync();
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(25)]
    [Arguments(50)]
    public async Task PlaylistQuery_OptimizedProjection(int pageSize)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var playlists = await context.Playlists
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.ApiKey,
                p.Name,
                p.Comment,
                p.SongCount,
                p.Duration
            })
            .ToListAsync();
    }

    [Benchmark]
    [Arguments(50)]
    [Arguments(100)]
    [Arguments(200)]
    public async Task SongQuery_WithMultipleIncludes(int takeCount)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var songs = await context.Songs
            .Include(s => s.Album).ThenInclude(a => a.Artist)
            .AsNoTracking()
            .OrderBy(s => s.Title)
            .Take(takeCount)
            .ToListAsync();
    }

    [Benchmark]
    [Arguments(50)]
    [Arguments(100)]
    [Arguments(200)]
    public async Task SongQuery_OptimizedWithSplitQuery(int takeCount)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var songs = await context.Songs
            .Include(s => s.Album).ThenInclude(a => a.Artist)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(s => s.Title)
            .Take(takeCount)
            .ToListAsync();
    }

    [Benchmark]
    public async Task UnboundedQuery_ToArrayAsync_AllPlaylists()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var playlists = await context.Playlists
            .Include(p => p.User)
            .AsNoTracking()
            .ToArrayAsync(); // This is the problematic pattern we're benchmarking
    }

    [Benchmark]
    public async Task BatchQuery_vs_MultipleQueries()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Simulate N+1 problem - multiple individual queries
        var playlistIds = await context.Playlists
            .AsNoTracking()
            .Select(p => p.Id)
            .Take(5)
            .ToListAsync();

        var results = new List<object>();
        foreach (var playlistId in playlistIds)
        {
            var playlist = await context.Playlists
                .Include(p => p.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == playlistId);
            if (playlist != null) results.Add(playlist);
        }
    }

    [Benchmark]
    public async Task BatchQuery_SingleQuery()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Optimized - single query with includes
        var playlists = await context.Playlists
            .Include(p => p.User)
            .AsNoTracking()
            .Take(5)
            .ToListAsync();
    }

    [Benchmark]
    [Arguments(1, 10)]    // Small page
    [Arguments(1, 25)]    // Medium page  
    [Arguments(1, 50)]    // Large page
    public async Task PaginatedQuery_vs_FullDataset(int page, int pageSize)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var songs = await context.Songs
            .Include(s => s.Album).ThenInclude(a => a.Artist)
            .AsNoTracking()
            .OrderBy(s => s.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(500)]
    public async Task ComplexQuery_MultipleToListAsync(int songCount)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Simulate multiple ToList calls - inefficient pattern
        var songsWithArtistA = await context.Songs
            .Include(s => s.Album).ThenInclude(a => a.Artist)
            .AsNoTracking()
            .Where(s => s.Album.Artist.Name.Contains("Artist"))
            .ToListAsync(); // First ToList

        var filteredSongs = songsWithArtistA
            .Where(s => s.Duration > 180000)
            .ToList(); // Second ToList

        var orderedSongs = filteredSongs
            .OrderBy(s => s.Album.Artist.Name)
            .Take(songCount)
            .ToList(); // Third ToList
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(500)]
    public async Task ComplexQuery_OptimizedSinglePass(int songCount)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Optimized - single query with all conditions
        var songs = await context.Songs
            .Include(s => s.Album).ThenInclude(a => a.Artist)
            .AsNoTracking()
            .Where(s => s.Album.Artist.Name.Contains("Artist"))
            .Where(s => s.Duration > 180000)
            .OrderBy(s => s.Album.Artist.Name)
            .Take(songCount)
            .ToListAsync(); // Single ToListAsync
    }
}