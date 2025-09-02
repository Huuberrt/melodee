using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;

namespace Melodee.Blazor.Services;

/// <summary>
/// Service to provide the application base URL for components that need it
/// </summary>
public class BaseUrlService : IBaseUrlService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMelodeeConfigurationFactory _configurationFactory;

    public BaseUrlService(IHttpContextAccessor httpContextAccessor, IMelodeeConfigurationFactory configurationFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _configurationFactory = configurationFactory;
    }

    public string? GetBaseUrl()
    {
        var configuration = _configurationFactory.GetConfigurationAsync().GetAwaiter().GetResult();
        var configuredBaseUrl = configuration.GetValue<string>(SettingRegistry.SystemBaseUrl);
        
        // If configuration is valid, use it
        if (configuredBaseUrl.Nullify() != null && configuredBaseUrl != MelodeeConfiguration.RequiredNotSetValue)
        {
            return configuredBaseUrl!.TrimEnd('/');
        }
        
        // Try to get from HttpContextAccessor
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            return $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";
        }
        
        // No base URL available
        return null;
    }
}