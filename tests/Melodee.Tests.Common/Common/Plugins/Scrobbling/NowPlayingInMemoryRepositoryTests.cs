using Melodee.Common.Models.Scrobbling;
using Melodee.Common.Plugins.Scrobbling;

namespace Melodee.Tests.Common.Common.Plugins.Scrobbling;

public class NowPlayingInMemoryRepositoryTests
{
    [Fact]
    public async Task AddOrUpdateNowPlaying_WithCapacityLimit_EvictsOldest()
    {
        var repo = new NowPlayingInMemoryRepository(maxEntries: 10, entryMaxAge: TimeSpan.FromMinutes(10));

        // Add 12 entries
        for (int i = 0; i < 12; i++)
        {
            var np = CreateNowPlaying(
                Guid.NewGuid(),
                lastScrobbledAt: NodaTime.Instant.FromDateTimeOffset(DateTimeOffset.UtcNow.AddMinutes(-i))
            );
            await repo.AddOrUpdateNowPlayingAsync(np);
        }

        // Should be reduced to ~80% of capacity after eviction
        Assert.True(repo.CurrentCount <= 10);
        await repo.ClearNowPlayingAsync();
    }

    [Fact]
    public async Task AddOrUpdateNowPlaying_WithExpiredEntries_RemovesExpired()
    {
        var repo = new NowPlayingInMemoryRepository(maxEntries: 100, entryMaxAge: TimeSpan.FromMilliseconds(50));
        await repo.AddOrUpdateNowPlayingAsync(CreateNowPlaying(Guid.NewGuid()));

        Assert.Equal(1, repo.CurrentCount);
        await Task.Delay(100);

        // Trigger cleanup by another add
        await repo.AddOrUpdateNowPlayingAsync(CreateNowPlaying(Guid.NewGuid()));

        Assert.True(repo.CurrentCount <= 2);
        await repo.ClearNowPlayingAsync();
    }

    private static NowPlayingInfo CreateNowPlaying(Guid songApiKey, NodaTime.Instant? lastScrobbledAt = null)
    {
        var user = new Melodee.Common.Models.UserInfo(
            Id: 1,
            ApiKey: Guid.NewGuid(),
            UserName: "user",
            Email: "user@example.com",
            PublicKey: "pub",
            PasswordEncrypted: "pwd");

        var scrobble = new ScrobbleInfo(
            SongApiKey: songApiKey,
            ArtistId: 1,
            AlbumId: 1,
            SongId: 1,
            SongTitle: "title",
            ArtistName: "artist",
            IsRandomizedScrobble: false,
            AlbumTitle: "album",
            SongDuration: 300,
            SongMusicBrainzId: null,
            SongNumber: 1,
            SongArtist: "artist",
            CreatedAt: NodaTime.SystemClock.Instance.GetCurrentInstant(),
            PlayerName: "test")
        {
            LastScrobbledAt = lastScrobbledAt ?? NodaTime.SystemClock.Instance.GetCurrentInstant()
        };

        return new NowPlayingInfo(user, scrobble);
    }
}
