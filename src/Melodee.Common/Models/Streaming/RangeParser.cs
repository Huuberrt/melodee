using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace Melodee.Common.Models.Streaming;

/// <summary>
/// Parses HTTP Range headers according to RFC 7233
/// Replaces the problematic range parsing in existing streaming code
/// </summary>
public static class RangeParser
{
    private static readonly Regex RangeRegex = new(@"^bytes=(\d+)-(\d*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SuffixRangeRegex = new(@"^bytes=-(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse Range header value into RangeInfo
    /// Supports formats: "bytes=start-end", "bytes=start-", "bytes=-suffix"
    /// </summary>
    /// <param name="rangeHeader">Range header value (e.g., "bytes=0-499")</param>
    /// <param name="fileSize">File size for validation and suffix range calculation</param>
    /// <returns>Parsed range info or null if invalid/unsupported</returns>
    public static RangeInfo? ParseRange(string? rangeHeader, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader) || fileSize <= 0)
            return null;

        rangeHeader = rangeHeader.Trim();

        // Handle suffix-byte-range-spec: "bytes=-500" (last N bytes)
        var suffixMatch = SuffixRangeRegex.Match(rangeHeader);
        if (suffixMatch.Success)
        {
            if (long.TryParse(suffixMatch.Groups[1].Value, out var suffixLength))
            {
                var start = Math.Max(0, fileSize - suffixLength);
                return new RangeInfo
                {
                    Start = start,
                    End = fileSize - 1
                };
            }
            return null;
        }

        // Handle byte-range-spec: "bytes=start-end" or "bytes=start-"
        var rangeMatch = RangeRegex.Match(rangeHeader);
        if (!rangeMatch.Success)
            return null;

        if (!long.TryParse(rangeMatch.Groups[1].Value, out var startPos))
            return null;

        // Validate start position
        if (startPos < 0 || startPos >= fileSize)
            return null;

        // Parse end position if present
        long? endPos = null;
        var endGroup = rangeMatch.Groups[2].Value;
        if (!string.IsNullOrEmpty(endGroup))
        {
            if (long.TryParse(endGroup, out var parsedEnd))
            {
                // Clamp end position to file size
                endPos = Math.Min(parsedEnd, fileSize - 1);
                
                // Validate end >= start
                if (endPos < startPos)
                    return null;
            }
            else
            {
                return null;
            }
        }

        return new RangeInfo
        {
            Start = startPos,
            End = endPos
        };
    }

    /// <summary>
    /// Parse Range header from StringValues (ASP.NET Core format)
    /// </summary>
    public static RangeInfo? ParseRange(StringValues rangeHeader, long fileSize)
    {
        return ParseRange(rangeHeader.ToString(), fileSize);
    }

    /// <summary>
    /// Check if range request is satisfiable for given file size
    /// </summary>
    public static bool IsRangeSatisfiable(string? rangeHeader, long fileSize)
    {
        var range = ParseRange(rangeHeader, fileSize);
        return range?.IsValidForFileSize(fileSize) ?? false;
    }

    /// <summary>
    /// Parse multiple range requests (currently not supported - returns first valid range)
    /// RFC 7233 allows multiple ranges but most implementations only support single ranges
    /// </summary>
    public static RangeInfo? ParseFirstRange(string? rangeHeader, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader))
            return null;

        // Split by comma for multipart ranges, but only process the first one
        var ranges = rangeHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        foreach (var range in ranges)
        {
            var parsed = ParseRange($"bytes={range.Replace("bytes=", "")}", fileSize);
            if (parsed != null)
                return parsed;
        }

        return null;
    }

    /// <summary>
    /// Create appropriate response headers for range or full content
    /// </summary>
    public static Dictionary<string, StringValues> CreateResponseHeaders(
        StreamingDescriptor descriptor, 
        int statusCode = 200)
    {
        var headers = new Dictionary<string, StringValues>(descriptor.ResponseHeaders);

        // Set content type
        headers["Content-Type"] = descriptor.ContentType;
        
        // Set accept-ranges
        headers["Accept-Ranges"] = "bytes";

        if (descriptor.Range != null && statusCode == 206)
        {
            // Partial content response
            var contentLength = descriptor.Range.GetContentLength(descriptor.FileSize);
            headers["Content-Length"] = contentLength.ToString();
            headers["Content-Range"] = descriptor.Range.ToContentRangeHeader(descriptor.FileSize);
        }
        else
        {
            // Full content response
            headers["Content-Length"] = descriptor.FileSize.ToString();
        }

        // Add cache headers if available
        if (descriptor.LastModified.HasValue)
        {
            headers["Last-Modified"] = descriptor.LastModified.Value.ToString("R");
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ETag))
        {
            headers["ETag"] = $"\"{descriptor.ETag}\"";
        }

        // Add download headers if needed
        if (descriptor.IsDownload && !string.IsNullOrWhiteSpace(descriptor.FileName))
        {
            headers["Content-Disposition"] = $"attachment; filename=\"{descriptor.FileName}\"";
        }

        return headers;
    }
}