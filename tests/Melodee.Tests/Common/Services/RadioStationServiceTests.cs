using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using NodaTime;

namespace Melodee.Tests.Common.Services;

public class RadioStationServiceTests : ServiceTestBase
{
    private RadioStation CreateValidRadioStation(string name = "Test Station", string streamUrl = "http://stream.example.com/radio")
    {
        return new RadioStation
        {
            Name = name,
            StreamUrl = streamUrl,
            HomePageUrl = "http://example.com",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    private async Task<User> CreateTestUserAsync(bool isAdmin = false)
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        var user = new User
        {
            UserName = "testuser",
            UserNameNormalized = "testuser",
            Email = "test@example.com",
            EmailNormalized = "test@example.com",
            PublicKey = "publickey",
            PasswordEncrypted = "password",
            IsAdmin = isAdmin,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_ShouldReturnEmptyResult_WhenNoRadioStations()
    {
        var service = GetRadioStationService();
        var pagedRequest = new PagedRequest { PageSize = 10 };

        var result = await service.ListAsync(pagedRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnCorrectPagination_WhenMultipleRadioStations()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        // Create test radio stations
        var stations = new[]
        {
            CreateValidRadioStation("Station 1", "http://stream1.com"),
            CreateValidRadioStation("Station 2", "http://stream2.com"),
            CreateValidRadioStation("Station 3", "http://stream3.com")
        };
        
        context.RadioStations.AddRange(stations);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var pagedRequest = new PagedRequest { PageSize = 2, Page = 1 };

        var result = await service.ListAsync(pagedRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Data.Count());
    }

    [Fact]
    public async Task ListAsync_ShouldReturnTotalCountOnly_WhenTotalCountOnlyRequest()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var station = CreateValidRadioStation();
        context.RadioStations.Add(station);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var pagedRequest = new PagedRequest { PageSize = 10, IsTotalCountOnlyRequest = true };

        var result = await service.ListAsync(pagedRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.TotalCount);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task ListAsync_ShouldHandleCancellationToken()
    {
        var service = GetRadioStationService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ListAsync(new PagedRequest(), cts.Token));
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_ShouldCreateRadioStation_WhenValidData()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();

        var result = await service.AddAsync(radioStation);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(radioStation.Name, result.Data.Name);
        Assert.Equal(radioStation.StreamUrl, result.Data.StreamUrl);
        Assert.Equal(radioStation.HomePageUrl, result.Data.HomePageUrl);
        Assert.NotEqual(Guid.Empty, result.Data.ApiKey);
        Assert.True(result.Data.Id > 0);
    }

    [Fact]
    public async Task AddAsync_ShouldGenerateApiKey_WhenAdding()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        var originalApiKey = radioStation.ApiKey;

        var result = await service.AddAsync(radioStation);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(originalApiKey, result.Data!.ApiKey);
        Assert.NotEqual(Guid.Empty, result.Data.ApiKey);
    }

    [Fact]
    public async Task AddAsync_ShouldSetCreatedAt_WhenAdding()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        var beforeAdd = SystemClock.Instance.GetCurrentInstant();

        var result = await service.AddAsync(radioStation);
        var afterAdd = SystemClock.Instance.GetCurrentInstant();

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.CreatedAt >= beforeAdd);
        Assert.True(result.Data.CreatedAt <= afterAdd);
    }

    [Fact]
    public async Task AddAsync_ShouldThrowGuardException_WhenRadioStationIsNull()
    {
        var service = GetRadioStationService();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.AddAsync(null!));
    }

    [Fact]
    public async Task AddAsync_ShouldReturnValidationFailure_WhenNameIsEmpty()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.Name = "";

        var result = await service.AddAsync(radioStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.NotEmpty(result.Messages ?? []);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task AddAsync_ShouldReturnValidationFailure_WhenNameIsNull()
    {
        var service = GetRadioStationService();
        var radioStation = new RadioStation
        {
            Name = null!,
            StreamUrl = "http://stream.com",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var result = await service.AddAsync(radioStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.NotEmpty(result.Messages ?? []);
    }

    [Fact]
    public async Task AddAsync_ShouldReturnValidationFailure_WhenStreamUrlIsEmpty()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.StreamUrl = "";

        var result = await service.AddAsync(radioStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.NotEmpty(result.Messages ?? []);
    }

    [Fact]
    public async Task AddAsync_ShouldReturnValidationFailure_WhenStreamUrlIsNull()
    {
        var service = GetRadioStationService();
        var radioStation = new RadioStation
        {
            Name = "Test Station",
            StreamUrl = null!,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var result = await service.AddAsync(radioStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.NotEmpty(result.Messages ?? []);
    }

    [Fact]
    public async Task AddAsync_ShouldReturnValidationFailure_WhenNameTooLong()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.Name = new string('a', 256); // Max is 255

        var result = await service.AddAsync(radioStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.NotEmpty(result.Messages ?? []);
    }

    [Fact]
    public async Task AddAsync_ShouldReturnValidationFailure_WhenStreamUrlTooLong()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.StreamUrl = "http://" + new string('a', 2001); // Max is 2000

        var result = await service.AddAsync(radioStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.NotEmpty(result.Messages ?? []);
    }

    [Fact]
    public async Task AddAsync_ShouldReturnValidationFailure_WhenHomePageUrlTooLong()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.HomePageUrl = "http://" + new string('a', 2001); // Max is 2000

        var result = await service.AddAsync(radioStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.NotEmpty(result.Messages ?? []);
    }

    [Fact]
    public async Task AddAsync_ShouldSucceed_WhenHomePageUrlIsNull()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.HomePageUrl = null;

        var result = await service.AddAsync(radioStation);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Null(result.Data.HomePageUrl);
    }

    [Fact]
    public async Task AddAsync_ShouldHandleCancellationToken()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.AddAsync(radioStation, cts.Token));
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_ShouldReturnRadioStation_WhenExists()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var result = await service.GetAsync(radioStation.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(radioStation.Id, result.Data.Id);
        Assert.Equal(radioStation.Name, result.Data.Name);
        Assert.Equal(radioStation.StreamUrl, result.Data.StreamUrl);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenRadioStationDoesNotExist()
    {
        var service = GetRadioStationService();
        var result = await service.GetAsync(999);

        // Note: IsSuccess is false when Data is null due to OperationResult.IsSuccess implementation
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(OperationResponseType.Ok, result.Type);
    }

    [Fact]
    public async Task GetAsync_ShouldThrowGuardException_WhenIdIsZero()
    {
        var service = GetRadioStationService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetAsync(0));
    }

    [Fact]
    public async Task GetAsync_ShouldThrowGuardException_WhenIdIsNegative()
    {
        var service = GetRadioStationService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetAsync(-1));
    }

    [Fact]
    public async Task GetAsync_ShouldUseCaching_WhenCalledMultipleTimes()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        
        // First call should hit database
        var result1 = await service.GetAsync(radioStation.Id);
        // Second call should hit cache
        var result2 = await service.GetAsync(radioStation.Id);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal(result1.Data!.Id, result2.Data!.Id);
        Assert.Equal(result1.Data.Name, result2.Data.Name);
    }

    [Fact]
    public async Task GetAsync_ShouldHandleCancellationToken()
    {
        var service = GetRadioStationService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetAsync(1, cts.Token));
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ShouldDeleteRadioStation_WhenUserIsAdminAndStationExists()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var user = await CreateTestUserAsync(isAdmin: true);
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var result = await service.DeleteAsync(user.Id, [radioStation.Id]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        
        // Verify station is deleted - use a fresh context to ensure we see committed changes
        await using var verifyContext = await MockFactory().CreateDbContextAsync();
        var deletedStation = await verifyContext.RadioStations.FindAsync(radioStation.Id);
        Assert.Null(deletedStation);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteMultipleRadioStations_WhenUserIsAdminAndStationsExist()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var user = await CreateTestUserAsync(isAdmin: true);
        var station1 = CreateValidRadioStation("Station 1", "http://stream1.com");
        var station2 = CreateValidRadioStation("Station 2", "http://stream2.com");
        context.RadioStations.AddRange(station1, station2);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var result = await service.DeleteAsync(user.Id, [station1.Id, station2.Id]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        
        // Verify stations are deleted - use a fresh context to ensure we see committed changes
        await using var verifyContext = await MockFactory().CreateDbContextAsync();
        var deletedStation1 = await verifyContext.RadioStations.FindAsync(station1.Id);
        var deletedStation2 = await verifyContext.RadioStations.FindAsync(station2.Id);
        Assert.Null(deletedStation1);
        Assert.Null(deletedStation2);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnAccessDenied_WhenUserIsNotAdmin()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var user = await CreateTestUserAsync(isAdmin: false);
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var result = await service.DeleteAsync(user.Id, [radioStation.Id]);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.AccessDenied, result.Type);
        Assert.False(result.Data);
        Assert.Contains("Non admin users cannot delete RadioStations.", result.Messages ?? []);
        
        // Verify station still exists
        await using var verifyContext1 = await MockFactory().CreateDbContextAsync();
        var existingStation = await verifyContext1.RadioStations.FindAsync(radioStation.Id);
        Assert.NotNull(existingStation);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnFailure_WhenRadioStationDoesNotExist()
    {
        var user = await CreateTestUserAsync(isAdmin: true);
        var service = GetRadioStationService();

        var result = await service.DeleteAsync(user.Id, [999]);

        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Contains("Unknown RadioStation.", result.Messages ?? []);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowException_WhenUserDoesNotExist()
    {
        var service = GetRadioStationService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteAsync(999, [1]));
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowGuardException_WhenRadioStationIdsIsNull()
    {
        var service = GetRadioStationService();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.DeleteAsync(1, null!));
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowGuardException_WhenRadioStationIdsIsEmpty()
    {
        var service = GetRadioStationService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.DeleteAsync(1, []));
    }

    [Fact]
    public async Task DeleteAsync_ShouldHandleCancellationToken()
    {
        var user = await CreateTestUserAsync(isAdmin: true);
        var service = GetRadioStationService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.DeleteAsync(user.Id, [1], cts.Token));
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_ShouldUpdateRadioStation_WhenValidData()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();
        
        // Reload the entity to get the generated ID
        context.Entry(radioStation).Reload();

        var service = GetRadioStationService();
        var updatedStation = new RadioStation
        {
            Id = radioStation.Id,
            Name = "Updated Station",
            StreamUrl = "http://updated-stream.com",
            HomePageUrl = "http://updated-homepage.com",
            Description = "Updated description",
            Notes = "Updated notes",
            IsLocked = true,
            SortOrder = 100,
            Tags = "tag1|tag2",
            ApiKey = radioStation.ApiKey,
            CreatedAt = radioStation.CreatedAt
        };

        var result = await service.UpdateAsync(updatedStation);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        
        // Verify updates in database - use a fresh context to ensure we see committed changes
        await using var verifyContext = await MockFactory().CreateDbContextAsync();
        var dbStation = await verifyContext.RadioStations.FindAsync(radioStation.Id);
        Assert.NotNull(dbStation);
        Assert.Equal("Updated Station", dbStation.Name);
        Assert.Equal("http://updated-stream.com", dbStation.StreamUrl);
        Assert.Equal("http://updated-homepage.com", dbStation.HomePageUrl);
        Assert.Equal("Updated description", dbStation.Description);
        Assert.Equal("Updated notes", dbStation.Notes);
        Assert.True(dbStation.IsLocked);
        Assert.Equal(100, dbStation.SortOrder);
        Assert.Equal("tag1|tag2", dbStation.Tags);
        Assert.NotNull(dbStation.LastUpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_ShouldSetLastUpdatedAt_WhenUpdating()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var beforeUpdate = SystemClock.Instance.GetCurrentInstant();
        
        var updatedStation = new RadioStation
        {
            Id = radioStation.Id,
            Name = "Updated Station",
            StreamUrl = radioStation.StreamUrl,
            ApiKey = radioStation.ApiKey,
            CreatedAt = radioStation.CreatedAt
        };

        var result = await service.UpdateAsync(updatedStation);
        var afterUpdate = SystemClock.Instance.GetCurrentInstant();

        Assert.True(result.IsSuccess);
        
        await using var verifyContext2 = await MockFactory().CreateDbContextAsync();
        var dbStation = await verifyContext2.RadioStations.FindAsync(radioStation.Id);
        Assert.NotNull(dbStation!.LastUpdatedAt);
        Assert.True(dbStation.LastUpdatedAt >= beforeUpdate);
        Assert.True(dbStation.LastUpdatedAt <= afterUpdate);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnNotFound_WhenRadioStationDoesNotExist()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.Id = 999;

        var result = await service.UpdateAsync(radioStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowGuardException_WhenIdIsZero()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.Id = 0;

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(radioStation));
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowGuardException_WhenIdIsNegative()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.Id = -1;

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(radioStation));
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnValidationFailure_WhenNameIsEmpty()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var updatedStation = CreateValidRadioStation();
        updatedStation.Id = radioStation.Id;
        updatedStation.Name = "";

        var result = await service.UpdateAsync(updatedStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.False(result.Data);
        Assert.NotEmpty(result.Messages ?? []);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnValidationFailure_WhenStreamUrlIsEmpty()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        var updatedStation = CreateValidRadioStation();
        updatedStation.Id = radioStation.Id;
        updatedStation.StreamUrl = "";

        var result = await service.UpdateAsync(updatedStation);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.False(result.Data);
        Assert.NotEmpty(result.Messages ?? []);
    }

    [Fact]
    public async Task UpdateAsync_ShouldClearCache_WhenUpdateSuccessful()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        
        // First, get the station to populate cache
        await service.GetAsync(radioStation.Id);
        
        // Update the station
        var updatedStation = CreateValidRadioStation();
        updatedStation.Id = radioStation.Id;
        updatedStation.Name = "Updated Station";

        var result = await service.UpdateAsync(updatedStation);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        
        // Get again - should reflect the update (cache should be cleared)
        var getResult = await service.GetAsync(radioStation.Id);
        Assert.Equal("Updated Station", getResult.Data!.Name);
    }

    [Fact]
    public async Task UpdateAsync_ShouldHandleCancellationToken()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();
        radioStation.Id = 1;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.UpdateAsync(radioStation, cts.Token));
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task AllMethods_ShouldHandleDbContextDisposal()
    {
        var service = GetRadioStationService();
        var radioStation = CreateValidRadioStation();

        // These operations should handle context disposal gracefully
        var addResult = await service.AddAsync(radioStation);
        Assert.True(addResult.IsSuccess);

        var getResult = await service.GetAsync(addResult.Data!.Id);
        Assert.True(getResult.IsSuccess);

        var listResult = await service.ListAsync(new PagedRequest());
        Assert.True(listResult.IsSuccess);
    }

    [Fact]
    public async Task UpdateAsync_ShouldNotUpdateApiKey_WhenUpdating()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();
        var originalApiKey = radioStation.ApiKey;

        var service = GetRadioStationService();
        var updatedStation = CreateValidRadioStation();
        updatedStation.Id = radioStation.Id;
        updatedStation.ApiKey = Guid.NewGuid(); // Try to change API key

        var result = await service.UpdateAsync(updatedStation);

        Assert.True(result.IsSuccess);
        
        // Verify API key was not changed
        await using var verifyContext3 = await MockFactory().CreateDbContextAsync();
        var dbStation = await verifyContext3.RadioStations.FindAsync(radioStation.Id);
        Assert.Equal(originalApiKey, dbStation!.ApiKey);
    }

    [Fact]
    public async Task UpdateAsync_ShouldNotUpdateCreatedAt_WhenUpdating()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var radioStation = CreateValidRadioStation();
        context.RadioStations.Add(radioStation);
        await context.SaveChangesAsync();
        var originalCreatedAt = radioStation.CreatedAt;

        var service = GetRadioStationService();
        var updatedStation = CreateValidRadioStation();
        updatedStation.Id = radioStation.Id;
        updatedStation.CreatedAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromDays(1));

        var result = await service.UpdateAsync(updatedStation);

        Assert.True(result.IsSuccess);
        
        // Verify CreatedAt was not changed
        await using var verifyContext4 = await MockFactory().CreateDbContextAsync();
        var dbStation = await verifyContext4.RadioStations.FindAsync(radioStation.Id);
        Assert.Equal(originalCreatedAt, dbStation!.CreatedAt);
    }

    [Fact]
    public async Task DeleteAsync_ShouldValidateAllStationsExist_BeforeDeleting()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        
        var user = await CreateTestUserAsync(isAdmin: true);
        var station = CreateValidRadioStation();
        context.RadioStations.Add(station);
        await context.SaveChangesAsync();

        var service = GetRadioStationService();
        
        // Try to delete one existing and one non-existing station
        var result = await service.DeleteAsync(user.Id, [station.Id, 999]);

        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Contains("Unknown RadioStation.", result.Messages ?? []);
        
        // Verify existing station was not deleted
        await using var verifyContext5 = await MockFactory().CreateDbContextAsync();
        var existingStation = await verifyContext5.RadioStations.FindAsync(station.Id);
        Assert.NotNull(existingStation);
    }

    #endregion
}
