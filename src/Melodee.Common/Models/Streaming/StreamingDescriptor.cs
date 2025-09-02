using Microsoft.Extensions.Primitives;

namespace Melodee.Common.Models.Streaming;

/// <summary>
/// Streaming descriptor that provides file metadata without loading content into memory.
/// This replaces the byte-array based approach with a descriptor pattern for memory-efficient streaming.
/// </summary>
public sealed record StreamingDescriptor
{
    /// <summary>
    /// Absolute path to the file to stream
    /// </summary>
    public required string FilePath { get; init; }
    
    /// <summary>
    /// Total file size in bytes
    /// </summary>
    public required long FileSize { get; init; }
    
    /// <summary>
    /// MIME content type (e.g., "audio/mpeg")
    /// </summary>
    public required string ContentType { get; init; }
    
    /// <summary>
    /// HTTP response headers to set
    /// </summary>
    public required IDictionary<string, StringValues> ResponseHeaders { get; init; }
    
    /// <summary>
    /// Range request information (null for full file)
    /// </summary>
    public RangeInfo? Range { get; init; }
    
    /// <summary>
    /// For download requests - suggested filename
    /// </summary>
    public string? FileName { get; init; }
    
    /// <summary>
    /// Whether this is a download request (affects headers)
    /// </summary>
    public bool IsDownload { get; init; }
    
    /// <summary>
    /// File last modified time for cache validation
    /// </summary>
    public DateTime? LastModified { get; init; }
    
    /// <summary>
    /// ETag for cache validation
    /// </summary>
    public string? ETag { get; init; }
}

/// <summary>
/// HTTP Range request information parsed from Range header
/// </summary>
public sealed record RangeInfo
{
    /// <summary>
    /// Start byte position (0-based, inclusive)
    /// </summary>
    public required long Start { get; init; }
    
    /// <summary>
    /// End byte position (0-based, inclusive) or null for end-of-file
    /// </summary>
    public long? End { get; init; }
    
    /// <summary>
    /// Calculate the actual end position given file size
    /// </summary>
    public long GetActualEnd(long fileSize) => End ?? fileSize - 1;
    
    /// <summary>
    /// Calculate content length for this range
    /// </summary>
    public long GetContentLength(long fileSize)
    {
        var actualEnd = GetActualEnd(fileSize);
        return actualEnd - Start + 1;
    }
    
    /// <summary>
    /// Check if range is valid for given file size
    /// </summary>
    public bool IsValidForFileSize(long fileSize)
    {
        if (Start < 0 || Start >= fileSize)
            return false;
            
        if (End.HasValue && (End.Value < Start || End.Value >= fileSize))
            return false;
            
        return true;
    }
    
    /// <summary>
    /// Create Content-Range header value
    /// </summary>
    public string ToContentRangeHeader(long fileSize)
    {
        var actualEnd = GetActualEnd(fileSize);
        return $"bytes {Start}-{actualEnd}/{fileSize}";
    }
}