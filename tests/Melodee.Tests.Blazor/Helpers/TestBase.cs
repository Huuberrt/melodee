using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Melodee.Blazor.Services;
using Melodee.Common.Services;
using Melodee.Common.Configuration;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Radzen;

namespace Melodee.Tests.Blazor.Helpers;

/// <summary>
/// Base class for Blazor component tests with common setup and mocks
/// </summary>
public abstract class TestBase : TestContext, IDisposable
{
    protected Mock<IAuthService> MockAuthService { get; }
    protected Mock<ILocalStorageService> MockLocalStorageService { get; }
    protected Mock<IConfiguration> MockConfiguration { get; }
    protected Mock<UserService> MockUserService { get; }
    protected Mock<LibraryService> MockLibraryService { get; }
    protected Mock<IMelodeeConfigurationFactory> MockConfigurationFactory { get; }
    protected Mock<NotificationService> MockNotificationService { get; }

    protected TestBase()
    {
        // Initialize mocks
        MockAuthService = new Mock<IAuthService>();
        MockLocalStorageService = new Mock<ILocalStorageService>();
        MockConfiguration = new Mock<IConfiguration>();
        MockUserService = new Mock<UserService>();
        MockLibraryService = new Mock<LibraryService>();
        MockConfigurationFactory = new Mock<IMelodeeConfigurationFactory>();
        MockNotificationService = new Mock<NotificationService>();

        // Setup default mock behaviors
        SetupDefaultMocks();

        // Register services
        RegisterServices();
    }

    private void SetupDefaultMocks()
    {
        // Default AuthService behavior - not logged in
        MockAuthService.Setup(x => x.IsLoggedIn).Returns(false);
        MockAuthService.Setup(x => x.IsAdmin).Returns(false);
        MockAuthService.Setup(x => x.CurrentUser).Returns(new ClaimsPrincipal());
        MockAuthService.Setup(x => x.EnsureAuthenticatedAsync()).ReturnsAsync(false);
        MockAuthService.Setup(x => x.GetStateFromTokenAsync()).ReturnsAsync(false);

        // Default configuration values
        var mockConfigSection = new Mock<IConfigurationSection>();
        mockConfigSection.Setup(x => x.Value).Returns("test-key");
        MockConfiguration.Setup(x => x.GetSection(It.IsAny<string>())).Returns(mockConfigSection.Object);

        // Default local storage - empty
        MockLocalStorageService.Setup(x => x.GetItemAsStringAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);
    }

    private void RegisterServices()
    {
        Services.AddSingleton(MockAuthService.Object);
        Services.AddSingleton(MockLocalStorageService.Object);
        Services.AddSingleton(MockConfiguration.Object);
        Services.AddSingleton(MockUserService.Object);
        Services.AddSingleton(MockLibraryService.Object);
        Services.AddSingleton(MockConfigurationFactory.Object);
        Services.AddSingleton(MockNotificationService.Object);

        // Add required Blazor services
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(sp => 
            new Mock<AuthenticationStateProvider>().Object);
    }

    /// <summary>
    /// Setup authenticated user for tests
    /// </summary>
    protected void SetupAuthenticatedUser(bool isAdmin = false)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.NameIdentifier, "123"),
        }, "test");

        if (isAdmin)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, "Administrator"));
        }

        var user = new ClaimsPrincipal(identity);

        MockAuthService.Setup(x => x.IsLoggedIn).Returns(true);
        MockAuthService.Setup(x => x.IsAdmin).Returns(isAdmin);
        MockAuthService.Setup(x => x.CurrentUser).Returns(user);
        MockAuthService.Setup(x => x.EnsureAuthenticatedAsync()).ReturnsAsync(true);
        MockAuthService.Setup(x => x.GetStateFromTokenAsync()).ReturnsAsync(true);
    }

    /// <summary>
    /// Setup unauthenticated user for tests
    /// </summary>
    protected void SetupUnauthenticatedUser()
    {
        MockAuthService.Setup(x => x.IsLoggedIn).Returns(false);
        MockAuthService.Setup(x => x.IsAdmin).Returns(false);
        MockAuthService.Setup(x => x.CurrentUser).Returns(new ClaimsPrincipal());
        MockAuthService.Setup(x => x.EnsureAuthenticatedAsync()).ReturnsAsync(false);
        MockAuthService.Setup(x => x.GetStateFromTokenAsync()).ReturnsAsync(false);
    }

    public new void Dispose()
    {
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}