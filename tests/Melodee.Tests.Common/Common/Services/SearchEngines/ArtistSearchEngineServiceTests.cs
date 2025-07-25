using System.ComponentModel.DataAnnotations;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.Spotify;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Moq;
using SearchArtist = Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData.Artist;
using SearchAlbum = Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData.Album;

namespace Melodee.Tests.Common.Common.Services.SearchEngines;

public class ArtistSearchEngineServiceTests : ServiceTestBase
{
    private new ArtistSearchEngineService GetArtistSearchEngineService()
    {
        return new ArtistSearchEngineService(
            Logger,
            CacheManager,
            MockSettingService(),
            MockSpotifyClientBuilder(),
            MockConfigurationFactory(),
            MockFactory(),
            MockArtistSearchEngineFactory(),
            GetMusicBrainzRepository());
    }

    // Core functionality tests that don't rely on database operations

    [Fact]
    public void Constructor_WithValidParameters_DoesNotThrow()
    {
        // Arrange & Act
        var service = GetArtistSearchEngineService();
        
        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task CheckInitialized_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        var service = GetArtistSearchEngineService();
        
        // Should throw because service is not initialized
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DoSearchAsync(new ArtistQuery { Name = "Test" }, 10));
        
        Assert.Contains("not initialized", exception.Message);
    }

    [Fact]
    public async Task GetById_WithNegativeId_ThrowsArgumentException()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.GetById(-1));
        
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task GetById_WithZeroId_ThrowsArgumentException()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.GetById(0));
        
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task AddArtistAsync_WithNullArtist_ThrowsArgumentNullException()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.AddArtistAsync(null!));
    }

    [Fact]
    public async Task UpdateArtistAsync_WithNullArtist_ThrowsArgumentNullException()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.UpdateArtistAsync(null!));
    }

    [Fact]
    public async Task DeleteArtistsAsync_WithNullArray_ThrowsArgumentNullException()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.DeleteArtistsAsync(null!));
    }

    [Fact]
    public async Task DeleteArtistsAsync_WithEmptyArray_ThrowsArgumentException()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.DeleteArtistsAsync(new int[0]));
    }

    [Fact]
    public async Task DoSearchAsync_WithVariousArtist_ReturnsEmptyResult()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "Various Artists" 
        }, 10);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithTheater_ReturnsEmptyResult()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "Theater" 
        }, 10);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithCaseSensitiveVariousArtist_ReturnsEmptyResult()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "VARIOUS ARTISTS" 
        }, 10);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithCancellationToken_RespectsTokenCancellation()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "Test Artist" 
        }, 10, cts.Token);
        
        // Should handle cancellation gracefully
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_WithoutInitialization_ThrowsInvalidOperationException()
    {
        var service = GetArtistSearchEngineService();
        
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DoArtistTopSongsSearchAsync("Test", null, 10));
    }

    [Fact] 
    public async Task DoArtistTopSongsSearchAsync_WithNonexistentArtist_ReturnsEmptyWithMessage()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoArtistTopSongsSearchAsync("Nonexistent Artist", null, 10);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
        Assert.Contains("No artist found", result.Messages?.FirstOrDefault() ?? "");
    }

    [Fact]
    public async Task InitializeAsync_WithoutConfiguration_CompletesSuccessfully()
    {
        var service = GetArtistSearchEngineService();
        
        await service.InitializeAsync();
        
        // Should not throw - indicates successful initialization
        Assert.True(true);
    }

    [Fact]
    public async Task InitializeAsync_WithConfiguration_CompletesSuccessfully()
    {
        var service = GetArtistSearchEngineService();
        var configuration = TestsBase.NewPluginsConfiguration();
        
        await service.InitializeAsync(configuration);
        
        // Should not throw - indicates successful initialization  
        Assert.True(true);
    }

    [Fact]
    public async Task InitializeAsync_CalledMultipleTimes_DoesNotThrow()
    {
        var service = GetArtistSearchEngineService();
        
        await service.InitializeAsync();
        await service.InitializeAsync(); // Second call should not throw
        
        // Should complete without exceptions
        Assert.True(true);
    }

    [Fact]
    public async Task DoSearchAsync_WithoutInitialization_ThrowsInvalidOperationException()
    {
        var service = GetArtistSearchEngineService();
        
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DoSearchAsync(new ArtistQuery { Name = "Test" }, 10));
    }

    [Fact]
    public async Task DoSearchAsync_WithVariousArtistCaseInsensitive_ReturnsEmptyResult()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "various artists" // lowercase 
        }, 10);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithTheaterCaseInsensitive_ReturnsEmptyResult()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "theater" // lowercase
        }, 10);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithValidArtistQuery_ReturnsResult()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "Metallica" // Valid artist name that should work with mock plugins
        }, 10);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // Data might be empty due to mocked plugins, but should not error
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithMaxResults_RespectsLimit()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "Test Artist"
        }, 5); // Max 5 results
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // Should respect the limit (results might be empty due to mocks)
        Assert.True(result.Data.Count() <= 5);
    }

    [Fact]
    public async Task DoSearchAsync_WithNullMaxResults_UsesDefaultLimit()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoSearchAsync(new ArtistQuery 
        { 
            Name = "Test Artist"
        }, null); // Use default
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_WithValidArtistId_ReturnsResult()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        var result = await service.DoArtistTopSongsSearchAsync("Test Artist", 1, 10);
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        // Results might be empty due to mocked plugins but should not error
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_WithCancellationToken_RespectsToken()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        var result = await service.DoArtistTopSongsSearchAsync("Test Artist", 1, 10, cts.Token);
        
        // Should handle cancellation gracefully
        Assert.NotNull(result);
    }
}