using Melodee.Blazor.Services;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Melodee.Tests.Blazor.Services;

public class BaseUrlServiceTests
{
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly Mock<IMelodeeConfigurationFactory> _mockConfigurationFactory;
    private readonly Mock<IMelodeeConfiguration> _mockConfiguration;
    private readonly BaseUrlService _service;

    public BaseUrlServiceTests()
    {
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _mockConfigurationFactory = new Mock<IMelodeeConfigurationFactory>();
        _mockConfiguration = new Mock<IMelodeeConfiguration>();
        
        _mockConfigurationFactory.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockConfiguration.Object);
            
        _service = new BaseUrlService(_mockHttpContextAccessor.Object, _mockConfigurationFactory.Object);
    }

    [Fact]
    public void GetBaseUrl_WithValidConfiguration_ReturnsConfiguredUrl()
    {
        // Arrange
        const string expectedUrl = "https://example.com";
        _mockConfiguration.Setup(x => x.GetValue<string>(SettingRegistry.SystemBaseUrl, null))
            .Returns(expectedUrl);

        // Act
        var result = _service.GetBaseUrl();

        // Assert
        Assert.Equal(expectedUrl, result);
    }

    [Fact]
    public void GetBaseUrl_WithValidConfigurationTrailingSlash_ReturnsUrlWithoutTrailingSlash()
    {
        // Arrange
        const string configuredUrl = "https://example.com/";
        const string expectedUrl = "https://example.com";
        _mockConfiguration.Setup(x => x.GetValue<string>(SettingRegistry.SystemBaseUrl, null))
            .Returns(configuredUrl);

        // Act
        var result = _service.GetBaseUrl();

        // Assert
        Assert.Equal(expectedUrl, result);
    }

    [Fact]
    public void GetBaseUrl_WithRequiredNotSetValue_FallsBackToHttpContext()
    {
        // Arrange
        _mockConfiguration.Setup(x => x.GetValue<string>(SettingRegistry.SystemBaseUrl, null))
            .Returns(MelodeeConfiguration.RequiredNotSetValue);

        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(x => x.Scheme).Returns("https");
        mockRequest.Setup(x => x.Host).Returns(new HostString("localhost:5000"));
        mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);

        // Act
        var result = _service.GetBaseUrl();

        // Assert
        Assert.Equal("https://localhost:5000", result);
    }

    [Fact]
    public void GetBaseUrl_WithNullConfiguration_FallsBackToHttpContext()
    {
        // Arrange
        _mockConfiguration.Setup(x => x.GetValue<string>(SettingRegistry.SystemBaseUrl, null))
            .Returns((string?)null);

        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(x => x.Scheme).Returns("http");
        mockRequest.Setup(x => x.Host).Returns(new HostString("api.example.com"));
        mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);

        // Act
        var result = _service.GetBaseUrl();

        // Assert
        Assert.Equal("http://api.example.com", result);
    }

    [Fact]
    public void GetBaseUrl_WithEmptyConfiguration_FallsBackToHttpContext()
    {
        // Arrange
        _mockConfiguration.Setup(x => x.GetValue<string>(SettingRegistry.SystemBaseUrl, null))
            .Returns(string.Empty);

        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(x => x.Scheme).Returns("https");
        mockRequest.Setup(x => x.Host).Returns(new HostString("melodee.app"));
        mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);

        // Act
        var result = _service.GetBaseUrl();

        // Assert
        Assert.Equal("https://melodee.app", result);
    }

    [Fact]
    public void GetBaseUrl_WithInvalidConfigurationAndNoHttpContext_ReturnsNull()
    {
        // Arrange
        _mockConfiguration.Setup(x => x.GetValue<string>(SettingRegistry.SystemBaseUrl, null))
            .Returns(MelodeeConfiguration.RequiredNotSetValue);
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        // Act
        var result = _service.GetBaseUrl();

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GetBaseUrl_WithInvalidConfigurationValues_FallsBackToHttpContext(string? invalidValue)
    {
        // Arrange
        _mockConfiguration.Setup(x => x.GetValue<string>(SettingRegistry.SystemBaseUrl, null))
            .Returns(invalidValue);

        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(x => x.Scheme).Returns("https");
        mockRequest.Setup(x => x.Host).Returns(new HostString("fallback.com"));
        mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);

        // Act
        var result = _service.GetBaseUrl();

        // Assert
        Assert.Equal("https://fallback.com", result);
    }
}