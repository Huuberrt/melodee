using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Blazor.Services;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Streaming;
using Melodee.Common.Security;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/[controller]")]
public class SongsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserService userService,
    SongService songService,
    StreamingLimiter streamingLimiter,
    IConfiguration configuration,
    IBlacklistService blacklistService,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    [HttpGet]
    [Route("{id:guid}")]
    public Task<IActionResult> SongById(Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync(short page, short pageSize, string? orderBy, string? orderDirection, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        var userResult = await userService.GetByApiKeyAsync(SafeParser.ToGuid(ApiRequest.ApiKey) ?? Guid.Empty, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        if (userResult.Data.IsLocked)
        {
            return Forbid("User is locked");
        }

        var orderByValue = orderBy ?? nameof(AlbumDataInfo.CreatedAt);
        var orderDirectionValue = orderDirection ?? PagedRequest.OrderDescDirection;

        var listResult = await songService.ListAsync(new PagedRequest
        {
            Page = page,
            PageSize = pageSize,
            OrderBy = new Dictionary<string, string> { { orderByValue, orderDirectionValue } }
        }, userResult.Data.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = GetBaseUrl(await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false));

        return Ok(new
        {
            meta = new PaginationMetadata(
                listResult.TotalCount,
                pageSize,
                page,
                listResult.TotalPages
            ),
            data = listResult.Data.Select(x => x.ToSongModel(baseUrl, userResult.Data.ToUserModel(baseUrl), userResult.Data.PublicKey)).ToArray()
        });
    }

    [HttpGet]
    [Route("recent")]
    public async Task<IActionResult> RecentlyAddedAsync(short limit, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        var userResult = await userService.GetByApiKeyAsync(SafeParser.ToGuid(ApiRequest.ApiKey) ?? Guid.Empty, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        if (userResult.Data.IsLocked)
        {
            return Forbid("User is locked");
        }

        var songRecentResult = await songService.ListAsync(new PagedRequest
        {
            Page = 1,
            PageSize = limit,
            OrderBy = new Dictionary<string, string> { { nameof(AlbumDataInfo.CreatedAt), PagedRequest.OrderDescDirection } }
        }, userResult.Data.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = GetBaseUrl(await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false));

        return Ok(new
        {
            meta = new PaginationMetadata(
                songRecentResult.TotalCount,
                limit,
                1,
                songRecentResult.TotalPages
            ),
            data = songRecentResult.Data.Select(x => x.ToSongModel(baseUrl, userResult.Data.ToUserModel(baseUrl), userResult.Data.PublicKey)).ToArray()
        });
    }

    [HttpPost]
    [Route("starred/{apiKey:guid}/{isStarred:bool}")]
    public async Task<IActionResult>? ToggleSongStarred(Guid apiKey, bool isStarred, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        var userResult = await userService.GetByApiKeyAsync(SafeParser.ToGuid(ApiRequest.ApiKey) ?? Guid.Empty, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        if (userResult.Data.IsLocked)
        {
            return Forbid("User is locked");
        }

        if (await blacklistService.IsEmailBlacklistedAsync(userResult.Data.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(GetRequestIp(HttpContext)).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "User is blacklisted" });
        }

        var toggleStarredResult = await userService.ToggleSongStarAsync(userResult.Data.Id, apiKey, isStarred, cancellationToken).ConfigureAwait(false);
        if (toggleStarredResult.IsSuccess)
        {
            return Ok();
        }

        return BadRequest("Unable to toggle star for song for user.");
    }
    
    [HttpPost]
    [Route("setrating/{apiKey:guid}/{rating:int}")]
    public async Task<IActionResult>? ToggleSongStarred(Guid apiKey, int rating, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        var userResult = await userService.GetByApiKeyAsync(SafeParser.ToGuid(ApiRequest.ApiKey) ?? Guid.Empty, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        if (userResult.Data.IsLocked)
        {
            return Forbid("User is locked");
        }

        if (await blacklistService.IsEmailBlacklistedAsync(userResult.Data.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(GetRequestIp(HttpContext)).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "User is blacklisted" });
        }
        var setRatingResult = await userService.SetSongRatingAsync(userResult.Data.Id, apiKey, rating, cancellationToken).ConfigureAwait(false);
        if (setRatingResult.IsSuccess)
        {
            return Ok();
        }
        
        return BadRequest("Unable to toggle star for song for user.");
    }

    [HttpGet]
    [Route("/song/stream/{apiKey:guid}/{userApiKey:guid}/{authToken}")]
    public async Task<IActionResult> StreamSong(Guid apiKey, Guid userApiKey, string authToken, CancellationToken cancellationToken = default)
    {
        var userResult = await userService.GetByApiKeyAsync(userApiKey, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return Unauthorized(new { error = "Authorization token is invalid" });
        }

        if (userResult.Data.IsLocked)
        {
            return Forbid("User is locked");
        }

        if (await blacklistService.IsEmailBlacklistedAsync(userResult.Data.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(GetRequestIp(HttpContext)).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "User is blacklisted" });
        }

        var hmacService = new HmacTokenService(userResult.Data.PublicKey);
        var authTokenValidation = hmacService.ValidateTimedToken(authToken.FromBase64());

        if (!authTokenValidation)
        {
            return Unauthorized(new { error = "Invalid Auth Token" });
        }

        // Optional: enforce streaming concurrency limits
        var userKey = $"web:{userResult.Data.Id}";
        if (!await streamingLimiter.TryEnterAsync(userKey, cancellationToken).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many concurrent streams" });
        }

        // Ensure exit on request completion
        HttpContext.Response.OnCompleted(() => { streamingLimiter.Exit(userKey); return Task.CompletedTask; });

        // Parse range header if present
        var rangeHeader = Request.Headers["Range"].ToString();
        
        var melodeeConfig = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var useBuffered = melodeeConfig.GetValue<bool?>(SettingRegistry.StreamingUseBufferedResponses) ?? false;

        if (useBuffered)
        {
            // Buffered fallback using descriptor
            var bufferedDescriptorResult = await songService.GetStreamingDescriptorAsync(
                userResult.Data.ToUserInfo(),
                apiKey,
                rangeHeader,
                false,
                cancellationToken).ConfigureAwait(false);

            if (!bufferedDescriptorResult.IsSuccess || bufferedDescriptorResult.Data == null)
            {
                return BadRequest(new { error = "Unable to load song" });
            }
            var bufferedDescriptor = bufferedDescriptorResult.Data;

            foreach (var header in RangeParser.CreateResponseHeaders(bufferedDescriptor, bufferedDescriptor.Range != null ? 206 : 200))
            {
                Response.Headers[header.Key] = header.Value;
            }
            Response.StatusCode = bufferedDescriptor.Range != null ? 206 : 200;

            byte[] bytes;
            if (bufferedDescriptor.Range != null)
            {
                await using var fs = new FileStream(bufferedDescriptor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(bufferedDescriptor.Range.Start, SeekOrigin.Begin);
                var len = (int)bufferedDescriptor.Range.GetContentLength(bufferedDescriptor.FileSize);
                bytes = new byte[len];
                var read = await fs.ReadAsync(bytes, 0, len, cancellationToken);
                if (read != len) Array.Resize(ref bytes, read);
            }
            else
            {
                bytes = await System.IO.File.ReadAllBytesAsync(bufferedDescriptor.FilePath, cancellationToken);
            }

            return new FileContentResult(bytes, bufferedDescriptor.ContentType ?? "application/octet-stream")
            {
                FileDownloadName = bufferedDescriptor.FileName
            };
        }

        var descriptorResult = await songService.GetStreamingDescriptorAsync(
            userResult.Data.ToUserInfo(), 
            apiKey, 
            rangeHeader,
            false, // not a download
            cancellationToken).ConfigureAwait(false);
            
        if (!descriptorResult.IsSuccess || descriptorResult.Data == null)
        {
            return BadRequest(new { error = "Unable to load song" });
        }

        var descriptor = descriptorResult.Data;
        
        // Determine status code
        var statusCode = descriptor.Range != null ? 206 : 200; // 206 for partial content, 200 for full

        // Create response headers using the helper
        var responseHeaders = RangeParser.CreateResponseHeaders(descriptor, statusCode);
        
        // Set response headers (don't clear existing headers, just set/append required ones)
        foreach (var header in responseHeaders)
        {
            Response.Headers[header.Key] = header.Value;
        }

        // Set status code
        Response.StatusCode = statusCode;

        // Return FileStreamResult for efficient streaming
        if (descriptor.Range != null)
        {
            // For range requests, use FileStreamResult with range support
            var fileStream = new FileStream(descriptor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            
            // Seek to start position
            fileStream.Seek(descriptor.Range.Start, SeekOrigin.Begin);
            
            // Create a bounded stream for the range
            var rangeStream = new BoundedStream(fileStream, descriptor.Range.GetContentLength(descriptor.FileSize));
            
            return new FileStreamResult(rangeStream, descriptor.ContentType)
            {
                EnableRangeProcessing = true,
                FileDownloadName = descriptor.IsDownload ? descriptor.FileName : null
            };
        }
        else
        {
            // For full file requests, use FileStreamResult with range processing enabled
            return new PhysicalFileResult(descriptor.FilePath, descriptor.ContentType)
            {
                EnableRangeProcessing = true,
                FileDownloadName = descriptor.IsDownload ? descriptor.FileName : null
            };
        }
    }
}
