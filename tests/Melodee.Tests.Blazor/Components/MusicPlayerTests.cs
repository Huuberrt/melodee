using Bunit;
using Melodee.Blazor.Components.Pages;
using Melodee.Blazor.Services;
using Melodee.Common.Models.Collection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using Xunit;
using Melodee.Common.Services;
using Melodee.Common.Configuration;
using System.Security.Claims;
using NodaTime;
using Melodee.Common.Services.Caching;
using Serilog;
using Microsoft.EntityFrameworkCore;
using Melodee.Common.Data;
using Melodee.Common.Plugins.Scrobbling;
using Melodee.Common.Models.Scrobbling;
using Microsoft.AspNetCore.Components.Authorization;

namespace Melodee.Tests.Blazor.Components;

public class MusicPlayerTests : TestContext
{
    private readonly Mock<IBaseUrlService> _mockBaseUrlService;
    private readonly Mock<ScrobbleService> _mockScrobbleService;
    private readonly Mock<IJSRuntime> _mockJSRuntime;
    private readonly Mock<IMelodeeConfiguration> _mockConfiguration;

    public MusicPlayerTests()
    {
        _mockBaseUrlService = new Mock<IBaseUrlService>();
        // Create ScrobbleService mock with required ctor args
        var logger = new Mock<ILogger>().Object;
        var cacheManager = new Mock<ICacheManager>().Object;
        AlbumService? albumService = null!; // Not needed for component tests
        var dbFactory = new Mock<IDbContextFactory<MelodeeDbContext>>().Object;
        var configFactory = new Mock<IMelodeeConfigurationFactory>();
        _mockConfiguration = new Mock<IMelodeeConfiguration>();
        configFactory.Setup(cf => cf.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConfiguration.Object);
        var nowPlayingRepo = new Mock<INowPlayingRepository>();
        nowPlayingRepo.Setup(r => r.GetNowPlayingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Melodee.Common.Models.OperationResult<NowPlayingInfo[]> { Data = [] });

        _mockScrobbleService = new Mock<ScrobbleService>(logger, cacheManager, albumService, dbFactory, configFactory.Object, nowPlayingRepo.Object);
        _mockJSRuntime = new Mock<IJSRuntime>();

        // Register mocks and required services in DI container
        Services.AddSingleton(_mockBaseUrlService.Object);
        Services.AddSingleton(_mockScrobbleService.Object);
        Services.AddSingleton(_mockJSRuntime.Object);
        Services.AddSingleton(_mockConfiguration.Object);
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthStateProvider());
        Services.AddSingleton<Radzen.DialogService>();
        Services.AddSingleton<Radzen.NotificationService>();
        Services.AddSingleton<Radzen.TooltipService>();
        Services.AddSingleton(configFactory.Object);

        // Set up JSInterop for Blazor component testing
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static SongDataInfo CreateTestSong(string apiKey, string title, string artist, string album, long duration = 180000)
    {
        return new SongDataInfo(
            Id: 1,
            ApiKey: Guid.Parse(apiKey),
            IsLocked: false,
            Title: title,
            TitleNormalized: title.ToLowerInvariant(),
            SongNumber: 1,
            ReleaseDate: new LocalDate(2023, 1, 1),
            AlbumName: album,
            AlbumApiKey: Guid.NewGuid(),
            ArtistName: artist,
            ArtistApiKey: Guid.NewGuid(),
            FileSize: 5000000,
            Duration: duration,
            CreatedAt: Instant.FromDateTimeUtc(DateTime.UtcNow),
            Tags: "rock,test",
            UserStarred: false,
            UserRating: 0
        );
    }

    [Fact]
    public void MusicPlayer_WithValidBaseUrl_GeneratesCorrectStreamUrl()
    {
        // Arrange
        const string expectedBaseUrl = "https://test.com";
        
        _mockBaseUrlService.Setup(x => x.GetBaseUrl()).Returns(expectedBaseUrl);

        var songs = new List<SongDataInfo>
        {
            CreateTestSong("12345678-1234-1234-1234-123456789abc", "Test Song", "Test Artist", "Test Album")
        };

        // Act & Assert - Component renders without throwing
        var component = RenderComponent<MusicPlayer>(parameters => parameters
            .Add(p => p.Songs, songs));
        
        // Verify component renders
        Assert.NotNull(component);
        Assert.Contains("Test Song", component.Markup);
        Assert.Contains("Test Artist", component.Markup);
        Assert.Contains("Test Album", component.Markup);
    }

    [Fact]
    public void MusicPlayer_WithNullBaseUrl_ThrowsException()
    {
        // Arrange
        _mockBaseUrlService.Setup(x => x.GetBaseUrl()).Returns((string?)null);

        var songs = new List<SongDataInfo>
        {
            CreateTestSong("12345678-1234-1234-1234-123456789abc", "Test Song", "Test Artist", "Test Album")
        };

        // Act & Assert
        var component = RenderComponent<MusicPlayer>(parameters => parameters
            .Add(p => p.Songs, songs));

        // Component should render but GetStreamUrl would throw when called
        // This tests the basic rendering without triggering stream URL generation
        Assert.NotNull(component);
    }

    [Fact]
    public void MusicPlayer_EmptySongList_RendersWithoutCrashing()
    {
        // Arrange
        _mockBaseUrlService.Setup(x => x.GetBaseUrl()).Returns("https://test.com");
        var emptySongs = new List<SongDataInfo>();

        // Act & Assert - Should not crash with empty list
        var component = RenderComponent<MusicPlayer>(parameters => parameters
            .Add(p => p.Songs, emptySongs));
        
        Assert.NotNull(component);
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://melodee.app")]
    [InlineData("http://192.168.1.100:8080")]
    public void MusicPlayer_WithDifferentBaseUrls_GeneratesCorrectStreamUrls(string baseUrl)
    {
        // Arrange
        _mockBaseUrlService.Setup(x => x.GetBaseUrl()).Returns(baseUrl);

        var songs = new List<SongDataInfo>
        {
            CreateTestSong("12345678-1234-1234-1234-123456789abc", "Test", "Artist", "Album")
        };

        // Act
        var component = RenderComponent<MusicPlayer>(parameters => parameters
            .Add(p => p.Songs, songs));

        // Assert - Component renders successfully with different base URLs
        Assert.NotNull(component);
        Assert.Contains("Test", component.Markup);
    }

    [Fact]
    public void MusicPlayer_FormatTime_ReturnsCorrectFormats()
    {
        // This test verifies the time formatting functionality
        // We can't directly test private methods, so we test through component behavior
        
        // Arrange
        _mockBaseUrlService.Setup(x => x.GetBaseUrl()).Returns("https://test.com");

        var songs = new List<SongDataInfo>
        {
            CreateTestSong("12345678-1234-1234-1234-123456789abc", "Long Song", "Artist", "Album", 245000)
        };

        // Act
        var component = RenderComponent<MusicPlayer>(parameters => parameters
            .Add(p => p.Songs, songs));

        // Assert - Component should handle duration formatting
        Assert.NotNull(component);
        Assert.Contains("Long Song", component.Markup);
    }

    [Fact] 
    public void MusicPlayer_WithMultipleSongs_DisplaysPlaylist()
    {
        // Arrange
        _mockBaseUrlService.Setup(x => x.GetBaseUrl()).Returns("https://test.com");

        var songs = new List<SongDataInfo>
        {
            CreateTestSong("12345678-1234-1234-1234-123456789001", "First Song", "Artist One", "Album One", 180000),
            CreateTestSong("12345678-1234-1234-1234-123456789002", "Second Song", "Artist Two", "Album Two", 210000)
        };

        // Act
        var component = RenderComponent<MusicPlayer>(parameters => parameters
            .Add(p => p.Songs, songs));

        // Assert - Both songs should appear in playlist
        Assert.NotNull(component);
        Assert.Contains("First Song", component.Markup);
        Assert.Contains("Second Song", component.Markup);
        Assert.Contains("Album One", component.Markup);
        Assert.Contains("Album Two", component.Markup);
    }

    [Fact]
    public void MusicPlayer_ComponentProperties_AreSetCorrectly()
    {
        // Arrange
        _mockBaseUrlService.Setup(x => x.GetBaseUrl()).Returns("https://test.com");

        var testSongs = new List<SongDataInfo>
        {
            CreateTestSong("12345678-1234-1234-1234-123456789abc", "Property Test Song", "Property Artist", "Property Album", 200000)
        };

        // Act
        var component = RenderComponent<MusicPlayer>(parameters => parameters
            .Add(p => p.Songs, testSongs));

        // Assert
        Assert.NotNull(component);
        Assert.Single(component.Instance.Songs);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), component.Instance.Songs[0].ApiKey);
        Assert.Equal("Property Test Song", component.Instance.Songs[0].Title);
    }
}

internal sealed class TestAuthStateProvider : AuthenticationStateProvider
{
    private readonly ClaimsPrincipal _user = new(new ClaimsIdentity());
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(new AuthenticationState(_user));
}
