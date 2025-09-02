using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Services;
using Moq;

namespace Melodee.Tests.Common.Common.Services;

public class StreamingLimiterTests
{
    private static IMelodeeConfigurationFactory ConfigFactory(int global, int perUser)
    {
        var dict = MelodeeConfiguration.AllSettings(new Dictionary<string, object?>
        {
            { SettingRegistry.StreamingMaxConcurrentStreamsGlobal, global },
            { SettingRegistry.StreamingMaxConcurrentStreamsPerUser, perUser }
        });
        var conf = new MelodeeConfiguration(dict);
        var mock = new Mock<IMelodeeConfigurationFactory>();
        mock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(conf);
        return mock.Object;
    }

    [Fact]
    public async Task Unlimited_WhenZero_AllowsAll()
    {
        var limiter = new StreamingLimiter(ConfigFactory(0, 0));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(await limiter.TryEnterAsync($"u{i}"));
        }
    }

    [Fact]
    public async Task Respects_Global_Limit()
    {
        var limiter = new StreamingLimiter(ConfigFactory(2, 0));
        Assert.True(await limiter.TryEnterAsync("a"));
        Assert.True(await limiter.TryEnterAsync("b"));
        Assert.False(await limiter.TryEnterAsync("c"));
        limiter.Exit("a");
        Assert.True(await limiter.TryEnterAsync("c"));
    }

    [Fact]
    public async Task Respects_PerUser_Limit()
    {
        var limiter = new StreamingLimiter(ConfigFactory(0, 2));
        Assert.True(await limiter.TryEnterAsync("user1"));
        Assert.True(await limiter.TryEnterAsync("user1"));
        Assert.False(await limiter.TryEnterAsync("user1"));
        limiter.Exit("user1");
        Assert.True(await limiter.TryEnterAsync("user1"));
    }
}

