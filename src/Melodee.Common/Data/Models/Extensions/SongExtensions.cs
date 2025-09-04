using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Constants;
using Melodee.Common.Extensions;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.Scrobbling;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Utility;
using Melodee.Common.Enums;

namespace Melodee.Common.Data.Models.Extensions;

public static class SongExtensions
{
    public static double DurationInSeconds(this Song song)
    {
        return song.Duration / 1000;
    }

    public static SongSearchResult ToSearchEngineSongSearchResult(this Song song, int sortOrder)
    {
        return new SongSearchResult
        {
            Id = song.Id,
            ApiKey = song.ApiKey,
            Name = song.Title,
            MusicBrainzId = song.MusicBrainzId,
            SortName = song.TitleSort ?? song.Title,
            SortOrder = sortOrder,
            PlayCount = song.PlayedCount
        };
    }

    public static string ToCoverArtId(this Song song)
    {
        return song.ToApiKey();
    }

    public static string ToApiKey(this Song song)
    {
        return $"song{OpenSubsonicServer.ApiIdSeparator}{song.ApiKey}";
    }

    public static string ToApiStreamUrl(this Song song, IMelodeeConfiguration configuration)
    {
        var baseUrl = configuration.GetValue<string>(SettingRegistry.SystemBaseUrl);
        if (baseUrl.Nullify() == null || baseUrl == MelodeeConfiguration.RequiredNotSetValue)
        {
            throw new Exception($"Configuration setting [{SettingRegistry.SystemBaseUrl}] is invalid.");
        }

        return $"{baseUrl}/rest/stream?id={song.ToApiKey()}";
    }

    public static string ToApiStreamUrl(this Song song, IMelodeeConfiguration configuration, Microsoft.AspNetCore.Http.HttpContext? httpContext)
    {
        var configuredBaseUrl = configuration.GetValue<string>(SettingRegistry.SystemBaseUrl);
        
        string baseUrl;
        if (configuredBaseUrl.Nullify() == null || configuredBaseUrl == MelodeeConfiguration.RequiredNotSetValue)
        {
            if (httpContext == null)
            {
                throw new Exception($"Configuration setting [{SettingRegistry.SystemBaseUrl}] is invalid and no HttpContext provided for fallback.");
            }
            
            baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";
        }
        else
        {
            baseUrl = configuredBaseUrl!;
        }

        return $"{baseUrl.TrimEnd('/')}/rest/stream?id={song.ToApiKey()}";
    }

    public static Child ToApiChild(this Song song, Album album, UserSong? userSong,
        NowPlayingInfo? nowPlayingInfo = null)
    {
        Contributor? albumArtist = null;

        return new Child(song.ToApiKey(),
            album.ToApiKey(),
            false,
            song.Title,
            album.Name,
            albumArtist == null ? album.Artist.Name : albumArtist.Artist!.Name,
            song.SongNumber,
            album.ReleaseDate.Year,
            song.ToCoverArtId(),
            song.FileSize,
            song.ContentType,
            Path.GetExtension(song.FileName),
            userSong?.IsStarred ?? false ? userSong.LastUpdatedAt.ToString() : null,
            song.Duration.ToSeconds(),
            song.BitRate,
            song.BitDepth,
            song.SamplingRate,
            song.ChannelCount,
            Path.Combine(album.Directory, song.FileName),
            song.PlayedCount,
            song.LastPlayedAt?.ToString(),
            1,
            song.CreatedAt.ToString(),
            album.ToApiKey(),
            album.Artist.ToApiKey(),
            "music",
            "song",
            false,
            song.BPM,
            null,
            song.TitleSort,
            song.MusicBrainzId?.ToString(),
            song.Genres?.Select(g => new Genre { Value = g, SongCount = 0, AlbumCount = 0 }).ToArray(),
            song.Contributors.Where(c => c.ContributorTypeValue == ContributorType.Performer)
                .Select(c => c.Artist != null
                    ? c.Artist.ToApiArtistID3()
                    : new ArtistID3(
                        $"contributor{OpenSubsonicServer.ApiIdSeparator}{(c.ContributorName ?? string.Empty).ToNormalizedString()}",
                        c.ContributorName ?? string.Empty,
                        song.ToCoverArtId(),
                        0,
                        0,
                        null,
                        null,
                        null,
                        null,
                        null)).ToArray(),
            album.Artist.Name,
            new[] { album.Artist.ToApiArtistID3() },
            album.Artist.Name,
            song.Contributors.Select(c => new Common.Models.OpenSubsonic.Contributor(
                c.Role,
                c.Artist != null
                    ? c.Artist.ToApiArtistID3()
                    : new ArtistID3(
                        $"contributor{OpenSubsonicServer.ApiIdSeparator}{(c.ContributorName ?? string.Empty).ToNormalizedString()}",
                        c.ContributorName ?? string.Empty,
                        song.ToCoverArtId(),
                        0,
                        0,
                        null,
                        null,
                        null,
                        null,
                        null
                    ),
                c.SubRole)).ToArray(),
            song.Contributors.FirstOrDefault(cc => cc.Role.Equals("Composer", StringComparison.OrdinalIgnoreCase))?.Artist?.Name
                ?? song.Contributors.FirstOrDefault(cc => cc.Role.Equals("Composer", StringComparison.OrdinalIgnoreCase))?.ContributorName,
            song.Moods,
            new ReplayGain(song.ReplayGain, album.ReplayGain, song.ReplayPeak, album.ReplayPeak, null, null),
            SafeParser.ToNumber<int>(song.CalculatedRating),
            userSong?.Rating,
            nowPlayingInfo?.User.UserName,
            nowPlayingInfo?.Scrobble.MinutesAgo,
            0,
            nowPlayingInfo?.Scrobble.PlayerName
        );
    }

    public static SongDataInfo ToSongDataInfo(this Song song, UserSong? userSong = null)
    {
        return new SongDataInfo(song.Id,
            song.ApiKey,
            song.IsLocked,
            song.Title,
            song.TitleNormalized,
            song.SongNumber,
            song.Album.ReleaseDate,
            song.Album.Name,
            song.Album.ApiKey,
            song.Album.Artist.Name,
            song.Album.Artist.ApiKey,
            song.FileSize,
            song.Duration,
            song.CreatedAt,
            song.Tags ?? string.Empty,
            userSong?.IsStarred ?? false,
            userSong?.Rating ?? 0
        );
    }
}
