namespace nadena.dev.resonity.remote.puppeteer.misc;

internal class BlockClosureStream : Stream
{
    private readonly Stream _upstream;
    
    public BlockClosureStream(Stream upstream)
    {
        _upstream = upstream;
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        return _upstream.BeginRead(buffer, offset, count, callback, state);
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        return _upstream.BeginWrite(buffer, offset, count, callback, state);
    }

    public override void Close()
    {
        // don't close
    }

    public override void CopyTo(Stream destination, int bufferSize)
    {
        _upstream.CopyTo(destination, bufferSize);
    }

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        return _upstream.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        return _upstream.EndRead(asyncResult);
    }

    public override void EndWrite(IAsyncResult asyncResult)
    {
        _upstream.EndWrite(asyncResult);
    }

    public override void Flush()
    {
        _upstream.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _upstream.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _upstream.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        return _upstream.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _upstream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        return _upstream.ReadAsync(buffer, cancellationToken);
    }

    public override int ReadByte()
    {
        return _upstream.ReadByte();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _upstream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _upstream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _upstream.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _upstream.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _upstream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        return _upstream.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        _upstream.WriteByte(value);
    }

    public override bool CanRead => _upstream.CanRead;

    public override bool CanSeek => _upstream.CanSeek;

    public override bool CanTimeout => _upstream.CanTimeout;

    public override bool CanWrite => _upstream.CanWrite;

    public override long Length => _upstream.Length;

    public override long Position
    {
        get => _upstream.Position;
        set => _upstream.Position = value;
    }

    public override int ReadTimeout
    {
        get => _upstream.ReadTimeout;
        set => _upstream.ReadTimeout = value;
    }

    public override int WriteTimeout
    {
        get => _upstream.WriteTimeout;
        set => _upstream.WriteTimeout = value;
    }
}