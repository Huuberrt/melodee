using Melodee.Common.Configuration;
using Melodee.Common.Data.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.OpenSubsonic.Requests;
using Melodee.Common.Models.OpenSubsonic.Responses;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Bus;
using Serilog;
using dbModels = Melodee.Common.Data.Models;

namespace Melodee.Common.Services;

/// <summary>
/// Handles user queue operations.
/// </summary>
public class UserQueueService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    UserService userService,
    IBus bus)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<PlayQueue?> GetPlayQueueForUserAsync(string username,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        var user = await userService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (!user.IsSuccess || user.Data == null)
        {
            return null;
        }

        var usersPlayQues = await scopedContext
            .PlayQues.Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
            .Where(x => x.UserId == user.Data.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (usersPlayQues.Length == 0)
        {
            return null;
        }

        var current = usersPlayQues.FirstOrDefault(x => x.IsCurrentSong);
        return new PlayQueue
        {
            Current = current?.PlayQueId ?? 0,
            Position = current?.Position ?? 0,
            ChangedBy = current?.ChangedBy ?? user.Data.UserName,
            Changed = current?.LastUpdatedAt.ToString() ?? string.Empty,
            Username = user.Data.UserName,
            Entry = usersPlayQues.Select(x => x.Song.ToApiChild(x.Song.Album, null)).ToArray()
        };
    }

    public async Task<bool> SavePlayQueueForUserAsync(string username, string[]? apiIds, string? currentApiId, double? position, string? client,
        CancellationToken cancellationToken = default)
    {
        var apiKeys = apiIds?.Select(ApiKeyFromId).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var current = ApiKeyFromId(currentApiId);
        
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        // If the apikey is blank then remove any current saved que
        if (apiKeys == null)
        {
            var user = await userService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
            if (user.IsSuccess && user.Data != null)
            {
                var playQuesToDelete = await scopedContext.PlayQues
                    .Where(pq => pq.UserId == user.Data.Id)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                
                scopedContext.PlayQues.RemoveRange(playQuesToDelete);
                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            return true;
        }
        else
        {
            var foundQuesSongApiKeys = new List<Guid>();
            var user = await userService.GetByUsernameAsync(username, cancellationToken)
                .ConfigureAwait(false);
            
            if (!user.IsSuccess || user.Data == null)
            {
                return false;
            }

            var usersPlayQues = await scopedContext
                .PlayQues.Include(x => x.Song)
                .Where(x => x.UserId == user.Data.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var changedByValue = client ?? user.Data.UserName;
            if (usersPlayQues.Length > 0)
            {
                foreach (var userPlay in usersPlayQues)
                {
                    if (!apiKeys.Contains(userPlay.Song.ApiKey))
                    {
                        scopedContext.PlayQues.Remove(userPlay);
                        continue;
                    }

                    if (userPlay.Song.ApiKey == current)
                    {
                        userPlay.Position = position ?? 0;
                    }

                    userPlay.IsCurrentSong = userPlay.Song.ApiKey == current;
                    userPlay.LastUpdatedAt = now;
                    userPlay.ChangedBy = changedByValue;
                    foundQuesSongApiKeys.Add(userPlay.Song.ApiKey);
                }
            }

            var addedPlayQues = new List<dbModels.PlayQueue>();
            foreach (var apiKeyToAdd in apiKeys.Except(foundQuesSongApiKeys))
            {
                var song = await scopedContext.Songs
                    .FirstOrDefaultAsync(x => x.ApiKey == apiKeyToAdd, cancellationToken).ConfigureAwait(false);
                if (song != null)
                {
                    addedPlayQues.Add(new dbModels.PlayQueue
                    {
                        PlayQueId = addedPlayQues.Count + 1,
                        CreatedAt = now,
                        IsCurrentSong = song.ApiKey == current,
                        UserId = user.Data.Id,
                        SongId = song.Id,
                        SongApiKey = song.ApiKey,
                        ChangedBy = changedByValue,
                        Position = apiKeyToAdd == current && position.HasValue ? position.Value : 0
                    });
                }
            }

            if (addedPlayQues.Count > 0)
            {
                await scopedContext.PlayQues.AddRangeAsync(addedPlayQues, cancellationToken).ConfigureAwait(false);
            }

            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private static Guid? ApiKeyFromId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var apiIdParts = id!.Split(OpenSubsonicServer.ApiIdSeparator);
        var toParse = id;
        if (apiIdParts.Length >= 2)
        {
            toParse = apiIdParts[1];
        }

        return SafeParser.ToGuid(toParse);
    }
}
