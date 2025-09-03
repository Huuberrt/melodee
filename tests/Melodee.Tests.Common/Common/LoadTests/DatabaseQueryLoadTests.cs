using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using NBomber.CSharp;
using NodaTime;
using Melodee.Tests.Common.Common.Services;

namespace Melodee.Tests.Common.Common.LoadTests;

public class DatabaseQueryLoadTests : ServiceTestBase
{
    [Xunit.Fact(Skip = "NBomber scenario used for manual load validation; skipped in CI to keep runs short.")]
    public async Task PlaylistQueryScenario_LoadTest_CompletesWithoutFailures()
    {
        // Seed a user and playlists for load testing
        await using var context = await MockFactory().CreateDbContextAsync();
        var user = new User
        {
            UserName = "load_user",
            UserNameNormalized = "LOAD_USER",
            Email = "load@example.com",
            EmailNormalized = "LOAD@EXAMPLE.COM",
            PublicKey = "loadkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var playlists = Enumerable.Range(0, 500).Select(i => new Playlist
        {
            ApiKey = Guid.NewGuid(),
            Name = $"Load Playlist {i}",
            Description = "Load test",
            IsPublic = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UserId = user.Id
        });
        context.Playlists.AddRange(playlists);
        await context.SaveChangesAsync();

        var service = GetPlaylistService();
        var userInfo = new UserInfo(user.Id, user.ApiKey, user.UserName, user.Email, string.Empty, string.Empty);

        // We execute an empty scenario with an init phase that runs DB queries to validate infra
        var scenario = Scenario.Empty("playlist_query_scenario");
        scenario = Scenario.WithInit(scenario, async _ =>
        {
            for (var i = 0; i < 50; i++)
            {
                var res = await service.ListAsync(userInfo, new PagedRequest { PageSize = 50, Page = 0 });
                if (!res.IsSuccess) throw new Exception("NBomber init: playlist query failed");
            }
        });
        scenario = Scenario.WithWarmUpDuration(scenario, TimeSpan.FromSeconds(1));
        scenario = Scenario.WithLoadSimulations(
            scenario,
            new[] { Simulation.Inject(rate: 5, interval: TimeSpan.FromMilliseconds(200), during: TimeSpan.FromSeconds(2)) }
        );

        var stats = NBomberRunner.RegisterScenarios(scenario).Run();

        // Validate runner completed and produced stats for the scenario
        Assert.NotNull(stats);
        Assert.Contains(stats.ScenarioStats, s => s.ScenarioName == "playlist_query_scenario");
    }
}
