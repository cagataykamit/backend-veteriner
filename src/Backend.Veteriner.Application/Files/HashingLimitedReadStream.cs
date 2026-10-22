using System.Security.Cryptography;

namespace Backend.Veteriner.Application.Files;

/// <summary>
/// Kaynağı okurken SHA-256 hesaplayan ve bayt sınırını akış sırasında uygulayan salt okunur sarmalayıcı: depo içeriği
/// yazarken özet ve boyut ayrı bir okuma gerektirmez; sınır aşılırsa <see cref="FileTooLargeException"/> fırlatılır.
/// </summary>
internal sealed class HashingLimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private string? _hashHex;

    public HashingLimitedReadStream(Stream inner, long maxBytes)
    {
        _inner = inner;
        _maxBytes = maxBytes;
    }

    public long BytesRead { get; private set; }

    /// <summary>Akış sonuna kadar okunduktan sonra küçük harfli onaltılık SHA-256.</summary>
    public string HashHex => _hashHex ??= Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Track(buffer.AsSpan(offset, _inner.Read(buffer, offset, count)));

    public override int Read(Span<byte> buffer)
        => Track(buffer[.._inner.Read(buffer)]);

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Track(buffer.AsSpan(offset, await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        return Track(buffer.Span[..read]);
    }

    private int Track(ReadOnlySpan<byte> chunk)
    {
        BytesRead += chunk.Length;
        if (BytesRead > _maxBytes)
            throw new FileTooLargeException();
        _hash.AppendData(chunk);
        return chunk.Length;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _hash.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Akış sırasında dosya sınırı aşıldı (413 <c>Files.TooLarge</c>).</summary>
internal sealed class FileTooLargeException() : Exception("Dosya boyut sınırını aşıyor.");
