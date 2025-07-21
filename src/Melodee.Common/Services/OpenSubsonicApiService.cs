using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Constants;
using Melodee.Common.Data.Models.DTOs;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Collection.Extensions;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.OpenSubsonic.DTO;
using Melodee.Common.Models.OpenSubsonic.Enums;
using Melodee.Common.Models.OpenSubsonic.Requests;
using Melodee.Common.Models.OpenSubsonic.Responses;
using Melodee.Common.Models.OpenSubsonic.Searching;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Extensions;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;
using Rebus.Bus;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using dbModels = Melodee.Common.Data.Models;
using Artist = Melodee.Common.Models.OpenSubsonic.Artist;
using Directory = Melodee.Common.Models.OpenSubsonic.Directory;
using License = Melodee.Common.Models.OpenSubsonic.License;
using Playlist = Melodee.Common.Models.OpenSubsonic.Playlist;
using PlayQueue = Melodee.Common.Models.OpenSubsonic.PlayQueue;
using ScanStatus = Melodee.Common.Models.OpenSubsonic.ScanStatus;

namespace Melodee.Common.Services;

/// <summary>
///     Handles OpenSubsonic API calls.
/// </summary>
public class OpenSubsonicApiService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    DefaultImages defaultImages,
    IMelodeeConfigurationFactory configurationFactory,
    UserService userService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    SearchService searchService,
    IScheduler schedule,
    ScrobbleService scrobbleService,
    LibraryService libraryService,
    ArtistSearchEngineService artistSearchEngineService,
    PlaylistService playlistService,
    ShareService shareService,
    IBus bus,
    ILyricPlugin lyricPlugin
)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public const string ImageCacheRegion = "urn:openSubsonic:artist-and-album-images";

    private Lazy<Task<IMelodeeConfiguration>> Configuration => new(() => configurationFactory.GetConfigurationAsync());

    private static bool IsApiIdForArtist(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"artist{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForAlbum(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"album{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForUser(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"user{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForSong(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"song{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForPlaylist(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"playlist{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForDynamicPlaylist(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"dpl{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static Guid? ApiKeyFromId(string? id)
    {
        if (id.Nullify() == null)
        {
            return null;
        }

        var apiIdParts = id!.Split(OpenSubsonicServer.ApiIdSeparator);
        var toParse = id;
        if (apiIdParts.Length < 2)
        {
            Log.Warning("ApiKeyFromId: Invalid ApiKey [{Key}]", id);
        }
        else
        {
            toParse = apiIdParts[1];
        }

        return SafeParser.ToGuid(toParse);
    }

    /// <summary>
    ///     Get details about the software license.
    /// </summary>
    public async Task<ResponseModel> GetLicenseAsync(
        ApiRequest apiApiRequest,
        CancellationToken cancellationToken = default)
    {
        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                DataPropertyName = "license",
                Data = new License(true,
                    (await Configuration.Value).GetValue<string>(SettingRegistry.OpenSubsonicServerLicenseEmail) ??
                    ServiceUser.Instance.Value.Email,
                    DateTimeOffset.UtcNow.AddYears(50).ToXmlSchemaDateTimeFormat(),
                    DateTimeOffset.UtcNow.AddYears(50).ToXmlSchemaDateTimeFormat()
                )
            }
        };
    }

    /// <summary>
    ///     Returns information about shared media this user is allowed to manage.
    /// </summary>
    /// <param name="apiRequest">An API request containing the necessary details for authentication and filtering the request.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>A ResponseModel containing user information and the corresponding list of shares.</returns>
    public async Task<ResponseModel> GetSharesAsync(
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = new List<Share>();

        var dbSharesResult = await userService.UserSharesAsync(authResponse.UserInfo.Id, cancellationToken)
            .ConfigureAwait(false);
        foreach (var dbShare in dbSharesResult ?? [])
        {
            Child[] shareEntries = [];
            switch (dbShare.ShareTypeValue)
            {
                case ShareType.Song:
                    var song = await songService.GetAsync(dbShare.ShareId, cancellationToken).ConfigureAwait(false);
                    var userSong = await userService.UserSongAsync(authResponse.UserInfo.Id, song.Data!.ApiKey,
                        cancellationToken);
                    if (userSong != null)
                    {
                        shareEntries = [song.Data.ToApiChild(song.Data.Album, userSong)];
                    }

                    break;
                case ShareType.Album:
                    var album = await albumService.GetAsync(dbShare.ShareId, cancellationToken).ConfigureAwait(false);
                    var userSongsForAlbum = await userService.UserSongsForAlbumAsync(authResponse.UserInfo.Id, album.Data!.ApiKey, cancellationToken);
                    if (userSongsForAlbum != null)
                    {
                        shareEntries = album.Data.Songs.Select(ss =>
                                ss.ToApiChild(ss.Album, userSongsForAlbum.FirstOrDefault(x => x.SongId == ss.Id)))
                            .ToArray();
                    }

                    break;
                case ShareType.Playlist:
                    var playlist = await playlistService.GetAsync(dbShare.ShareId, cancellationToken)
                        .ConfigureAwait(false);
                    var userSongsForPlaylist = await userService.UserSongsForPlaylistAsync(authResponse.UserInfo.Id,
                        playlist.Data!.ApiKey, cancellationToken);
                    if (userSongsForPlaylist != null)
                    {
                        shareEntries = playlist.Data.Songs.Select(pls => pls.Song.ToApiChild(pls.Song.Album,
                            userSongsForPlaylist.FirstOrDefault(x => x.SongId == pls.Song.Id))).ToArray();
                    }

                    break;
            }

            data.Add(dbShare.ToApiShare(dbShare.ToUrl(await Configuration.Value), shareEntries));
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data.ToArray(),
                DataPropertyName = "shares",
                DataDetailPropertyName = apiRequest.IsXmlRequest ? string.Empty : "share"
            }
        };
    }

    /// <summary>
    ///     Creates a public URL that can be used by anyone to stream music.
    /// </summary>
    /// <param name="apiRequest">The API request containing authentication and other request-related details.</param>
    /// <param name="id">The unique identifier of the item to be shared (e.g., song, album, or playlist).</param>
    /// <param name="description">An optional description for the shared item.</param>
    /// <param name="expires">
    ///     The expiration timestamp (in milliseconds since UNIX epoch) for the share, or null if the share
    ///     does not expire.
    /// </param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A <see cref="ResponseModel" /> containing information about the created share, including success status and
    ///     data.
    /// </returns>
    public async Task<ResponseModel> CreateShareAsync(
        ApiRequest apiRequest,
        string id,
        string? description,
        long? expires,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        // The user must be authorized to share
        var user = await userService.GetAsync(authResponse.UserInfo.Id, cancellationToken).ConfigureAwait(false);
        if (!user.Data?.CanShare() ?? false)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        var shareApiKey = ApiKeyFromId(id)!.Value;
        Child[] resultEntries;

        var dbShare = new dbModels.Share
        {
            UserId = user.Data!.Id,
            ShareId = 0,
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };
        if (IsApiIdForSong(id))
        {
            var song = await songService.GetByApiKeyAsync(shareApiKey, cancellationToken).ConfigureAwait(false);
            if (!song.IsSuccess)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                };
            }

            dbShare.ShareType = SafeParser.ToNumber<int>(ShareType.Song);
            dbShare.ShareId = song.Data!.Id;
            var userSong =
                await userService.UserSongAsync(authResponse.UserInfo.Id, song.Data.ApiKey, cancellationToken);
            resultEntries = [song.Data.ToApiChild(song.Data.Album, userSong)];
        }
        else if (IsApiIdForAlbum(id))
        {
            var album = await albumService.GetByApiKeyAsync(shareApiKey, cancellationToken).ConfigureAwait(false);
            if (!album.IsSuccess)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                };
            }

            dbShare.ShareType = SafeParser.ToNumber<int>(ShareType.Album);
            dbShare.ShareId = album.Data!.Id;
            var userSongsForAlbum =
                await userService.UserSongsForAlbumAsync(authResponse.UserInfo.Id, album.Data.ApiKey,
                    cancellationToken);
            resultEntries = album.Data.Songs.Select(song =>
                song.ToApiChild(song.Album, userSongsForAlbum?.FirstOrDefault(x => x.SongId == song.Id))).ToArray();
        }
        else if (IsApiIdForPlaylist(id))
        {
            var playlist = await playlistService.GetByApiKeyAsync(authResponse.UserInfo, shareApiKey, cancellationToken).ConfigureAwait(false);
            if (!playlist.IsSuccess)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                };
            }

            dbShare.ShareType = SafeParser.ToNumber<int>(ShareType.Playlist);
            dbShare.ShareId = playlist.Data!.Id;
            var userSongsForPlaylist = await userService.UserSongsForPlaylistAsync(authResponse.UserInfo.Id,
                playlist.Data.ApiKey, cancellationToken);
            resultEntries = playlist.Data.Songs.Select(pls =>
                    pls.Song.ToApiChild(pls.Song.Album,
                        userSongsForPlaylist?.FirstOrDefault(x => x.SongId == pls.Song.Id)))
                .ToArray();
        }
        else
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty,
                    Error.RequiredParameterMissingError)
            };
        }

        dbShare.Description = description;
        dbShare.ExpiresAt = expires != null ? Instant.FromUnixTimeMilliseconds(expires.Value) : null;
        var addResult = await shareService.AddAsync(dbShare, cancellationToken).ConfigureAwait(false);
        var data = addResult.IsSuccess
            ? addResult.Data!.ToApiShare(addResult.Data!.ToUrl(await Configuration.Value), resultEntries)
            : null;

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                IsSuccess = data != null,
                Data = data,
                DataPropertyName = "shares",
                DataDetailPropertyName = apiRequest.IsXmlRequest ? string.Empty : "share"
            }
        };
    }

    /// <summary>
    ///     Updates the description and/or expiration date for an existing share.
    /// </summary>
    /// <param name="apiRequest">The API request with authentication and context information.</param>
    /// <param name="id">The unique identifier of the share to be updated.</param>
    /// <param name="description">An optional description to attach to the share.</param>
    /// <param name="expires">An optional expiration time for the share in Unix timestamp format.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A task representing the asynchronous operation, containing a response model with the result of the share
    ///     update.
    /// </returns>
    public async Task<ResponseModel> UpdateShareAsync(
        ApiRequest apiRequest,
        string id,
        string? description,
        long? expires,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        var apiKey = ApiKeyFromId(id);
        var shareResult = await shareService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        
        if (shareResult.IsSuccess && shareResult.Data != null)
        {
            var share = shareResult.Data;
            share.Description = description;
            share.ExpiresAt = expires != null ? Instant.FromUnixTimeMilliseconds(expires.Value) : share.ExpiresAt;
            
            var updateResult = await shareService.UpdateAsync(share, cancellationToken).ConfigureAwait(false);
            notAuthorizedError =
                updateResult is
                {
                    IsSuccess: false, Type: OperationResponseType.Unauthorized or OperationResponseType.AccessDenied
                }
                    ? Error.UserNotAuthorizedError
                    : Error.InvalidApiKeyError;
            result = updateResult.IsSuccess;
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    /// <summary>
    ///     Deletes an existing share.
    /// </summary>
    /// <param name="id">The unique identifier of the shared resource to be deleted.</param>
    /// <param name="apiRequest">The API request containing authentication and other request details.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a <see cref="ResponseModel" />
    ///     indicating the success or failure of the operation.
    /// </returns>
    public async Task<ResponseModel> DeleteShareAsync(
        string id, 
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        var apiKey = ApiKeyFromId(id);
        var shareResult = await shareService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        
        if (shareResult.IsSuccess && shareResult.Data != null)
        {
            var deleteResult = await shareService
                .DeleteAsync(authResponse.UserInfo.Id, [shareResult.Data.Id], cancellationToken).ConfigureAwait(false);
            notAuthorizedError =
                deleteResult is
                {
                    IsSuccess: false, Type: OperationResponseType.Unauthorized or OperationResponseType.AccessDenied
                }
                    ? Error.UserNotAuthorizedError
                    : Error.InvalidApiKeyError;
            result = deleteResult.IsSuccess;
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    /// <summary>
    ///     Returns all playlists a user is allowed to play.
    /// </summary>
    public async Task<ResponseModel> GetPlaylistsAsync(ApiRequest apiRequest, CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = new List<Playlist>();

        try
        {
            var playlistsResult = await playlistService.GetPlaylistsForUserAsync(authResponse.UserInfo.Id, cancellationToken);
            if (playlistsResult.IsSuccess && playlistsResult.Data != null)
            {
                data = playlistsResult.Data.Select(x => x.ToApiPlaylist(false)).ToList();
            }

            var dynamicPlaylists = await playlistService.DynamicListAsync(authResponse.UserInfo, new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            data.AddRange(dynamicPlaylists.Data.Select(x => x.ToApiPlaylist(false, true)));
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to get Playlists Request [{ApiResult}]", apiRequest);
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data.ToArray(),
                DataPropertyName = "playlists",
                DataDetailPropertyName = "playlist"
            }
        };
    }

    public async Task<ResponseModel> UpdatePlaylistAsync(
        UpdatePlayListRequest updateRequest, 
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        var apiKey = ApiKeyFromId(updateRequest.PlaylistId);
        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        // Handle song removal using PlaylistService
        if (updateRequest.SongIdToRemove?.Any() == true)
        {
            var songsToRemove = updateRequest.SongIdToRemove
                .Select(ApiKeyFromId)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();

            if (songsToRemove.Any())
            {
                var removeResult = await playlistService.RemoveSongsFromPlaylistAsync(apiKey.Value, songsToRemove, cancellationToken).ConfigureAwait(false);
                if (!removeResult.IsSuccess)
                {
                    return new ResponseModel
                    {
                        UserInfo = UserInfo.BlankUserInfo,
                        IsSuccess = false,
                        ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                    };
                }
            }
        }

        // Handle song addition using PlaylistService
        if (updateRequest.SongIdToAdd?.Any() == true)
        {
            var songsToAdd = updateRequest.SongIdToAdd
                .Select(ApiKeyFromId)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();

            if (songsToAdd.Any())
            {
                var addResult = await playlistService.AddSongsToPlaylistAsync(apiKey.Value, songsToAdd, cancellationToken).ConfigureAwait(false);
                if (!addResult.IsSuccess)
                {
                    return new ResponseModel
                    {
                        UserInfo = UserInfo.BlankUserInfo,
                        IsSuccess = false,
                        ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                    };
                }
            }
        }

        // Handle metadata updates using PlaylistService
        if (updateRequest.Name != null || updateRequest.Comment != null || updateRequest.Public.HasValue)
        {
            var updateResult = await playlistService.UpdatePlaylistMetadataAsync(
                apiKey.Value,
                authResponse.UserInfo.Id,
                updateRequest.Name,
                updateRequest.Comment,
                updateRequest.Public,
                cancellationToken).ConfigureAwait(false);

            if (updateResult.IsSuccess)
            {
                result = true;
            }
            else if (updateResult.Type == OperationResponseType.AccessDenied)
            {
                notAuthorizedError = Error.UserNotAuthorizedError;
            }
        }
        else
        {
            result = true; // No metadata to update, consider successful
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    // TODO: This should be moved to PlaylistService in the future.
    /// <summary>
    ///     Deletes a saved playlist.
    /// </summary>
    public async Task<ResponseModel> DeletePlaylistAsync(string id, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var apiKey = ApiKeyFromId(id);
            var playlist = await scopedContext
                .Playlists
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
                .ConfigureAwait(false);
            if (playlist != null)
            {
                if (playlist.UserId != authResponse.UserInfo.Id)
                {
                    notAuthorizedError = Error.UserNotAuthorizedError;
                }
                else
                {
                    scopedContext.Playlists.Remove(playlist);
                    await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    result = true;
                }
            }
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    // TODO: This should be moved to PlaylistService in the future.
    public async Task<ResponseModel> CreatePlaylistAsync(
        string? id,
        string? name,
        string[]? songId,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var playListId = string.Empty;
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var isCreatingPlaylist = id.Nullify() == null && name.Nullify() != null;
            if (isCreatingPlaylist)
            {
                // creating new with name and songs 
                var songApiKeysForPlaylist =
                    songId?.Where(x => x.Nullify() != null).Select(ApiKeyFromId).ToArray() ?? [];
                var songsForPlaylist = await scopedContext.Songs.Where(x => songApiKeysForPlaylist.Contains(x.ApiKey))
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false);
                var newPlaylist = new dbModels.Playlist
                {
                    CreatedAt = now,
                    Name = name!,
                    UserId = authResponse.UserInfo.Id,
                    SongCount = SafeParser.ToNumber<short>(songsForPlaylist.Length),
                    Duration = songsForPlaylist.Sum(x => x.Duration),
                    Songs = songsForPlaylist.Select((x, i) => new dbModels.PlaylistSong
                    {
                        SongId = x.Id,
                        SongApiKey = x.ApiKey,
                        PlaylistOrder = i
                    }).ToArray()
                };
                await scopedContext.Playlists.AddAsync(newPlaylist, cancellationToken).ConfigureAwait(false);
                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                playListId = newPlaylist.ToApiKey();
                Logger.Information("User [{UserInfo}] created playlist [{Name}] with [{SongCount}] songs.",
                    authResponse.UserInfo, name, songsForPlaylist.Length);
            }
            // updating either new name or songs on playlist
        }

        return await GetPlaylistAsync(playListId, apiRequest, cancellationToken);
    }

    // TODO: This should be moved to PlaylistService in the future.
    /// <summary>
    ///     Returns a listing of files in a saved playlist.
    /// </summary>
    public async Task<ResponseModel> GetPlaylistAsync(
        string id,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Playlist? data;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var apiKey = ApiKeyFromId(id);

            if (IsApiIdForDynamicPlaylist(id))
            {
                var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
                var dynamicPlaylist = await libraryService
                    .GetDynamicPlaylistAsync(apiKey ?? Guid.Empty, cancellationToken).ConfigureAwait(false);
                var dp = dynamicPlaylist.Data;
                if (dp == null)
                {
                    Logger.Warning("Invalid dynamic playlist id [{Id}] for Request [{Request}]", apiKey, apiRequest);
                    return new ResponseModel
                    {
                        UserInfo = UserInfo.BlankUserInfo,
                        ResponseData = authResponse.ResponseData with
                        {
                            Error = Error.InvalidApiKeyError
                        }
                    };
                }

                var dpWhere = dp.PrepareSongSelectionWhere(authResponse.UserInfo);
                var dpOrderBy = dp.SongSelectionOrder ?? "RANDOM()";

                var offset = 0;
                var fetch = dp.SongLimit ?? 100;

                // Use EF Core FromSqlRaw for better security and maintainability
                var sql = $"""
                           SELECT s."Id", s."ApiKey", s."IsLocked", s."Title", s."TitleNormalized", s."SongNumber", a."ReleaseDate",
                                  a."Name" as "AlbumName", a."ApiKey" as "AlbumApiKey", ar."Name" as "ArtistName", ar."ApiKey" as "ArtistApiKey",
                                  s."FileSize", s."Duration", s."CreatedAt", s."Tags", us."IsStarred" as "UserStarred", us."Rating" as "UserRating"
                           FROM "Songs" s
                           join "Albums" a on (s."AlbumId" = a."Id")
                           join "Artists" ar on (a."ArtistId" = ar."Id")
                           left join "UserSongs" us on (s."Id" = us."SongId" and us."UserId" = {authResponse.UserInfo.Id})
                           where {dpWhere}
                           order by {dpOrderBy}
                           offset {offset} rows fetch next {fetch} rows only;
                           """;
                var songDataInfosForDp = await scopedContext.Database
                    .SqlQueryRaw<SongDataInfo>(sql)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                var songs = await scopedContext
                    .Songs
                    .Include(x => x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                    .Include(x => x.Album).ThenInclude(x => x.Artist).ThenInclude(x => x.Library)
                    .Where(x => songDataInfosForDp.Select(y => y.Id).Contains(x.Id))
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                var dpSongs = new List<dbModels.PlaylistSong>();
                foreach (var songDataInfo in songDataInfosForDp.Select((x, i) => new { x, i }))
                {
                    var song = songs.FirstOrDefault(x => x.Id == songDataInfo.x.Id);
                    if (song != null)
                    {
                        dpSongs.Add(songDataInfo.x.ToPlaylistSong(songDataInfo.i, song));
                    }
                }

                data = new dbModels.Playlist
                {
                    Id = 1,
                    IsLocked = false,
                    SortOrder = 0,
                    ApiKey = dp.Id,
                    CreatedAt = now,
                    Description = dp.Comment,
                    Name = dp.Name,
                    Comment = null,
                    User = ServiceUser.Instance.Value,
                    IsPublic = true,
                    SongCount = SafeParser.ToNumber<short>(songDataInfosForDp.Count()),
                    Duration = songDataInfosForDp.Sum(x => x.Duration),
                    AllowedUserIds = authResponse.UserInfo.UserName,
                    Songs = dpSongs
                }.ToApiPlaylist(true, true);
            }

            else
            {
                var playlist = await scopedContext
                    .Playlists
                    .Include(x => x.User)
                    .Include(x => x.Songs).ThenInclude(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
                    .Include(x => x.Songs).ThenInclude(x => x.Song).ThenInclude(x =>
                        x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                    .FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
                    .ConfigureAwait(false);

                data = playlist?.ToApiPlaylist();
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                IsSuccess = data != null,
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "playlist"
            }
        };
    }

    // TODO: This should be moved to AlbumService in the future.
    public async Task<ResponseModel> GetAlbumListAsync(
        GetAlbumListRequest albumListRequest,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        long totalCount = 0;
        AlbumList[] data = [];
        var sql = string.Empty;

        try
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var sqlParameters = new Dictionary<string, object?>
                {
                    { "genre", albumListRequest.Genre },
                    { "fromYear", albumListRequest.FromYear },
                    { "toYear", albumListRequest.ToYear },
                    { "userId", authResponse.UserInfo.Id }
                };
                var selectSql = """
                                SELECT 
                                'album_' || cast(a."ApiKey" as varchar(50)) as "Id",
                                a."Name" as "Album",
                                a."Name" as "Title",
                                a."Name" as "Name",
                                'album_' || cast(a."ApiKey" as varchar(50)) as "CoverArt",
                                a."SongCount",
                                a."CreatedAt" as "CreatedRaw",
                                a."Duration"/1000 as "Duration",
                                a."PlayedCount",
                                'artist_' || cast(aa."ApiKey" as varchar(50)) as "ArtistId",
                                aa."Name" as "Artist",
                                DATE_PART('year', a."ReleaseDate"::date) as "Year",
                                a."Genres",
                                (SELECT "IsStarred" FROM "UserAlbums" WHERE "UserId" = @userId AND "AlbumId" = a."Id") as "Starred", 
                                (SELECT COUNT(*) FROM "UserAlbums" WHERE "IsStarred" AND "AlbumId" = a."Id") as "UserStarredCount",
                                a."CalculatedRating" as "AverageRating",
                                ''
                                FROM "Albums" a 
                                JOIN "Artists" aa on (a."ArtistId" = aa."Id")
                                """;
                var whereSql = string.Empty;
                var orderSql = string.Empty;
                var limitSql =
                    $"OFFSET {albumListRequest.OffsetValue} ROWS FETCH NEXT {albumListRequest.SizeValue} ROWS ONLY;";
                switch (albumListRequest.Type)
                {
                    case ListType.Random:
                        orderSql = "ORDER BY RANDOM()";
                        break;

                    case ListType.Newest:
                        orderSql = "ORDER BY a.\"CreatedAt\" DESC";
                        break;

                    case ListType.Highest:
                        orderSql = "ORDER BY a.\"CalculatedRating\" DESC";
                        break;

                    case ListType.Frequent:
                        orderSql = "ORDER BY a.\"PlayedCount\" DESC";
                        break;

                    case ListType.Recent:
                        orderSql = "ORDER BY a.\"LastPlayedAt\" DESC";
                        break;

                    case ListType.AlphabeticalByName:
                        orderSql = "ORDER BY a.\"SortName\"";
                        break;

                    case ListType.AlphabeticalByArtist:
                        orderSql = "ORDER BY aa.\"SortName\"";
                        break;

                    case ListType.Starred:
                        orderSql = "ORDER BY \"UserStarredCount\" DESC";
                        break;

                    case ListType.ByYear:
                        whereSql = "where DATE_PART('year', a.\"ReleaseDate\"::date) between @fromYear AND @toYear";
                        orderSql = "ORDER BY \"Year\" DESC";
                        break;

                    case ListType.ByGenre:
                        whereSql = "where @genre = any(a.\"Genres\")";
                        orderSql = "ORDER BY \"Genre\" DESC";
                        break;
                }

                sql = $"{selectSql} {whereSql} {orderSql} {limitSql}";
                data = await scopedContext.Database
                    .SqlQueryRaw<AlbumList>(sql, sqlParameters.Values.Where(v => v != null).Cast<object>().ToArray())
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                var countSql = $"SELECT COUNT(*) FROM \"Albums\" a JOIN \"Artists\" aa on (a.\"ArtistId\" = aa.\"Id\") {whereSql}";
                totalCount = await scopedContext.Database
                    .SqlQueryRaw<long>(countSql, sqlParameters.Values.Where(v => v != null).Cast<object>().ToArray())
                    .FirstAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to get AlbumList SQL [{Sql}] Request [{ApiResult}]", sql, apiRequest);
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            TotalCount = totalCount,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "albumList",
                DataDetailPropertyName = "album"
            }
        };
    }

    // TODO: This should be moved to AlbumService in the future.
    /// <summary>
    ///     Returns a list of random, newest, highest rated etc. albums.
    /// </summary>
    public async Task<ResponseModel> GetAlbumList2Async(
        GetAlbumListRequest albumListRequest,
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        long totalCount = 0;
        AlbumList2[] data = [];
        var sql = string.Empty;

        try
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var sqlParameters = new Dictionary<string, object?>
                {
                    { "genre", albumListRequest.Genre },
                    { "fromYear", albumListRequest.FromYear },
                    { "toYear", albumListRequest.ToYear },
                    { "userId", authResponse.UserInfo.Id }
                };

                var selectSql = """
                                SELECT 
                                'album_' || cast(a."ApiKey" as varchar(50)) as "Id",
                                'library_' || cast(aa."ApiKey" as varchar(50)) as "Parent",
                                a."Name" as "Album",
                                a."Name" as "Title",
                                a."Name" as "Name",
                                a."SortName",
                                a."Comment",
                                a."MusicBrainzId",
                                'album_' || cast(a."ApiKey" as varchar(50)) as "CoverArt",
                                a."SongCount",
                                a."CreatedAt" as "CreatedRaw",
                                a."Duration"/1000 as "Duration",
                                a."PlayedCount",
                                'artist_' || cast(aa."ApiKey" as varchar(50)) as "ArtistId",
                                aa."Name" as "Artist",
                                DATE_PART('year', a."ReleaseDate"::date) as "Year",
                                a."Genres",
                                (SELECT "StarredAt" FROM "UserAlbums" WHERE "UserId" = @userId AND "AlbumId" = a."Id") as "Starred", 
                                (SELECT COUNT(*) FROM "UserAlbums" WHERE "IsStarred" AND "AlbumId" = a."Id") as "UserStarredCount"
                                FROM "Albums" a 
                                JOIN "Artists" aa on (a."ArtistId" = aa."Id")
                                JOIN "Libraries" l on (aa."LibraryId" = l."Id")
                                """;
                var whereSql = string.Empty;
                string orderSql;
                var limitSql =
                    $"OFFSET {albumListRequest.OffsetValue} ROWS FETCH NEXT {albumListRequest.SizeValue} ROWS ONLY;";
                switch (albumListRequest.Type)
                {
                    case ListType.Newest:
                        orderSql = "ORDER BY a.\"CreatedAt\" DESC nulls last";
                        break;

                    case ListType.Highest:
                        orderSql = "ORDER BY a.\"CalculatedRating\" DESC nulls last";
                        break;

                    case ListType.Frequent:
                        orderSql = "ORDER BY a.\"PlayedCount\" DESC nulls last";
                        break;

                    case ListType.Recent:
                        orderSql = "ORDER BY a.\"LastPlayedAt\" DESC nulls last ";
                        break;

                    case ListType.AlphabeticalByName:
                        orderSql = "ORDER BY a.\"SortName\"";
                        break;

                    case ListType.AlphabeticalByArtist:
                        orderSql = "ORDER BY aa.\"SortName\"";
                        break;

                    case ListType.Starred:
                        orderSql = "ORDER BY \"Starred\" DESC nulls last";
                        break;

                    case ListType.ByYear:
                        if (albumListRequest.FromYear < albumListRequest.ToYear)
                        {
                            whereSql = "where DATE_PART('year', a.\"ReleaseDate\"::date) between @fromYear AND @toYear";
                            orderSql = "ORDER BY \"Year\" DESC nulls last";
                        }
                        else
                        {
                            orderSql = "ORDER BY \"Year\" ASC nulls last";
                        }

                        break;

                    case ListType.ByGenre:
                        whereSql = "where @genre = any(a.\"Genres\")";
                        orderSql = "ORDER BY \"Genres\" DESC nulls last";
                        break;

                    default:
                        orderSql = "ORDER BY RANDOM()";
                        break;
                }

                sql = $"{selectSql} {whereSql} {orderSql} {limitSql}";
                data = await scopedContext.Database
                    .SqlQueryRaw<AlbumList2>(sql, sqlParameters.Values.Where(v => v != null).Cast<object>().ToArray())
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                var countSql = """
                      SELECT COUNT(a.*)
                      FROM "Albums" a 
                      JOIN "Artists" aa on (a."ArtistId" = aa."Id")
                      JOIN "Libraries" l on (aa."LibraryId" = l."Id")
                      """;
                var countSqlWithWhere = $"{countSql} {whereSql}";
                totalCount = await scopedContext.Database
                    .SqlQueryRaw<long>(countSqlWithWhere, sqlParameters.Values.Where(v => v != null).Cast<object>().ToArray())
                    .FirstAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            Logger.Debug("[{MethodName}] Total Count [{TotalCount}] Result Count [{Count}]",
                nameof(GetAlbumList2Async), totalCount, data.Length);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to get AlbumList2 SQL [{Sql}] Request [{ApiResult}]", sql, apiRequest);
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            TotalCount = totalCount,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "albumList2",
                DataDetailPropertyName = "album"
            }
        };
    }

    public async Task<ResponseModel> GetSongAsync(string apiKey, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var songId = ApiKeyFromId(apiKey) ?? Guid.Empty;
        if (songId == Guid.Empty)
        {
            Logger.Warning("Invalid song id [{SongId}] for Request [{Request}]", apiKey, apiRequest);
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                ResponseData = authResponse.ResponseData with
                {
                    Error = Error.InvalidApiKeyError
                }
            };
        }

        var songResponse = await songService.GetByApiKeyAsync(songId, cancellationToken);
        if (!songResponse.IsSuccess)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                ResponseData = authResponse.ResponseData with
                {
                    Error = Error.InvalidApiKeyError
                }
            };
        }

        var userSong = await userService.UserSongAsync(authResponse.UserInfo.Id, songId, cancellationToken);
        return new ResponseModel
        {
            IsSuccess = songResponse.IsSuccess,
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = songResponse.Data?.ToApiChild(songResponse.Data.Album, userSong),
                DataPropertyName = "song"
            }
        };
    }

    public async Task<ResponseModel> GetAlbumAsync(string apiId, ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        AlbumId3WithSongs? data = null;

        var apiKey = ApiKeyFromId(apiId);
        if (apiKey != null)
        {
            var albumResponse = await albumService.GetByApiKeyAsync(apiKey.Value, cancellationToken);
            if (!albumResponse.IsSuccess)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    ResponseData = authResponse.ResponseData with
                    {
                        Error = Error.InvalidApiKeyError
                    }
                };
            }


            var album = albumResponse.Data!;
            var userAlbum = await userService.UserAlbumAsync(authResponse.UserInfo.Id, apiKey.Value, cancellationToken);
            var userSongsForAlbum =
                await userService.UserSongsForAlbumAsync(authResponse.UserInfo.Id, apiKey.Value, cancellationToken) ??
                [];
            data = new AlbumId3WithSongs
            {
                AlbumDate = album.ReleaseDate.ToItemDate(),
                Artist = album.Artist.Name,
                ArtistId = album.Artist.ToApiKey(),
                Artists = album.ContributingArtists(),
                CoverArt = album.ToApiKey(),
                Created = album.CreatedAt.ToString(),
                DiscTitles = [],
                DisplayArtist = album.Artist.Name,
                Duration = album.Duration.ToSeconds(),
                Genre = album.Genres?.ToCsv(),
                Genres = album.Genres?.Select(x => new ItemGenre(x)).ToArray() ?? [],
                Id = album.ToApiKey(),
                IsCompilation = album.IsCompilation,
                Moods = album.Moods ?? [],
                MusicBrainzId = album.MusicBrainzId?.ToString(),
                Name = album.Name,
                OriginalAlbumDate = album.OriginalReleaseDate?.ToItemDate() ?? album.ReleaseDate.ToItemDate(),
                OriginalReleaseDate = album.OriginalReleaseDate?.ToItemDate() ?? album.ReleaseDate.ToItemDate(),
                Parent = album.ToApiKey(),
                PlayCount = album.PlayedCount,
                Played = album.LastPlayedAt.ToString(),
                RecordLabels = album.RecordLabels(),
                Song = album.Songs.OrderBy(x => x.SortOrder)
                    .Select(x => x.ToApiChild(album, userSongsForAlbum.FirstOrDefault(us => us.SongId == x.Id)))
                    .ToArray(),
                SongCount = album.SongCount ?? 0,
                SortName = album.SortName,
                Starred = userAlbum?.StarredAt?.ToString(),
                Title = album.Name,
                UserRating = userAlbum?.Rating,
                Year = album.ReleaseDate.Year
            };
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "album"
            }
        };
    }

    // TODO: This should be moved to AlbumService in the future.
    /// <summary>
    ///     Returns all genres.
    /// </summary>
    public async Task<ResponseModel> GetGenresAsync(ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = new List<Genre>();

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Get all albums and songs with their genres using EF Core
            var albums = await scopedContext.Albums
                .AsNoTracking()
                .Where(a => a.Genres != null && a.Genres.Length > 0)
                .Select(a => a.Genres)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            var songs = await scopedContext.Songs
                .AsNoTracking()
                .Where(s => s.Genres != null && s.Genres.Length > 0)
                .Select(s => s.Genres)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            // Flatten and collect all unique genres
            var allGenresSet = new HashSet<string>();
            var genreCounts = new Dictionary<string, (int songCount, int albumCount)>();

            // Process album genres
            foreach (var albumGenres in albums.Where(g => g != null))
            {
                foreach (var genre in albumGenres!)
                {
                    allGenresSet.Add(genre);
                    if (genreCounts.ContainsKey(genre))
                    {
                        genreCounts[genre] = (genreCounts[genre].songCount, genreCounts[genre].albumCount + 1);
                    }
                    else
                    {
                        genreCounts[genre] = (0, 1);
                    }
                }
            }

            // Process song genres  
            foreach (var songGenres in songs.Where(g => g != null))
            {
                foreach (var genre in songGenres!)
                {
                    allGenresSet.Add(genre);
                    if (genreCounts.ContainsKey(genre))
                    {
                        genreCounts[genre] = (genreCounts[genre].songCount + 1, genreCounts[genre].albumCount);
                    }
                    else
                    {
                        genreCounts[genre] = (1, 0);
                    }
                }
            }

            var allGenres = allGenresSet.OrderBy(g => g).ToArray();

            foreach (var genre in allGenres)
            {
                var genreNormalized = genre.ToNormalizedString() ?? genre;
                if (data.All(x => x.ValueNormalized != genreNormalized))
                {
                    var counts = genreCounts.TryGetValue(genre, out var count) ? count : (0, 0);
                    data.Add(new Genre
                        {
                            Value = genre.CleanString() ?? genre,
                            SongCount = counts.Item1,
                            AlbumCount = counts.Item2
                        }
                    );
                }
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = data,
                DataPropertyName = "genres",
                DataDetailPropertyName = "genre"
            }
        };
    }

    /// <summary>
    ///     Returns the avatar (personal image) for a user.
    /// </summary>
    public async Task<ResponseModel> GetAvatarAsync(
        string username,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        byte[]? avatarBytes = null;

        try
        {
            avatarBytes = await CacheManager.GetAsync($"urn:openSubsonic:avatar:{username}", async () =>
            {
                using (Operation.At(LogEventLevel.Debug).Time("GetAvatarAsync: [{Username}]", username))
                {
                    var userLibraryResult = await libraryService.GetUserImagesLibraryAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (userLibraryResult.IsSuccess)
                    {
                        var userAvatarFilename = authResponse.UserInfo.ToAvatarFileName(userLibraryResult.Data.Path);
                        if (File.Exists(userAvatarFilename))
                        {
                            return await File.ReadAllBytesAsync(userAvatarFilename, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }

                    return null;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to get avatar for user [{Username}]", username);
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = avatarBytes ?? defaultImages.UserAvatarBytes,
                DataPropertyName = string.Empty,
                DataDetailPropertyName = string.Empty
            }
        };
    }

    private static string GenerateImageCacheKeyForApiId(string apiId, ImageSize size)
    {
        return $"urn:openSubsonic:imageForApikey:{apiId}:{size}";
    }

    /// <summary>
    ///     Returns an artist, album, or song art image.
    /// </summary>
    public async Task<ResponseModel> GetImageForApiKeyId(
        string apiId,
        string? size,
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var isUserImageRequest = IsApiIdForUser(apiId);
        // If a user image request don't auth as it's used in the UI in the header (before auth'ed).
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess && !isUserImageRequest)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var isForPlaylist = IsApiIdForDynamicPlaylist(apiId) || IsApiIdForPlaylist(apiId);

        var badEtag = Instant.MinValue.ToEtag();
        var sizeValue = size.Nullify() == null ? ImageSize.Large : SafeParser.ToEnum<ImageSize>(size);

        var imageBytesAndEtag = await CacheManager.GetAsync(GenerateImageCacheKeyForApiId(apiId, sizeValue), async () =>
        {
            using (Operation.At(LogEventLevel.Debug)
                       .Time("GetImageForApiKeyId: [{Username}] Size [{Size}]", apiId, sizeValue))
            {
                var doCheckResize = true;
                byte[]? result = null;
                var eTag = string.Empty;
                try
                {
                    var apiKey = ApiKeyFromId(apiId);
                    if (apiKey == null)
                    {
                        return new ImageBytesAndEtag(null, null);
                    }

                    if (IsApiIdForArtist(apiId))
                    {
                        var artistImageBytesAndEtag = await artistService.GetArtistImageBytesAndEtagAsync(apiKey, size, cancellationToken);
                        result = artistImageBytesAndEtag.Bytes ?? defaultImages.ArtistBytes;
                        eTag = artistImageBytesAndEtag.Etag ?? badEtag;
                    }
                    else if (IsApiIdForDynamicPlaylist(apiId))
                    {
                        // Dynamic playlists don't exist in the database they are created on demand from configured json files.
                        var dynamicPlaylist = await libraryService
                            .GetDynamicPlaylistAsync(apiKey.Value, cancellationToken).ConfigureAwait(false);
                        var playlistImageFileInfo = new FileInfo(dynamicPlaylist.Data?.ImageFileName ?? string.Empty);
                        if (playlistImageFileInfo.Exists)
                        {
                            result = await File.ReadAllBytesAsync(playlistImageFileInfo.FullName, cancellationToken)
                                .ConfigureAwait(false);
                            eTag = playlistImageFileInfo.LastWriteTimeUtc.ToEtag();
                        }
                        else
                        {
                            result = defaultImages.PlaylistImageBytes;
                            eTag = badEtag;
                        }
                    }
                    else if (IsApiIdForPlaylist(apiId))
                    {
                        var playlistImageBytesAndEtag = await playlistService.GetPlaylistImageBytesAndEtagAsync(apiKey.Value, size, cancellationToken).ConfigureAwait(false);
                        result = playlistImageBytesAndEtag.Bytes ?? defaultImages.PlaylistImageBytes;
                        eTag = playlistImageBytesAndEtag.Etag ?? badEtag;
                    }
                    else if (isUserImageRequest)
                    {
                        var userResult = await userService.GetByApiKeyAsync(apiKey.Value, cancellationToken)
                            .ConfigureAwait(false);
                        var userImageLibrary = await libraryService.GetUserImagesLibraryAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var userImageFileName = userResult.Data?.ToAvatarFileName(userImageLibrary.Data.Path);
                        var userImageFileInfo = new FileInfo(userImageFileName ?? string.Empty);
                        if (userImageFileInfo.Exists)
                        {
                            result = await File.ReadAllBytesAsync(userImageFileInfo.FullName, cancellationToken)
                                .ConfigureAwait(false);
                            eTag = userImageFileInfo.LastWriteTimeUtc.ToEtag();
                        }
                        else
                        {
                            result = defaultImages.UserAvatarBytes;
                            eTag = badEtag;
                        }
                    }
                    else if (IsApiIdForSong(apiId) || IsApiIdForAlbum(apiId))
                    {
                        if (IsApiIdForSong(apiId))
                        {
                            // If it's a song get the album ApiKey and proceed to get Album cover
                            var songInfo = await DatabaseSongIdsInfoForSongApiKey(apiKey.Value, cancellationToken)
                                .ConfigureAwait(false);
                            if (songInfo != null)
                            {
                                apiKey = songInfo.AlbumApiKey;
                            }
                        }

                        var albumImageBytesAndEtag = await albumService.GetAlbumImageBytesAndEtagAsync(apiKey, size, cancellationToken);
                        result = albumImageBytesAndEtag.Bytes ?? defaultImages.AlbumCoverBytes;
                        eTag = albumImageBytesAndEtag.Etag ?? badEtag;
                    }

                    if (result != null && !isForPlaylist && doCheckResize)
                    {
                        if (sizeValue != ImageSize.Large)
                        {
                            var sizeParsedToInt = SafeParser.ToNumber<int>(size);
                            if (sizeParsedToInt > 0)
                            {
                                result = ImageConvertor.ResizeImageIfNeeded(result,
                                    sizeParsedToInt,
                                    sizeParsedToInt, isUserImageRequest);
                                eTag = HashHelper.CreateMd5(eTag + sizeParsedToInt);
                            }
                            else
                            {
                                switch (sizeValue)
                                {
                                    case ImageSize.Thumbnail:
                                        var thumbnailSize = (await Configuration.Value).GetValue<int?>(SettingRegistry.ImagingThumbnailSize) ?? SafeParser.ToNumber<int>(ImageSize.Thumbnail);
                                        result = ImageConvertor.ResizeImageIfNeeded(result,
                                            thumbnailSize,
                                            thumbnailSize,
                                            isUserImageRequest);
                                        eTag = HashHelper.CreateMd5(eTag + nameof(ImageSize.Thumbnail));
                                        break;

                                    case ImageSize.Small:
                                        var smallSize = (await Configuration.Value).GetValue<int?>(SettingRegistry.ImagingSmallSize) ??
                                                        throw new Exception($"Invalid configuration [{SettingRegistry.ImagingSmallSize}] not found.");
                                        result = ImageConvertor.ResizeImageIfNeeded(result,
                                            smallSize,
                                            smallSize,
                                            isUserImageRequest);
                                        eTag = HashHelper.CreateMd5(eTag + nameof(ImageSize.Small));
                                        break;

                                    case ImageSize.Medium:
                                        var mediumSize =
                                            (await Configuration.Value).GetValue<int?>(
                                                SettingRegistry.ImagingMediumSize) ??
                                            throw new Exception(
                                                $"Invalid configuration [{SettingRegistry.ImagingMediumSize}] not found.");
                                        result = ImageConvertor.ResizeImageIfNeeded(result,
                                            mediumSize,
                                            mediumSize,
                                            isUserImageRequest);
                                        eTag = HashHelper.CreateMd5(eTag + nameof(ImageSize.Medium));
                                        break;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to get cover image for [{ApiId}]", apiId);
                }

                return new ImageBytesAndEtag(result, eTag);
            }
        }, cancellationToken, new TimeSpan(0, 0, 120, 0), ImageCacheRegion);

        return new ResponseModel
        {
            ApiKeyId = apiId,
            IsSuccess = imageBytesAndEtag.Bytes != null,
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = imageBytesAndEtag.Bytes,
                DataPropertyName = string.Empty,
                DataDetailPropertyName = string.Empty,
                Etag = imageBytesAndEtag.Etag,
                ContentType = isForPlaylist ? "image/gif" : "image/jpeg"
            }
        };
    }

    /// <summary>
    ///     List the OpenSubsonic extensions supported by this server.
    ///     <remarks>Unlike all other APIs getOpenSubsonicExtensions must be publicly accessible.</remarks>
    /// </summary>
    public async Task<ResponseModel> GetOpenSubsonicExtensionsAsync(ApiRequest apiApiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
        };
        var data = new List<OpenSubsonicExtension>
        {
            // Custom extensions added for Melodee
            new("melodeeExtensions", [1]),
            // Add support for POST request to the API (application/x-www-form-urlencoded).
            new("apiKeyAuthentication", [1]),
            // Add support for POST request to the API (application/x-www-form-urlencoded).
            new("formPost", [1]),
            // add support for synchronized lyrics, multiple languages, and retrieval by song ID
            new("songLyrics", [1]),
            // Add support for start offset for transcoding.
            new("transcodeOffset", [1])
        };
        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = data,
                DataPropertyName = "openSubsonicExtensions"
            }
        };
    }

    public async Task<ResponseModel> StartScanAsync(ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        await schedule.TriggerJob(JobKeyRegistry.LibraryProcessJobJobKey, cancellationToken);

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = new ScanStatus(true, 0),
                DataPropertyName = "scanStatus"
            }
        };
    }

    public async Task<ResponseModel> GetScanStatusAsync(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var executingJobs = await schedule.GetCurrentlyExecutingJobs(cancellationToken);
        var libraryProcessJob =
            executingJobs.FirstOrDefault(x => Equals(x.JobDetail.Key, JobKeyRegistry.LibraryProcessJobJobKey));

        var data = new ScanStatus(false, 0);
        try
        {
            if (libraryProcessJob != null)
            {
                var dataMap = libraryProcessJob.JobDetail.JobDataMap;
                if (dataMap.ContainsKey(JobMapNameRegistry.ScanStatus) && dataMap.ContainsKey(JobMapNameRegistry.Count))
                {
                    data = new ScanStatus(
                        dataMap.GetString(JobMapNameRegistry.ScanStatus) == nameof(Enums.ScanStatus.InProcess),
                        dataMap.GetIntValue(JobMapNameRegistry.Count));
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Attempting to get Scan Status");
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = data,
                DataPropertyName = "scanStatus"
            }
        };
    }

    /// <summary>
    ///     Test connectivity with the server.
    ///     <remarks>
    ///         This method does NOT do authentication as its only purpose is for a 'test' for the consumer to get a result
    ///         from the server.
    ///     </remarks>
    /// </summary>
    public async Task<ResponseModel> PingAsync(ApiRequest apiRequest, CancellationToken cancellationToken = default)
    {
        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
        };
    }

    public async Task<ResponseModel> AuthenticateSubsonicApiAsync(ApiRequest apiRequest, CancellationToken cancellationToken = default)
    {
        if (!apiRequest.RequiresAuthentication)
        {
            var user = apiRequest.Username == null
                ? null
                : await userService.GetByUsernameAsync(apiRequest.Username, cancellationToken).ConfigureAwait(false);
            return new ResponseModel
            {
                UserInfo = user?.Data?.ToUserInfo() ?? UserInfo.BlankUserInfo,
                ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
            };
        }

        if (apiRequest.Username?.Nullify() == null ||
            (apiRequest.Password?.Nullify() == null &&
             apiRequest.Token?.Nullify() == null))
        {
            Logger.Warning("[{MethodName}] [{ApiRequest}] is invalid",
                nameof(AuthenticateSubsonicApiAsync),
                apiRequest.ToString());
            return new ResponseModel
            {
                UserInfo = new UserInfo(0, Guid.Empty, string.Empty, string.Empty, string.Empty, string.Empty),
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
            };
        }

        using (Operation.At(LogEventLevel.Debug)
                   .Time("AuthenticateSubsonicApiAsync: username [{Username}]", apiRequest.Username))
        {
            try
            {
                OperationResult<Data.Models.User?> loginResult;

                if (apiRequest.Jwt.Nullify() != null)
                {
                    // see https://github.com/navidrome/navidrome/blob/acce3c97d5dcf22a005a46d855bb1763a8bb8b66/server/subsonic/middlewares.go#L132
                    throw new NotImplementedException();
                }

                if (apiRequest.Token?.Nullify() != null && apiRequest.Salt?.Nullify() != null)
                {
                    // Use existing token validation method
                    loginResult = await userService.ValidateTokenAsync(apiRequest.Username, apiRequest.Token, apiRequest.Salt, cancellationToken);
                }
                else
                {
                    // Use existing password authentication
                    var password = apiRequest.Password;
                    if (apiRequest.Password?.StartsWith("enc:", StringComparison.Ordinal) ?? false)
                    {
                        password = password?.FromHexString();
                    }
                    loginResult = await userService.LoginUserByUsernameAsync(apiRequest.Username, password, cancellationToken);
                }

                return new ResponseModel
                {
                    UserInfo = loginResult.Data?.ToUserInfo() ?? UserInfo.BlankUserInfo,
                    IsSuccess = loginResult.IsSuccess,
                    ResponseData = await NewApiResponse(loginResult.IsSuccess, string.Empty, string.Empty, loginResult.IsSuccess ? null : Error.AuthError)
                };
            }
            catch (Exception e)
            {
                Logger.Error(e, "Error authenticating user, request [{ApiRequest}]", apiRequest);
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
                };
            }
        }
    }

    private Task<ApiResponse> DefaultApiResponse()
    {
        return NewApiResponse(true, string.Empty, string.Empty);
    }

    public async Task<ApiResponse> NewApiResponse(bool isOk, string dataPropertyName, string dataDetailPropertyName,
        Error? error = null, object? data = null)
    {
        return new ApiResponse
        {
            IsSuccess = isOk,
            Version =
                (await Configuration.Value).GetValue<string>(SettingRegistry.OpenSubsonicServerSupportedVersion) ??
                throw new InvalidOperationException(),
            Type = (await Configuration.Value).GetValue<string>(SettingRegistry.OpenSubsonicServerType) ??
                   throw new InvalidOperationException(),
            ServerVersion = (await Configuration.Value).GetValue<string>(SettingRegistry.OpenSubsonicServerVersion) ??
                            throw new InvalidOperationException(),
            Error = error,
            Data = data,
            DataDetailPropertyName = dataDetailPropertyName,
            DataPropertyName = dataPropertyName
        };
    }

    // TODO: This should be moved to UserQueueService in the future.
    public async Task<ResponseModel> GetPlayQueueAsync(ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var user = await userService.GetByUsernameAsync(apiRequest.Username!, cancellationToken)
                .ConfigureAwait(false);
            var usersPlayQues = await scopedContext
                .PlayQues.Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == user.Data!.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            var current = usersPlayQues.FirstOrDefault(x => x.IsCurrentSong);
            var data = new PlayQueue
            {
                Current = current?.PlayQueId ?? 0,
                Position = current?.Position ?? 0,
                ChangedBy = current?.ChangedBy ?? user.Data!.UserName,
                Changed = current?.LastUpdatedAt.ToString() ?? string.Empty,
                Username = user.Data!.UserName,
                Entry = usersPlayQues.Select(x => x.Song.ToApiChild(x.Song.Album, null)).ToArray()
            };

            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                ResponseData = authResponse.ResponseData with
                {
                    Data = current == null ? null : data,
                    DataPropertyName = "playQueue"
                }
            };
        }
    }

    // TODO: This should be moved to UserQueueService in the future.
    public async Task<ResponseModel> SavePlayQueueAsync(string[]? apiIds, string? currentApiId, double? position,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        bool result;
        var apiKeys = apiIds?.Select(x => ApiKeyFromId(x)!.Value).ToArray();
        var current = ApiKeyFromId(currentApiId);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // If the apikey is blank then remove any current saved que
            if (apiKeys == null)
            {
                var user = await userService.GetByUsernameAsync(apiRequest.Username!, cancellationToken).ConfigureAwait(false);
                if (user.IsSuccess && user.Data != null)
                {
                    var playQuesToDelete = await scopedContext.PlayQues
                        .Where(pq => pq.UserId == user.Data.Id)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                    
                    scopedContext.PlayQues.RemoveRange(playQuesToDelete);
                    await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                result = true;
            }
            else
            {
                var foundQuesSongApiKeys = new List<Guid>();
                var user = await userService.GetByUsernameAsync(apiRequest.Username!, cancellationToken)
                    .ConfigureAwait(false);
                var usersPlayQues = await scopedContext
                    .PlayQues.Include(x => x.Song)
                    .Where(x => x.UserId == user.Data!.Id)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
                var changedByValue = apiRequest.ApiRequestPlayer.Client ?? user.Data!.UserName;
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
                            UserId = user.Data!.Id,
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
                result = true;
            }
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty, result ? null : Error.AuthError)
        };
    }

    public async Task<ResponseModel> CreateUserAsync(CreateUserRequest request, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var requirePrivateCode = (await Configuration.Value).GetValue<string>(SettingRegistry.RegisterPrivateCode);
        if (requirePrivateCode.Nullify() != null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty,
                    new Error(10, "Private code is configured. User registration must be done via the server."))
            };
        }

        var registerResult = await userService
            .RegisterAsync(request.Username, request.Email, request.Password, null, cancellationToken)
            .ConfigureAwait(false);
        var result = registerResult.IsSuccess;
        if (!result)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = result,
                ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                    new Error(10, "User creation failed."))
            };
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty, result ? null : Error.AuthError)
        };
    }

    public async Task<ResponseModel> ScrobbleAsync(string[] ids, double[]? times, bool? submission,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        if (times?.Length > 0 && times.Length != ids.Length)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty,
                    Error.GenericError("Wrong number of timestamps."))
            };
        }

        await scrobbleService.InitializeAsync(await Configuration.Value, cancellationToken).ConfigureAwait(false);

        // If not provided then default to this is a "submission" versus a "now playing" notification.
        var isSubmission = submission ?? true;

        if (!isSubmission)
        {
            foreach (var idAndIndex in ids.Select((id, index) => new { id, index }))
            {
                await scrobbleService.NowPlaying(authResponse.UserInfo, ApiKeyFromId(idAndIndex.id) ?? Guid.Empty,
                    times?.Length > idAndIndex.index ? times[idAndIndex.index] : null,
                    apiRequest.ApiRequestPlayer?.Client ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            foreach (var idAndIndex in ids.Select((id, index) => new { id, index }))
            {
                var id = ApiKeyFromId(idAndIndex.id) ?? Guid.Empty;
                var uniqueId = SafeParser.Hash(authResponse.UserInfo.ApiKey.ToString(), id.ToString());
                var nowPlayingInfo =
                    (await scrobbleService.GetNowPlaying(cancellationToken).ConfigureAwait(false)).Data
                    .FirstOrDefault(x => x.UniqueId == uniqueId);
                if (nowPlayingInfo != null)
                {
                    await scrobbleService.Scrobble(authResponse.UserInfo,
                            id,
                            false,
                            apiRequest.ApiRequestPlayer.Client ?? string.Empty,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    Logger.Debug("Scrobble: Ignoring duplicate scrobble submission for [{UniqueId}]", uniqueId);
                }
            }
        }

        var result = true;
        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty, result ? null : Error.AuthError)
        };
    }

    /// <summary>
    ///     Get bytes for song with support for chunking from request header values.
    /// </summary>
    public async Task<StreamResponse> StreamAsync(StreamRequest request, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return new StreamResponse(new HeaderDictionary(), false, []);
        }

        if (request.IsDownloadingRequest)
        {
            var isDownloadingEnabled =
                (await Configuration.Value).GetValue<bool?>(SettingRegistry.SystemIsDownloadingEnabled) ?? false;
            if (!isDownloadingEnabled)
            {
                Logger.Warning("[{ServiceName}] Downloading is disabled [{SettingName}]. Request [{Request}",
                    nameof(OpenSubsonicApiService),
                    SettingRegistry.SystemIsDownloadingEnabled,
                    request);
                return new StreamResponse(new HeaderDictionary(), false, []);
            }
        }

        if (request is { IsDownloadingRequest: false, TimeOffset: not null })
        {
            Logger.Warning("[{ServiceName}] Stream request has TimeOffset. Request [{Request}",
                nameof(OpenSubsonicApiService), request);
            throw new NotImplementedException();
        }

        var apiKey = ApiKeyFromId(request.Id);
        if (apiKey == null)
        {
            Logger.Warning("[{ServiceName}] Invalid song ID [{Id}]", nameof(OpenSubsonicApiService), request.Id);
            return new StreamResponse(new HeaderDictionary(), false, []);
        }

        // Parse range header
        long rangeBegin = 0;
        long rangeEnd = 0;
        var range = apiRequest.RequestHeaders.FirstOrDefault(x => x.Key == "Range")?.Value ?? string.Empty;
        if (!request.IsDownloadingRequest && range.Nullify() != null)
        {
            if (string.Equals(range, "bytes=0-", StringComparison.OrdinalIgnoreCase))
            {
                long.TryParse(range, out rangeBegin);
            }
            else
            {
                var rangeParts = range.Split('-');
                long.TryParse(rangeParts[0], out rangeBegin);
                if (rangeParts.Length > 1)
                {
                    long.TryParse(rangeParts[1], out rangeEnd);
                }
            }
        }

        // Use SongService for streaming
        var streamResult = await songService.GetStreamForSongAsync(
            authResponse.UserInfo,
            apiKey.Value,
            rangeBegin,
            rangeEnd,
            request.Format,
            request.MaxBitRate,
            request.IsDownloadingRequest,
            cancellationToken);

        if (!streamResult.IsSuccess)
        {
            Logger.Warning("[{ServiceName}] Failed to get stream for song [{ApiKey}]: {Message}",
                nameof(OpenSubsonicApiService), apiKey.Value, streamResult.Messages?.FirstOrDefault());
            return new StreamResponse(new HeaderDictionary(), false, []);
        }

        // Send stream event for scrobbling
        await bus.SendLocal(new UserStreamEvent(apiRequest, request)).ConfigureAwait(false);

        return streamResult.Data;
    }

    public async Task<ResponseModel> GetNowPlayingAsync(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var nowPlaying = await scrobbleService.GetNowPlaying(cancellationToken).ConfigureAwait(false);
        var data = new List<Child>();
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var nowPlayingSongApiKeys = nowPlaying.Data.Select(x => x.Scrobble.SongApiKey).ToList();
            var nowPlayingSongs = await (from s in scopedContext
                        .Songs.Include(x => x.Album)
                    where nowPlayingSongApiKeys.Contains(s.ApiKey)
                    select s)
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            var nowPlayingSongIds = nowPlayingSongs.Select(x => x.Id).ToArray();
            var nowPlayingAlbumIds = nowPlayingSongs.Select(x => x.AlbumId).Distinct().ToArray();
            var nowPlayingSongsAlbums = await (from a in scopedContext.Albums.Include(x => x.Artist)
                    where nowPlayingAlbumIds.Contains(a.Id)
                    select a)
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            var nowPlayingUserSongs = await (from us in scopedContext.UserSongs
                    where us.UserId == authResponse.UserInfo.Id
                    where nowPlayingSongIds.Contains(us.Id)
                    select us)
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var nowPlayingSong in nowPlayingSongs)
            {
                var album = nowPlayingSongsAlbums.First(x => x.Id == nowPlayingSong.AlbumId);
                var userSong = nowPlayingUserSongs.FirstOrDefault(x => x.SongId == nowPlayingSong.Id);
                var nowPlayingSongUniqueId = SafeParser.Hash(authResponse.UserInfo.ApiKey.ToString(),
                    nowPlayingSong.ApiKey.ToString());
                data.Add(nowPlayingSong.ToApiChild(album, userSong,
                    nowPlaying.Data.FirstOrDefault(x => x.UniqueId == nowPlayingSongUniqueId)));
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "nowPlaying",
                DataDetailPropertyName = "entry"
            }
        };
    }

    public async Task<ResponseModel> SearchAsync(SearchRequest request, bool isSearch3, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        long totalCount = 0;
        var defaultPageSize = (await Configuration.Value).GetValue<short>(SettingRegistry.SearchEngineDefaultPageSize);
        var maxAllowedPageSize = (await Configuration.Value).GetValue<short>(SettingRegistry.SearchEngineMaximumAllowedPageSize);

        var artistOffset = request.ArtistOffset ?? 0;
        if (artistOffset < 0) artistOffset = defaultPageSize;

        var artistCount = request.ArtistCount ?? defaultPageSize;
        if (artistCount > maxAllowedPageSize) artistCount = maxAllowedPageSize;

        var albumOffset = request.AlbumOffset ?? 0;
        if (albumOffset < 0) albumOffset = defaultPageSize;

        var albumCount = request.AlbumCount ?? defaultPageSize;
        if (albumCount > maxAllowedPageSize) albumCount = maxAllowedPageSize;

        var songOffset = request.SongOffset ?? 0;
        if (songOffset < 0) songOffset = defaultPageSize;

        var songCount = request.SongCount ?? defaultPageSize;
        if (songCount > maxAllowedPageSize) songCount = maxAllowedPageSize;

        // Handle total count requests for empty queries
        if (request.Query.Nullify() == null)
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            if (request.AlbumCount == 1)
            {
                totalCount = await scopedContext.Albums.LongCountAsync(cancellationToken);
            }
            else if (request.ArtistCount == 1)
            {
                totalCount = await scopedContext.Artists.LongCountAsync(cancellationToken);
            }
            else if (request.SongCount == 1)
            {
                totalCount = await scopedContext.Songs.LongCountAsync(cancellationToken);
            }
        }

        // Use SearchService instead of raw SQL
        var searchResult = await searchService.DoOpenSubsonicSearchAsync(
            authResponse.UserInfo.ApiKey,
            request.Query,
            artistOffset,
            artistCount,
            albumOffset,
            albumCount,
            songOffset,
            songCount,
            cancellationToken);

        if (!searchResult.IsSuccess)
        {
            Logger.Warning("[{ServiceName}] Search failed: {Message}", nameof(OpenSubsonicApiService), 
                searchResult.Messages?.FirstOrDefault());
            return new ResponseModel
            {
                UserInfo = authResponse.UserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
            };
        }

        // Convert domain objects to OpenSubsonic search results
        var artists = searchResult.Data.Artists.Select(ToArtistSearchResult).ToArray();
        var albums = searchResult.Data.Albums.Select(ToAlbumSearchResult).ToArray();  
        var songs = searchResult.Data.Songs.Select(ToSongSearchResult).ToArray();

        if (albums.Length == 0 && songs.Length == 0 && artists.Length == 0)
        {
            Logger.Information("! No result for query [{Query}] Normalized [{QueryNormalized}]", request.QueryValue,
                request.QueryNormalizedValue);
        }

        return new ResponseModel
        {
            TotalCount = totalCount,
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = isSearch3
                    ? new SearchResult3(artists, albums, songs)
                    : new SearchResult2(artists, albums, songs),
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty :
                isSearch3 ? "searchResult3" : "searchResult2"
            }
        };
    }


    public async Task<ResponseModel> GetMusicDirectoryAsync(string apiId, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Directory? data = null;

        var apiKey = ApiKeyFromId(apiId);
        if (IsApiIdForArtist(apiId) && apiKey != null)
        {
            var artistInfo =
                await DatabaseArtistInfoForArtistApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken);
            if (artistInfo != null)
            {
                await using (var scopedContext =
                             await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var artistAlbums = await scopedContext
                        .Albums
                        .Include(x => x.Artist)
                        .Include(x => x.UserAlbums.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                        .Where(x => x.ArtistId == artistInfo.Id)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                    data = new Directory(artistInfo.CoverArt,
                        null,
                        artistInfo.Name,
                        artistInfo.UserStarred.ToString(),
                        artistInfo.UserRating,
                        artistInfo.CalculatedRating,
                        artistInfo.PlayCount,
                        artistInfo.Played.ToString(),
                        artistAlbums.Select(x => x.ToApiChild(x.UserAlbums.FirstOrDefault())).ToArray());
                }
            }
        }
        else if (IsApiIdForAlbum(apiId) && apiKey != null)
        {
            var albumInfo =
                await DatabaseAlbumInfoForAlbumApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken);
            if (albumInfo != null)
            {
                var albumSongInfos =
                    await DatabaseSongInfosForAlbumApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken);
                if (albumSongInfos != null)
                {
                    await using (var scopedContext =
                                 await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var songIds = albumSongInfos.Select(x => x.Id).ToArray();
                        var albumSongs = await scopedContext
                            .Songs
                            .Include(x => x.Album).ThenInclude(x => x.Artist)
                            .Include(x => x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                            .Where(x => songIds.Contains(x.Id))
                            .ToArrayAsync(cancellationToken)
                            .ConfigureAwait(false);
                        data = new Directory(albumInfo.CoverArt,
                            albumInfo.CoverArt,
                            albumInfo.Name,
                            albumInfo.UserStarred.ToString(),
                            albumInfo.UserRating,
                            albumInfo.CalculatedRating,
                            albumInfo.PlayCount,
                            albumInfo.Played.ToString(),
                            albumSongs.Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray());
                    }
                }
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "directory"
            }
        };
    }


    public async Task<ResponseModel> GetIndexesAsync(bool isArtistIndex, string dataPropertyName, Guid? musicFolderId,
        long? ifModifiedSince, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var indexLimit = (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
        if (indexLimit == 0)
        {
            indexLimit = short.MaxValue;
        }

        object? data;
        var libraryId = 0;
        var lastModified = string.Empty;
        if (musicFolderId.HasValue)
        {
            var libraryResult =
                await libraryService.ListAsync(new PagedRequest(), cancellationToken).ConfigureAwait(false);
            var library = libraryResult.Data.FirstOrDefault(x => x.ApiKey == musicFolderId.Value);
            libraryId = library?.Id ?? 0;
            lastModified = library?.LastUpdatedAt.ToString() ?? string.Empty;
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Build EF Core query for artist indexes
            var artistQuery = scopedContext.Artists
                .Include(a => a.UserArtists.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                .AsNoTracking()
                .Where(a => libraryId == 0 || a.LibraryId == libraryId)
                .OrderBy(a => a.SortOrder)
                .ThenBy(a => a.SortName);

            var artists = await artistQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var indexes = artists.Select(a =>
            {
                var userArtist = a.UserArtists.FirstOrDefault();
                return new DatabaseDirectoryInfo(
                    a.Id,
                    a.ApiKey,
                    a.SortName?.Length > 0 ? a.SortName.Substring(0, 1) : string.Empty,
                    a.Name,
                    $"artist_{a.ApiKey}",
                    a.CalculatedRating,
                    a.AlbumCount,
                    a.PlayedCount,
                    a.CreatedAt,
                    a.LastUpdatedAt,
                    a.LastPlayedAt,
                    a.Directory,
                    userArtist?.IsStarred == true ? userArtist.StarredAt : null,
                    userArtist?.Rating
                );
            }).ToArray();

            var configuration = await Configuration.Value;

            var artistIndexes = new List<ArtistIndex>();
            foreach (var grouped in indexes.GroupBy(x => x.Index))
            {
                var aa = new List<Artist>();
                foreach (var info in grouped)
                {
                    aa.Add(new Artist(info.CoverArt,
                        info.Name,
                        info.AlbumCount,
                        info.UserRatingValue,
                        info.CalculatedRating,
                        info.CoverArt,
                        configuration.GenerateImageUrl(info.CoverArt, ImageSize.Large),
                        info.UserStarred?.ToString()));
                }

                artistIndexes.Add(new ArtistIndex(grouped.Key, aa.Take(indexLimit).ToArray()));
            }

            if (!isArtistIndex)
            {
                data = new Indexes(
                    (await Configuration.Value).GetValue<string>(SettingRegistry.ProcessingIgnoredArticles) ??
                    string.Empty, lastModified,
                    [],
                    artistIndexes.ToArray(),
                    []);
            }
            else
            {
                data = new Artists(
                    (await Configuration.Value).GetValue<string>(SettingRegistry.ProcessingIgnoredArticles) ??
                    string.Empty, lastModified,
                    artistIndexes.ToArray());
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : dataPropertyName
            }
        };
    }

    /// <summary>
    ///     Returns all configured top-level music folders.
    /// </summary>
    public async Task<ResponseModel> GetMusicFolders(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        NamedInfo[] data = [];

        var libraryResult = await libraryService.ListAsync(new PagedRequest(), cancellationToken).ConfigureAwait(false);
        if (libraryResult.IsSuccess)
        {
            data = libraryResult.Data.Where(x => x.TypeValue == LibraryType.Storage)
                .Select(x => new NamedInfo(x.ToApiKey(), x.Name)).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "musicFolders",
                DataDetailPropertyName = "musicFolder"
            }
        };
    }

    public async Task<ResponseModel> GetArtistAsync(string id, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Artist? data = null;

        var apiKey = ApiKeyFromId(id);
        if (apiKey != null)
        {
            var artistInfo =
                await DatabaseArtistInfoForArtistApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken)
                    .ConfigureAwait(false);
            if (artistInfo != null)
            {
                var configuration = await Configuration.Value;
                data = new Artist(
                    id,
                    artistInfo.Name,
                    artistInfo.AlbumCount,
                    artistInfo.UserRating,
                    artistInfo.CalculatedRating,
                    artistInfo.CoverArt,
                    configuration.GenerateImageUrl(id, ImageSize.Large),
                    artistInfo.UserStarred?.ToString(),
                    await AlbumListForArtistApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken)
                        .ConfigureAwait(false));
            }
            else
            {
                Logger.Warning("[{MethodName}] invalid artist id [{Id}] ApiRequest [{ApiRequest}]",
                    nameof(GetArtistAsync), id, apiRequest.ToString());
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            TotalCount = data?.AlbumCount ?? 0,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "artist"
            }
        };
    }

    /// <summary>
    ///     Toggles a star to a song, album, or artist.
    /// </summary>
    public async Task<ResponseModel> ToggleStarAsync(bool isStarred, string? id, string? albumId, string? artistId,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var result = false;
        var idValue = id ?? albumId ?? artistId;
        var apiKey = ApiKeyFromId(idValue);
        if (apiKey != null)
        {
            if (IsApiIdForArtist(idValue))
            {
                result = (await userService
                    .ToggleAristStarAsync(authResponse.UserInfo.Id, apiKey.Value, isStarred, cancellationToken)
                    .ConfigureAwait(false)).Data;
            }

            if (IsApiIdForAlbum(idValue))
            {
                result = (await userService
                    .ToggleAlbumStarAsync(authResponse.UserInfo.Id, apiKey.Value, isStarred, cancellationToken)
                    .ConfigureAwait(false)).Data;
            }

            if (IsApiIdForSong(idValue))
            {
                result = (await userService
                    .ToggleSongStarAsync(authResponse.UserInfo.Id, apiKey.Value, isStarred, cancellationToken)
                    .ConfigureAwait(false)).Data;
            }
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                result ? null : Error.InvalidApiKeyError)
        };
    }

    /// <summary>
    ///     Sets the rating for a music file.
    /// </summary>
    public async Task<ResponseModel> SetRatingAsync(string id, int rating, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        OperationResult<bool> result;

        if (IsApiIdForSong(id))
        {
            result = await userService.SetSongRatingAsync(authResponse.UserInfo.Id, apiKey.Value, rating, cancellationToken);
        }
        else if (IsApiIdForAlbum(id))
        {
            result = await userService.SetAlbumRatingAsync(authResponse.UserInfo.Id, apiKey.Value, rating, cancellationToken);
        }
        else if (IsApiIdForArtist(id))
        {
            result = await userService.SetArtistRatingAsync(authResponse.UserInfo.Id, apiKey.Value, rating, cancellationToken);
        }
        else
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result.IsSuccess,
            ResponseData = await NewApiResponse(result.IsSuccess, string.Empty, string.Empty,
                result.IsSuccess ? null : Error.InvalidApiKeyError)
        };
    }

    public async Task<ResponseModel> GetTopSongsAsync(string artist, int? count, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Child[]? data;

        await artistSearchEngineService.InitializeAsync(await Configuration.Value, cancellationToken)
            .ConfigureAwait(false);

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var artistId = await scopedContext.Artists.Where(x => x.Name == artist).Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var topSongsResult = await artistSearchEngineService
                .DoArtistTopSongsSearchAsync(artist, artistId, count, cancellationToken).ConfigureAwait(false);
            var songIds = topSongsResult.Data.Where(x => x.Id != null).Select(x => x.Id).ToArray();
            var songs = await scopedContext
                .Songs.Include(x => x.Album).ThenInclude(x => x.Artist)
                .Include(x => x.UserSongs.Where(us => us.UserId == authResponse.UserInfo.Id))
                .Where(x => songIds.Contains(x.Id)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            data = (from s in songs
                    join tsr in topSongsResult.Data on s.Id equals tsr.Id
                    orderby tsr.SortOrder
                    select s
                ).Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "topSongs",
                DataDetailPropertyName = "song"
            }
        };
    }

    public async Task<ResponseModel> GetStarred2Async(string? musicFolderId, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        ArtistID3[] artists;
        AlbumID3[] albums;
        Child[] songs;

        var indexLimit = (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
        if (indexLimit == 0)
        {
            indexLimit = short.MaxValue;
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var userStarredArtists = await scopedContext
                .UserArtists.Include(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            artists = userStarredArtists.Select(x => x.Artist.ToApiArtistID3(x)).ToArray();

            var userStarredAlbums = await scopedContext
                .UserAlbums.Include(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            albums = userStarredAlbums.Select(x => x.Album.ToArtistID3(x, null)).ToArray();

            var userStarredSongs = await scopedContext
                .UserSongs.Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            songs = userStarredSongs.Select(x => x.Song.ToApiChild(x.Song.Album, x)).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = new StarredInfo2(artists, albums, songs),
                DataPropertyName = "starred2"
            }
        };
    }

    public async Task<ResponseModel> GetStarredAsync(string? musicFolderId, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Artist[] artists;
        Child[] albums;
        Child[] songs;

        var indexLimit = (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
        if (indexLimit == 0)
        {
            indexLimit = short.MaxValue;
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var userStarredArtists = await scopedContext
                .UserArtists.Include(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            artists = userStarredArtists.Select(x => x.Artist.ToApiArtist(x)).ToArray();

            var userStarredAlbums = await scopedContext
                .UserAlbums.Include(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            albums = userStarredAlbums.Select(x => x.Album.ToApiChild(x)).ToArray();

            var userStarredSongs = await scopedContext
                .UserSongs.Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            songs = userStarredSongs.Select(x => x.Song.ToApiChild(x.Song.Album, x)).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = new StarredInfo(artists, albums, songs),
                DataPropertyName = "starred"
            }
        };
    }

    public async Task<ResponseModel> GetSongsByGenreAsync(string genre, int? count, int? offset, string? musicFolderId,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var indexLimit = (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
        if (indexLimit == 0)
        {
            indexLimit = short.MaxValue;
        }

        long totalCount;
        Child[] songs;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Build EF Core query for songs by genre
            var songQuery = scopedContext.Songs
                .Include(x => x.Album).ThenInclude(x => x.Artist)
                .Include(x => x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                .AsNoTracking()
                .Where(s => (s.Genres != null && s.Genres.Contains(genre)) || 
                           (s.Album.Genres != null && s.Album.Genres.Contains(genre)));

            // Get total count
            totalCount = await songQuery.CountAsync(cancellationToken).ConfigureAwait(false);

            // Apply pagination and get songs
            var takeSize = (count ?? indexLimit) < indexLimit ? (count ?? indexLimit) : indexLimit;
            var dbSongs = await songQuery
                .Skip(offset ?? 0)
                .Take(takeSize)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = dbSongs.Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            TotalCount = totalCount,
            ResponseData = await DefaultApiResponse() with
            {
                Data = songs,
                DataPropertyName = "songsByGenre",
                DataDetailPropertyName = "song"
            }
        };
    }

    public async Task<ResponseModel> GetBookmarksAsync(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var bookmarksResult = await userService.GetBookmarksAsync(authResponse.UserInfo.Id, cancellationToken).ConfigureAwait(false);
        
        Bookmark[] data = [];
        if (bookmarksResult.IsSuccess)
        {
            data = bookmarksResult.Data.Select(x => x.ToApiBookmark()).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "bookmarks",
                DataDetailPropertyName = "bookmark"
            }
        };
    }

    public async Task<ResponseModel> CreateBookmarkAsync(string id, int position, string? comment,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        var bookmarkResult = await userService.CreateBookmarkAsync(authResponse.UserInfo.Id, apiKey.Value, position, comment, cancellationToken).ConfigureAwait(false);

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = bookmarkResult.IsSuccess,
            ResponseData = await NewApiResponse(bookmarkResult.IsSuccess, string.Empty, string.Empty,
                bookmarkResult.IsSuccess ? null : Error.InvalidApiKeyError)
        };
    }

    public async Task<ResponseModel> DeleteBookmarkAsync(string id, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        var deleteResult = await userService.DeleteBookmarkAsync(authResponse.UserInfo.Id, apiKey.Value, cancellationToken).ConfigureAwait(false);

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = deleteResult.IsSuccess,
            ResponseData = await NewApiResponse(deleteResult.IsSuccess, string.Empty, string.Empty,
                deleteResult.IsSuccess ? null : Error.InvalidApiKeyError)
        };
    }

    // TODO: This should be moved to ArtistService in the future.
    public async Task<ResponseModel> GetArtistInfoAsync(string id,
        int? numberOfSimilarArtistsToReturn,
        bool isArtistInfo2,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        ArtistInfo? data = null;

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var apiKey = ApiKeyFromId(id);
            var artist = await scopedContext.Artists.FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
                .ConfigureAwait(false);
            if (artist != null)
            {
                var configuration = await Configuration.Value;

                Artist[]? similarArtists = null;
                if (numberOfSimilarArtistsToReturn > 0)
                {
                    var similarArtistRelationType = SafeParser.ToNumber<int>(ArtistRelationType.Similar);
                    var similarDbArtists = await scopedContext.ArtistRelation
                        .Include(x => x.RelatedArtist)
                        .Where(x => x.ArtistId == artist.Id)
                        .Where(x => x.ArtistRelationType == similarArtistRelationType)
                        .OrderBy(x => x.Artist.SortName)
                        .Take(numberOfSimilarArtistsToReturn.Value)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (similarDbArtists.Any())
                    {
                        similarArtists = similarDbArtists.Select(x => x.RelatedArtist.ToApiArtist()).ToArray();
                    }
                }

                data = new ArtistInfo(artist.ToApiKey(),
                    artist.Name,
                    configuration.GenerateImageUrl(id, ImageSize.Thumbnail),
                    configuration.GenerateImageUrl(id, ImageSize.Medium),
                    configuration.GenerateImageUrl(id, ImageSize.Large),
                    artist.SongCount,
                    artist.AlbumCount,
                    artist.Biography,
                    artist.MusicBrainzId,
                    artist.LastFmId,
                    similarArtists,
                    isArtistInfo2);
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : isArtistInfo2 ? "artistInfo2" : "artistInfo"
            }
        };
    }

    // TODO: This should be moved to AlbumService in the future.
    public async Task<ResponseModel> GetAlbumInfoAsync(string id, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        AlbumInfo? data = null;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var apiKey = ApiKeyFromId(id);
            if (IsApiIdForSong(id))
            {
                // Some players send the first song to get an albums details. No idea why. 
                var songApiKey = ApiKeyFromId(id);
                if (songApiKey != null)
                {
                    var songInfo = await DatabaseSongIdsInfoForSongApiKey(songApiKey.Value, cancellationToken)
                        .ConfigureAwait(false);
                    apiKey = songInfo?.AlbumApiKey ?? apiKey;
                }
            }

            var album = await scopedContext.Albums.FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
                .ConfigureAwait(false);
            if (album != null)
            {
                var configuration = await Configuration.Value;

                data = new AlbumInfo(album.ToApiKey(),
                    album.Name,
                    configuration.GenerateImageUrl(id, ImageSize.Thumbnail),
                    configuration.GenerateImageUrl(id, ImageSize.Medium),
                    configuration.GenerateImageUrl(id, ImageSize.Large),
                    album.SongCount,
                    1,
                    album.Notes,
                    album.MusicBrainzId);
            }
        }

        return new ResponseModel
        {
            IsSuccess = data != null,
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "albumInfo"
            }
        };
    }

    public async Task<ResponseModel> GetUserAsync(string username, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        // Only users with admin privileges are allowed to call this method.
        var isUserAdmin = await userService.IsUserAdminAsync(authResponse.UserInfo.UserName, cancellationToken)
            .ConfigureAwait(false);
        if (!isUserAdmin)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        User? data = null;
        var user = await userService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user.IsSuccess)
        {
            data = user.Data!.ToApiUser();
        }

        return new ResponseModel
        {
            IsSuccess = data != null,
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "user"
            }
        };
    }

    public async Task<ResponseModel> GetRandomSongsAsync(int size, string? genre, int? fromYear, int? toYear,
        string? musicFolderId, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Child[]? songs;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var indexLimit =
                (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
            if (indexLimit == 0)
            {
                indexLimit = short.MaxValue;
            }

            var takeSize = size < indexLimit ? size : indexLimit;
            var genreFilter = genre ?? string.Empty;
            var fromYearFilter = fromYear ?? 0;
            var toYearFilter = toYear ?? 9999;

            // Build EF Core query for random songs with filters
            var songQuery = scopedContext.Songs
                .Include(x => x.Album).ThenInclude(x => x.Artist)
                .Include(x => x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                .AsNoTracking()
                .Where(s => 
                    (string.IsNullOrEmpty(genreFilter) || 
                     (s.Genres != null && s.Genres.Contains(genreFilter)) ||
                     (s.Album.Genres != null && s.Album.Genres.Contains(genreFilter))) &&
                    s.Album.ReleaseDate.Year >= fromYearFilter &&
                    s.Album.ReleaseDate.Year <= toYearFilter);

            // Get random songs - first get all matching songs, then shuffle in memory
            var allDbSongs = await songQuery
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            // Shuffle and take the required number
            var random = new Random();
            var dbSongs = allDbSongs.OrderBy(x => random.Next()).Take(takeSize).ToArray();

            songs = dbSongs.Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = songs,
                DataPropertyName = "randomSongs",
                DataDetailPropertyName = "song"
            }
        };
    }

    // TODO: This should be moved to RadioStationService in the future.
    public async Task<ResponseModel> DeleteInternetRadioStationAsync(string id, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        // Only users with admin privileges are allowed to call this method.
        var isUserAdmin = await userService.IsUserAdminAsync(authResponse.UserInfo.UserName, cancellationToken)
            .ConfigureAwait(false);
        if (!isUserAdmin)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var apiKey = ApiKeyFromId(id);
            var radioStation = await scopedContext
                .RadioStations
                .FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
                .ConfigureAwait(false);
            if (radioStation != null)
            {
                scopedContext.RadioStations.Remove(radioStation);
                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                result = true;
            }
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    // TODO: This should be moved to RadioStationService in the future.
    public async Task<ResponseModel> CreateInternetRadioStationAsync(string name, string streamUrl, string? homePageUrl,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        // Only users with admin privileges are allowed to call this method.
        var isUserAdmin = await userService.IsUserAdminAsync(authResponse.UserInfo.UserName, cancellationToken)
            .ConfigureAwait(false);
        if (!isUserAdmin)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        Error? notAuthorizedError = null;
        bool result;
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

            var radioStation = new dbModels.RadioStation
            {
                Name = name,
                StreamUrl = streamUrl,
                CreatedAt = now
            };
            await scopedContext.RadioStations.AddAsync(radioStation, cancellationToken).ConfigureAwait(false);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = true;
            Logger.Information("User [{UserInfo}] created radio station [{Name}].",
                authResponse.UserInfo,
                name);
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    // TODO: This should be moved to RadioStationService in the future.
    public async Task<ResponseModel> UpdateInternetRadioStationAsync(string id, string name, string streamUrl,
        string? homePageUrl, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        // Only users with admin privileges are allowed to call this method.
        var isUserAdmin = await userService.IsUserAdminAsync(authResponse.UserInfo.UserName, cancellationToken)
            .ConfigureAwait(false);
        if (!isUserAdmin)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var apiKey = ApiKeyFromId(id);
            var radioStation = await scopedContext
                .RadioStations
                .FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
                .ConfigureAwait(false);
            if (radioStation != null)
            {
                var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

                radioStation.Name = name;
                radioStation.StreamUrl = streamUrl;
                radioStation.HomePageUrl = homePageUrl;
                radioStation.LastUpdatedAt = now;
                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                result = true;
            }
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    // TODO: This should be moved to RadioStationService in the future.
    public async Task<ResponseModel> GetInternetRadioStationsAsync(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = new List<InternetRadioStation>();
        try
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var radioStations = await scopedContext
                    .RadioStations
                    .AsNoTracking()
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                data = radioStations.Select(x => x.ToApiInternetRadioStation()).ToList();
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to get Radio Stations Request [{ApiResult}]", apiRequest);
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data.ToArray(),
                DataPropertyName = "internetRadioStations",
                DataDetailPropertyName = apiRequest.IsXmlRequest ? string.Empty : "internetRadioStation"
            }
        };
    }

    // TODO: This should be moved to SongService in the future.
    public async Task<ResponseModel> GetLyricsListForSongIdAsync(string id, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        LyricsList[]? data = null;

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var apiKey = ApiKeyFromId(id);
            var dbSong = await scopedContext.Songs
                .Include(x => x.Album).ThenInclude(x => x.Artist).ThenInclude(x => x.Library)
                .FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken).ConfigureAwait(false);

            if (dbSong != null)
            {
                var lyricsResult = await lyricPlugin.GetLyricListAsync(
                    Path.Combine(dbSong.Album.Artist.Library.Path, dbSong.Album.Artist.Directory).ToFileSystemDirectoryInfo(),
                    new FileSystemFileInfo
                    {
                        Name = dbSong.FileName,
                        Size = dbSong.FileSize
                    },
                    cancellationToken).ConfigureAwait(false);
                if (lyricsResult.IsSuccess)
                {
                    data = [lyricsResult.Data!];
                }
            }
        }


        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "structuredLyrics",
                DataDetailPropertyName = string.Empty
            }
        };
    }

    // TODO: This should be moved to SongService in the future.
    public async Task<ResponseModel> GetLyricsForArtistAndTitleAsync(string? artist, string? title, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Lyrics? data = null;

        if (artist.Nullify() != null && title.Nullify() != null)
        {
            await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var artistNameValue = artist!.ToNormalizedString() ?? artist!;
                var titleNameValue = title!.ToNormalizedString() ?? title!;
                var dbArtist = await scopedContext
                    .Artists
                    .Include(x => x.Library)
                    .Include(x => x.Albums).ThenInclude(x => x.Songs)
                    .FirstOrDefaultAsync(x => x.NameNormalized == artistNameValue, cancellationToken).ConfigureAwait(false);

                if (dbArtist != null)
                {
                    var dbSong = dbArtist.Albums.SelectMany(x => x.Songs).FirstOrDefault(x => x.TitleNormalized == titleNameValue);
                    if (dbSong != null)
                    {
                        var lyricsResult = await lyricPlugin.GetLyricsAsync(
                            Path.Combine(dbArtist.Library.Path, dbArtist.Directory).ToFileSystemDirectoryInfo(),
                            new FileSystemFileInfo
                            {
                                Name = dbSong.FileName,
                                Size = dbSong.FileSize
                            },
                            cancellationToken).ConfigureAwait(false);
                        if (lyricsResult.IsSuccess)
                        {
                            data = lyricsResult.Data;
                        }
                    }
                }
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "lyrics",
                DataDetailPropertyName = string.Empty
            }
        };
    }

    private static ArtistSearchResult ToArtistSearchResult(ArtistDataInfo artist)
    {
        return new ArtistSearchResult($"artist_{artist.ApiKey}", artist.Name, $"artist_{artist.ApiKey}", artist.AlbumCount);
    }

    private static AlbumSearchResult ToAlbumSearchResult(AlbumDataInfo album)
    {
        return new AlbumSearchResult
        {
            Id = $"album_{album.ApiKey}",
            Name = album.Name,
            CoverArt = $"album_{album.ApiKey}",
            SongCount = album.SongCount,
            Artist = album.ArtistName,
            ArtistId = $"artist_{album.ArtistApiKey}",
            CreatedAt = album.CreatedAt,
            DurationMs = album.Duration,
            Genres = ParseGenresFromTags(album.Tags)
        };
    }

    private static SongSearchResult ToSongSearchResult(SongDataInfo song)
    {
        return new SongSearchResult
        {
            Id = $"song_{song.ApiKey}",
            Parent = $"album_{song.AlbumApiKey}",
            Title = song.Title,
            Album = song.AlbumName,
            Artist = song.ArtistName,
            CoverArt = $"song_{song.ApiKey}",
            CreatedAt = song.CreatedAt,
            Duration = (int)(song.Duration / 1000), // Convert milliseconds to seconds
            Bitrate = 0, // Not available in SongDataInfo
            Track = song.SongNumber,
            Year = song.ReleaseDate.Year,
            Genres = ParseGenresFromTags(song.Tags),
            Size = (int)song.FileSize,
            ContentType = "audio/mpeg", // Default - not available in SongDataInfo
            Path = "unknown", // Not available in SongDataInfo
            Suffix = "mp3", // Default - not available in SongDataInfo  
            AlbumId = $"album_{song.AlbumApiKey}",
            ArtistId = $"artist_{song.ArtistApiKey}"
        };
    }

    private static string[]? ParseGenresFromTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return null;
            
        // Tags are pipe-separated, genres might be in there
        // This is a simplified implementation - adjust based on actual tag format
        return tags.Split('|', StringSplitOptions.RemoveEmptyEntries)
                  .Where(tag => !string.IsNullOrWhiteSpace(tag))
                  .ToArray();
    }
}
