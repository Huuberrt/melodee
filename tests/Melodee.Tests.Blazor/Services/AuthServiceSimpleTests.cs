using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Melodee.Blazor.Services;
using System.Security.Claims;

namespace Melodee.Tests.Blazor.Services;

/// <summary>
/// Simplified AuthService tests focusing on the optimization features
/// </summary>
public class AuthServiceSimpleTests
{
    private readonly Mock<ILocalStorageService> _mockLocalStorage;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly AuthService _authService;
    private const string TestToken = "test-secret-key-that-is-long-enough-for-hmac-sha512-algorithm-requirements-at-least-64-characters";

    public AuthServiceSimpleTests()
    {
        _mockLocalStorage = new Mock<ILocalStorageService>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        // Setup configuration mocks
        var mockTokenSection = new Mock<IConfigurationSection>();
        mockTokenSection.Setup(x => x.Value).Returns(TestToken);
        var mockHoursSection = new Mock<IConfigurationSection>();
        mockHoursSection.Setup(x => x.Value).Returns("24");
        
        _mockConfiguration.Setup(x => x.GetSection("MelodeeAuthSettings:Token")).Returns(mockTokenSection.Object);
        _mockConfiguration.Setup(x => x.GetSection("MelodeeAuthSettings:TokenHours")).Returns(mockHoursSection.Object);

        _authService = new AuthService(_mockLocalStorage.Object, _mockConfiguration.Object);
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenNotValidatedAndNotLoggedIn_CallsGetStateFromToken()
    {
        // Arrange
        _mockLocalStorage.Setup(x => x.GetItemAsStringAsync("melodee_auth_token"))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.EnsureAuthenticatedAsync();

        // Assert
        result.Should().BeFalse();
        _mockLocalStorage.Verify(x => x.GetItemAsStringAsync("melodee_auth_token"), Times.Once);
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenAlreadyValidated_DoesNotCallGetStateFromTokenAgain()
    {
        // Arrange - First call to validate
        _mockLocalStorage.Setup(x => x.GetItemAsStringAsync("melodee_auth_token"))
            .ReturnsAsync((string?)null);
        await _authService.EnsureAuthenticatedAsync();

        // Reset the mock to verify second call behavior
        _mockLocalStorage.Reset();

        // Act - Second call should not validate again
        var result = await _authService.EnsureAuthenticatedAsync();

        // Assert
        result.Should().BeFalse();
        _mockLocalStorage.Verify(x => x.GetItemAsStringAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Login_SetsValidatedFlagToTrue()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test") }, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        await _authService.Login(user);

        // Subsequent call to EnsureAuthenticatedAsync should not validate token
        _mockLocalStorage.Reset();
        var result = await _authService.EnsureAuthenticatedAsync();

        // Assert
        result.Should().BeTrue();
        _mockLocalStorage.Verify(x => x.GetItemAsStringAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAsync_ResetsValidatedFlag()
    {
        // Arrange - First login
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test") }, "jwt");
        var user = new ClaimsPrincipal(identity);
        await _authService.Login(user);

        // Act - Logout
        await _authService.LogoutAsync();

        // Reset mock to check validation happens again
        _mockLocalStorage.Reset();
        _mockLocalStorage.Setup(x => x.GetItemAsStringAsync("melodee_auth_token"))
            .ReturnsAsync((string?)null);

        // Act - Try to ensure authenticated after logout
        var result = await _authService.EnsureAuthenticatedAsync();

        // Assert
        result.Should().BeFalse();
        _authService.IsLoggedIn.Should().BeFalse();
        _mockLocalStorage.Verify(x => x.GetItemAsStringAsync("melodee_auth_token"), Times.Once); // Should validate again
    }

    [Fact]
    public void IsLoggedIn_WithAuthenticatedUser_ReturnsTrue()
    {
        // Arrange
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test") }, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        _authService.CurrentUser = user;

        // Assert
        _authService.IsLoggedIn.Should().BeTrue();
    }

    [Fact]
    public void IsLoggedIn_WithUnauthenticatedUser_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal();

        // Act
        _authService.CurrentUser = user;

        // Assert
        _authService.IsLoggedIn.Should().BeFalse();
    }

    [Fact]
    public void CurrentUser_WhenSet_TriggersUserChangedEvent()
    {
        // Arrange
        ClaimsPrincipal? changedUser = null;
        _authService.UserChanged += user => changedUser = user;

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test") }, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        _authService.CurrentUser = user;

        // Assert
        changedUser.Should().NotBeNull();
        changedUser!.Identity!.Name.Should().Be("test");
    }
}