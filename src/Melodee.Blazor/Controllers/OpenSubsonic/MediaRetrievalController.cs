using System.Net;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.OpenSubsonic.Requests;
using Melodee.Common.Models.OpenSubsonic.Responses;
using Melodee.Common.Models.Streaming;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Results;
using Microsoft.AspNetCore.Mvc;

namespace Melodee.Blazor.Controllers.OpenSubsonic;

public class MediaRetrievalController(ISerializer serializer, EtagRepository etagRepository, OpenSubsonicApiService openSubsonicApiService, IMelodeeConfigurationFactory configurationFactory, StreamingLimiter streamingLimiter) : ControllerBase(etagRepository, serializer, configurationFactory)
{
    /// <summary>
    ///     Searches for and returns lyrics for a given song.
    /// </summary>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The song title.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [HttpPost]
    [Route("/rest/getLyrics.view")]
    [Route("/rest/getLyrics")]
    public Task<IActionResult> GetLyricsAsync(string? artist, string? title, CancellationToken cancellationToken = default)
    {
        return MakeResult(openSubsonicApiService.GetLyricsForArtistAndTitleAsync(artist, title, ApiRequest, cancellationToken));
    }

    /// <summary>
    ///     Add support for synchronized lyrics, multiple languages, and retrieval by song ID.
    ///     <remarks>
    ///         Retrieves all structured lyrics from the server for a given song. The lyrics can come from embedded tags
    ///         (SYLT/USLT), LRC file/text file, or any other external source.
    ///     </remarks>
    /// </summary>
    /// <param name="id">The track ID.</param>
    /// ///
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [HttpPost]
    [Route("/rest/getLyricsBySongId.view")]
    [Route("/rest/getLyricsBySongId")]
    public Task<IActionResult> GetLyricsAsync(string id, CancellationToken cancellationToken = default)
    {
        return MakeResult(openSubsonicApiService.GetLyricsListForSongIdAsync(id, ApiRequest, cancellationToken));
    }


    [HttpGet]
    [HttpPost]
    [Route("/rest/hls.view")]
    [Route("/rest/hls")]
    [Route("/rest/getCaptions.view")]
    [Route("/rest/getCaptions")]
    public IActionResult DeprecatedWontImplement()
    {
        HttpContext.Response.Headers.Append("Cache-Control", "no-cache");
        return StatusCode((int)HttpStatusCode.Gone);
    }

    /// <summary>
    ///     Returns the avatar (personal image) for a user.
    /// </summary>
    /// <param name="username">The user in question.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [HttpPost]
    [Route("/rest/getAvatar.view")]
    [Route("/rest/getAvatar")]
    public async Task<IActionResult> GetAvatarAsync(string username, CancellationToken cancellationToken = default)
    {
        return new FileContentResult((byte[])(await openSubsonicApiService.GetAvatarAsync(username,
                ApiRequest,
                cancellationToken)).ResponseData.Data!,
            "image/png");
    }

    /// <summary>
    ///     Returns a cover art image.
    /// </summary>
    /// <param name="id">Composite ID of type:apikey</param>
    /// <param name="size">If specified, scale image to this size.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [HttpPost]
    [Route("/rest/getCoverArt.view")]
    [Route("/rest/getCoverArt")]
    public Task<IActionResult> GetCoverArtAsync(string id, string? size, CancellationToken cancellationToken = default)
    {
        return ImageResult(id, openSubsonicApiService.GetImageForApiKeyId(id,
            size,
            ApiRequest,
            cancellationToken));
    }

    /// <summary>
    ///     Downloads a given media file.
    /// </summary>
    /// <param name="request">Stream model for parameters for downloading.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [HttpPost]
    [Route("/rest/download.view")]
    [Route("/rest/download")]
    public async Task<IActionResult> DownloadAsync(StreamRequest request, CancellationToken cancellationToken = default)
    {
        request.IsDownloadingRequest = true;
        var melodeeConfig = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var useBuffered = melodeeConfig.GetValue<bool?>(SettingRegistry.StreamingUseBufferedResponses) ?? false;

        // Optional streaming limit; key off username
        var userKey = $"subsonic:{ApiRequest.Username ?? "unknown"}";
        if (!await streamingLimiter.TryEnterAsync(userKey, cancellationToken).ConfigureAwait(false))
        {
            return StatusCode((int)HttpStatusCode.TooManyRequests, new { error = "Too many concurrent streams" });
        }
        HttpContext.Response.OnCompleted(() => { streamingLimiter.Exit(userKey); return Task.CompletedTask; });

        if (useBuffered)
        {
            // Buffered fallback using descriptor
            var bufferedDescriptorResult = await openSubsonicApiService.GetStreamingDescriptorAsync(request, ApiRequest, cancellationToken).ConfigureAwait(false);
            if (!bufferedDescriptorResult.IsSuccess || bufferedDescriptorResult.Data == null)
            {
                return StatusCode((int)HttpStatusCode.NotFound);
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

        var descriptorResult = await openSubsonicApiService.GetStreamingDescriptorAsync(request, ApiRequest, cancellationToken).ConfigureAwait(false);
        
        if (descriptorResult.IsSuccess && descriptorResult.Data != null)
        {
            var descriptor = descriptorResult.Data;
            
            // Create response headers
            var statusCode = descriptor.Range != null ? 206 : 200;
            var responseHeaders = RangeParser.CreateResponseHeaders(descriptor, statusCode);
            
            foreach (var header in responseHeaders)
            {
                Response.Headers[header.Key] = header.Value;
            }
            
            Response.StatusCode = statusCode;
            
            // Return efficient file streaming result
            if (descriptor.Range != null)
            {
                // For range requests
                var fileStream = new FileStream(descriptor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                
                fileStream.Seek(descriptor.Range.Start, SeekOrigin.Begin);
                var rangeStream = new BoundedStream(fileStream, descriptor.Range.GetContentLength(descriptor.FileSize));
                
                return new FileStreamResult(rangeStream, descriptor.ContentType)
                {
                    EnableRangeProcessing = true,
                    FileDownloadName = descriptor.FileName
                };
            }
            else
            {
                // For full file downloads
                return new PhysicalFileResult(descriptor.FilePath, descriptor.ContentType)
                {
                    EnableRangeProcessing = true,
                    FileDownloadName = descriptor.FileName
                };
            }
        }

        Response.StatusCode = (int)HttpStatusCode.NotFound;
        return new JsonStringResult(Serializer.Serialize(new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = false,
            ResponseData = await openSubsonicApiService.NewApiResponse(
                false,
                string.Empty,
                string.Empty,
                Error.DataNotFoundError)
        })!);
    }

    /// <summary>
    ///     Streams a given media file.
    /// </summary>
    /// <param name="request">Stream model for parameters for streaming.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [HttpPost]
    [Route("/rest/stream.view")]
    [Route("/rest/stream")]
    public async Task<IActionResult> StreamAsync(StreamRequest request, CancellationToken cancellationToken = default)
    {
        var melodeeConfig = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var useBuffered = melodeeConfig.GetValue<bool?>(SettingRegistry.StreamingUseBufferedResponses) ?? false;

        // Optional streaming limit; key off username
        var userKey = $"subsonic:{ApiRequest.Username ?? "unknown"}";
        if (!await streamingLimiter.TryEnterAsync(userKey, cancellationToken).ConfigureAwait(false))
        {
            return StatusCode((int)HttpStatusCode.TooManyRequests, new { error = "Too many concurrent streams" });
        }
        HttpContext.Response.OnCompleted(() => { streamingLimiter.Exit(userKey); return Task.CompletedTask; });

        if (useBuffered)
        {
            // Buffered fallback using descriptor
            var bufferedDescriptorResult = await openSubsonicApiService.GetStreamingDescriptorAsync(request, ApiRequest, cancellationToken).ConfigureAwait(false);
            if (!bufferedDescriptorResult.IsSuccess || bufferedDescriptorResult.Data == null)
            {
                return StatusCode((int)HttpStatusCode.NotFound);
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
                FileDownloadName = request.IsDownloadingRequest ? bufferedDescriptor.FileName : null
            };
        }

        var descriptorResult = await openSubsonicApiService.GetStreamingDescriptorAsync(request, ApiRequest, cancellationToken).ConfigureAwait(false);
        
        if (descriptorResult.IsSuccess && descriptorResult.Data != null)
        {
            var descriptor = descriptorResult.Data;
            
            // Create response headers
            var statusCode = descriptor.Range != null ? 206 : 200;
            var responseHeaders = RangeParser.CreateResponseHeaders(descriptor, statusCode);
            
            foreach (var header in responseHeaders)
            {
                Response.Headers[header.Key] = header.Value;
            }
            
            Response.StatusCode = statusCode;
            
            // Return efficient file streaming result
            if (descriptor.Range != null)
            {
                // For range requests
                var fileStream = new FileStream(descriptor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                
                fileStream.Seek(descriptor.Range.Start, SeekOrigin.Begin);
                var rangeStream = new BoundedStream(fileStream, descriptor.Range.GetContentLength(descriptor.FileSize));
                
                return new FileStreamResult(rangeStream, descriptor.ContentType)
                {
                    EnableRangeProcessing = true,
                    FileDownloadName = descriptor.IsDownload ? descriptor.FileName : null
                };
            }
            else
            {
                // For full file streaming
                return new PhysicalFileResult(descriptor.FilePath, descriptor.ContentType)
                {
                    EnableRangeProcessing = true,
                    FileDownloadName = descriptor.IsDownload ? descriptor.FileName : null
                };
            }
        }

        Response.StatusCode = (int)HttpStatusCode.NotFound;
        return new JsonStringResult(Serializer.Serialize(new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = false,
            ResponseData = await openSubsonicApiService.NewApiResponse(
                false,
                string.Empty,
                string.Empty,
                Error.DataNotFoundError)
        })!);
    }
}
