namespace Melodee.Blazor.Services;

/// <summary>
/// Service to provide the application base URL for components that need it
/// </summary>
public interface IBaseUrlService
{
    /// <summary>
    /// Gets the base URL for the application
    /// </summary>
    /// <returns>The base URL or null if not available</returns>
    string? GetBaseUrl();
}