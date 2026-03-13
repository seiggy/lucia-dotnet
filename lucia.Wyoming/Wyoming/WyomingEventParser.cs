using System.Buffers;
using System.Text;
using System.Text.Json;

namespace lucia.Wyoming.Wyoming;

/// <summary>
/// Reads Wyoming protocol events from a stream.
/// Each event is a newline-delimited JSON header optionally followed by extra JSON data and binary payload bytes.
/// </summary>
public sealed class WyomingEventParser
{
    private const int HeaderBufferSize = 8192;

    private readonly Stream _stream;
    private readonly byte[] _readBuffer = new byte[HeaderBufferSize];
    private int _bufferStart;
    private int _bufferEnd;

    public WyomingEventParser(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _stream = stream;
    }

    /// <summary>
    /// Read the next Wyoming event from the stream.
    /// Returns <see langword="null"/> if the stream is closed before the next header starts.
    /// </summary>
    public async Task<WyomingEvent?> ReadEventAsync(CancellationToken ct = default)
    {
        var headerLine = await ReadLineAsync(ct).ConfigureAwait(false);
        if (headerLine is null)
        {
            return null;
        }

        WyomingEventHeader? header;
        try
        {
            header = JsonSerializer.Deserialize<WyomingEventHeader>(headerLine);
        }
        catch (JsonException)
        {
            throw new WyomingProtocolException("Failed to parse event header.");
        }

        if (header is null)
        {
            throw new WyomingProtocolException("Failed to parse event header.");
        }

        ValidateLengths(header);

        if (header.DataLength > 0)
        {
            await ReadAndValidateExtraDataAsync(header.DataLength, ct).ConfigureAwait(false);
        }

        byte[]? payload = null;
        if (header.PayloadLength > 0)
        {
            payload = GC.AllocateUninitializedArray<byte>(header.PayloadLength);
            await ReadExactAsync(payload, ct).ConfigureAwait(false);
        }

        return WyomingEventFactory.Create(header, payload);
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            var newlineIndex = FindNewlineIndex();
            if (newlineIndex >= 0)
            {
                var headerLength = newlineIndex - _bufferStart;
                if (headerLength == 0)
                {
                    throw new WyomingProtocolException("Received an empty Wyoming event header.");
                }

                if (_readBuffer[newlineIndex - 1] == '\r')
                {
                    headerLength--;
                }

                if (headerLength <= 0)
                {
                    throw new WyomingProtocolException("Received an empty Wyoming event header.");
                }

                var headerLine = Encoding.UTF8.GetString(_readBuffer, _bufferStart, headerLength);
                AdvanceBuffer(newlineIndex - _bufferStart + 1);
                return headerLine;
            }

            var bufferedCount = _bufferEnd - _bufferStart;
            if (bufferedCount >= HeaderBufferSize)
            {
                throw new WyomingProtocolException(
                    $"Header line exceeds maximum length of {HeaderBufferSize} bytes.");
            }

            CompactBuffer();

            var bytesRead = await _stream.ReadAsync(_readBuffer.AsMemory(_bufferEnd), ct).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (_bufferStart == _bufferEnd)
                {
                    return null;
                }

                throw new WyomingProtocolException("Stream closed while reading event header.");
            }

            _bufferEnd += bytesRead;
        }
    }

    private async Task ReadAndValidateExtraDataAsync(int dataLength, CancellationToken ct)
    {
        var rented = ArrayPool<byte>.Shared.Rent(dataLength);
        try
        {
            await ReadExactAsync(rented.AsMemory(0, dataLength), ct).ConfigureAwait(false);

            try
            {
                using var _ = JsonDocument.Parse(rented.AsMemory(0, dataLength));
            }
            catch (JsonException)
            {
                throw new WyomingProtocolException("Failed to parse event extra data.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        await ReadExactAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
    }

    private async Task ReadExactAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = CopyFromBuffer(buffer.Span);

        while (totalRead < buffer.Length)
        {
            var bytesRead = await _stream.ReadAsync(buffer[totalRead..], ct).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new WyomingProtocolException("Stream closed while reading event content.");
            }

            totalRead += bytesRead;
        }
    }

    private int FindNewlineIndex()
    {
        return Array.IndexOf(_readBuffer, (byte)'\n', _bufferStart, _bufferEnd - _bufferStart);
    }

    private void CompactBuffer()
    {
        if (_bufferStart == _bufferEnd)
        {
            _bufferStart = 0;
            _bufferEnd = 0;
            return;
        }

        if (_bufferStart == 0)
        {
            return;
        }

        var bufferedCount = _bufferEnd - _bufferStart;
        Buffer.BlockCopy(_readBuffer, _bufferStart, _readBuffer, 0, bufferedCount);
        _bufferStart = 0;
        _bufferEnd = bufferedCount;
    }

    private int CopyFromBuffer(Span<byte> destination)
    {
        var available = _bufferEnd - _bufferStart;
        if (available == 0)
        {
            return 0;
        }

        var bytesToCopy = Math.Min(available, destination.Length);
        _readBuffer.AsSpan(_bufferStart, bytesToCopy).CopyTo(destination);
        AdvanceBuffer(bytesToCopy);
        return bytesToCopy;
    }

    private void AdvanceBuffer(int count)
    {
        _bufferStart += count;
        if (_bufferStart == _bufferEnd)
        {
            _bufferStart = 0;
            _bufferEnd = 0;
        }
    }

    private static void ValidateLengths(WyomingEventHeader header)
    {
        if (header.DataLength < 0)
        {
            throw new WyomingProtocolException("Event header contains a negative data_length.");
        }

        if (header.PayloadLength < 0)
        {
            throw new WyomingProtocolException("Event header contains a negative payload_length.");
        }
    }
}
