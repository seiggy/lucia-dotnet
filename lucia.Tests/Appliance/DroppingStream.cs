namespace lucia.Tests.Appliance;

/// <summary>
/// Serves the first <c>bytesBeforeDrop</c> bytes of a payload, then fails like a dropped connection.
/// </summary>
internal sealed class DroppingStream(byte[] payload, int bytesBeforeDrop) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= bytesBeforeDrop)
        {
            throw new IOException("The connection was reset.");
        }
        var read = Math.Min(count, bytesBeforeDrop - _position);
        Array.Copy(payload, _position, buffer, offset, read);
        _position += read;
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
