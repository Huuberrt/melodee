using Melodee.Blazor.Filters;
using Xunit;

namespace Melodee.Tests.Blazor.Filters;

public class EtagRepositoryTests
{
    private readonly EtagRepository _etagRepository;

    public EtagRepositoryTests()
    {
        _etagRepository = new EtagRepository();
    }

    #region Baseline Tests - Current Behavior

    [Fact]
    public void AddEtag_WithValidApiKeyIdAndEtag_ReturnsTrue()
    {
        // Arrange - Baseline test
        const string apiKeyId = "test-api-key";
        const string etag = "test-etag";

        // Act
        var result = _etagRepository.AddEtag(apiKeyId, etag);

        // Assert - Capture current behavior
        Assert.True(result);
    }

    [Fact]
    public void AddEtag_WithNullApiKeyId_ReturnsFalse()
    {
        // Arrange - Test fixed guard logic
        string? apiKeyId = null;
        const string etag = "test-etag";

        // Act
        var result = _etagRepository.AddEtag(apiKeyId, etag);

        // Assert - Fixed behavior with AND logic
        Assert.False(result);
    }

    [Fact]
    public void AddEtag_WithNullEtag_ReturnsFalse()
    {
        // Arrange - Test fixed guard logic
        const string apiKeyId = "test-api-key";
        string? etag = null;

        // Act
        var result = _etagRepository.AddEtag(apiKeyId, etag);

        // Assert - Fixed behavior with AND logic  
        Assert.False(result);
    }

    [Fact]
    public void AddEtag_WithBothNullValues_ReturnsFalse()
    {
        // Arrange - Test fixed guard logic
        string? apiKeyId = null;
        string? etag = null;

        // Act
        var result = _etagRepository.AddEtag(apiKeyId, etag);

        // Assert - Both null should return false
        Assert.False(result);
    }

    [Fact]
    public void EtagMatch_WithValidApiKeyIdAndEtag_ReturnsTrue()
    {
        // Arrange - Baseline test
        const string apiKeyId = "test-api-key";
        const string etag = "test-etag";

        _etagRepository.AddEtag(apiKeyId, etag);

        // Act
        var result = _etagRepository.EtagMatch(apiKeyId, etag);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EtagMatch_WithNullApiKeyId_ReturnsFalse()
    {
        // Arrange - Baseline test for current guard logic
        string? apiKeyId = null;
        const string etag = "test-etag";

        // Act
        var result = _etagRepository.EtagMatch(apiKeyId, etag);

        // Assert - Current behavior with OR logic
        Assert.False(result);
    }

    [Fact]
    public void AddEtag_WithManyEntries_RespectsMaxLimit()
    {
        // Arrange - Test eviction policy with small limit
        var repository = new EtagRepository(maxEntries: 50);
        const int entryCount = 100;

        // Act - Add more entries than the limit
        for (int i = 0; i < entryCount; i++)
        {
            var result = repository.AddEtag($"api-key-{i}", $"etag-{i}");
            Assert.True(result);
        }

        // Force cleanup to ensure eviction happens
        repository.ForceCleanup();
        
        // Assert - Should not exceed the limit significantly
        Assert.True(repository.CurrentCount <= 50);
    }

    [Fact]
    public void EtagMatch_WithExpiredEntry_ReturnsFalse()
    {
        // Arrange - Create repository with very short expiry
        var repository = new EtagRepository(entryMaxAge: TimeSpan.FromMilliseconds(10));
        const string apiKeyId = "test-key";
        const string etag = "test-etag";
        
        // Add entry and verify it exists
        Assert.True(repository.AddEtag(apiKeyId, etag));
        Assert.True(repository.EtagMatch(apiKeyId, etag));

        // Act - Wait for expiry
        Thread.Sleep(20);

        // Assert - Should return false for expired entry
        var result = repository.EtagMatch(apiKeyId, etag);
        Assert.False(result);
    }

    [Fact]
    public void AddEtag_WithValidConfiguration_RespectsSettings()
    {
        // Arrange
        var repository = new EtagRepository(maxEntries: 5, entryMaxAge: TimeSpan.FromHours(1));
        
        // Act & Assert - Should work with custom configuration
        Assert.True(repository.AddEtag("key1", "etag1"));
        Assert.True(repository.EtagMatch("key1", "etag1"));
        Assert.Equal(1, repository.CurrentCount);
    }

    #endregion
}