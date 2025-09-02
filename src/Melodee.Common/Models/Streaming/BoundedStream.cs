namespace Melodee.Common.Models.Streaming;

/// <summary>
/// A stream wrapper that limits reading to a specified number of bytes from the underlying stream.
/// Used for HTTP range requests to ensure only the requested range is returned.
/// </summary>
public sealed class BoundedStream : Stream
{
    private readonly Stream _baseStream;
    private readonly long _maxBytesToRead;
    private long _bytesRead;
    private bool _disposed;

    public BoundedStream(Stream baseStream, long maxBytesToRead)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        _maxBytesToRead = Math.Max(0, maxBytesToRead);
    }

    public override bool CanRead => _baseStream.CanRead && _bytesRead < _maxBytesToRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => false;

    public override long Length => Math.Min(_baseStream.Length - _baseStream.Position, _maxBytesToRead);

    public override long Position
    {
        get => _baseStream.Position;
        set => _baseStream.Position = value;
    }

    public override void Flush()
    {
        _baseStream.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _baseStream.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_bytesRead >= _maxBytesToRead)
            return 0;

        var maxToRead = (int)Math.Min(count, _maxBytesToRead - _bytesRead);
        var actualRead = _baseStream.Read(buffer, offset, maxToRead);
        _bytesRead += actualRead;
        return actualRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_bytesRead >= _maxBytesToRead)
            return 0;

        var maxToRead = (int)Math.Min(count, _maxBytesToRead - _bytesRead);
        var actualRead = await _baseStream.ReadAsync(buffer, offset, maxToRead, cancellationToken);
        _bytesRead += actualRead;
        return actualRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_bytesRead >= _maxBytesToRead)
            return 0;

        var maxToRead = (int)Math.Min(buffer.Length, _maxBytesToRead - _bytesRead);
        var actualRead = await _baseStream.ReadAsync(buffer[..maxToRead], cancellationToken);
        _bytesRead += actualRead;
        return actualRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _baseStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException("BoundedStream does not support SetLength");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("BoundedStream does not support writing");
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _baseStream?.Dispose();
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (_baseStream != null)
            {
                await _baseStream.DisposeAsync();
            }
            _disposed = true;
        }
        await base.DisposeAsync();
    }
}