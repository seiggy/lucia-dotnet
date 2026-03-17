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
    private readonly Stream _stream;
    private readonly WyomingOptions _options;
    private readonly byte[] _readBuffer;
    private int _bufferStart;
    private int _bufferEnd;

    public WyomingEventParser(Stream stream, WyomingOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        _stream = stream;
        _options = options;
        _readBuffer = new byte[_options.MaxHeaderLineBytes];
    }

    /// <summary>
    /// Read the next Wyoming event from the stream.
    /// Returns <see langword="null"/> if the stream is closed before the next header starts.
    /// </summary>
    public async Task<WyomingEvent?> ReadEventAsync(CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ReadTimeoutSeconds));

        try
        {
            var headerLine = await ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
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

            ValidateLengths(header, _options);

            if (header.DataLength > 0)
            {
                var extraData = await ReadAndValidateExtraDataAsync(header.DataLength, timeoutCts.Token).ConfigureAwait(false);
                if (extraData is not null)
                {
                    var mergedData = header.Data is null
                        ? new Dictionary<string, object>()
                        : new Dictionary<string, object>(header.Data);

                    foreach (var kvp in extraData)
                    {
                        mergedData[kvp.Key] = kvp.Value;
                    }

                    header = new WyomingEventHeader
                    {
                        Type = header.Type,
                        Data = mergedData,
                        DataLength = header.DataLength,
                        PayloadLength = header.PayloadLength,
                    };
                }
            }

            byte[]? payload = null;
            if (header.PayloadLength > 0)
            {
                payload = GC.AllocateUninitializedArray<byte>(header.PayloadLength);
                await ReadExactAsync(payload, timeoutCts.Token).ConfigureAwait(false);
            }

            return WyomingEventFactory.Create(header, payload);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new WyomingProtocolException("Read timeout exceeded");
        }
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
            if (bufferedCount >= _options.MaxHeaderLineBytes)
            {
                throw new WyomingProtocolException(
                    $"Header line exceeds maximum length of {_options.MaxHeaderLineBytes} bytes.");
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

    private async Task<Dictionary<string, object>?> ReadAndValidateExtraDataAsync(int dataLength, CancellationToken ct)
    {
        var rented = ArrayPool<byte>.Shared.Rent(dataLength);
        try
        {
            await ReadExactAsync(rented.AsMemory(0, dataLength), ct).ConfigureAwait(false);

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(rented.AsSpan(0, dataLength));
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

    private static void ValidateLengths(WyomingEventHeader header, WyomingOptions options)
    {
        if (header.DataLength < 0)
        {
            throw new WyomingProtocolException("Event header contains a negative data_length.");
        }

        if (header.DataLength > options.MaxDataLength)
        {
            throw new WyomingProtocolException(
                $"data_length {header.DataLength} exceeds maximum {options.MaxDataLength}");
        }

        if (header.PayloadLength < 0)
        {
            throw new WyomingProtocolException("Event header contains a negative payload_length.");
        }

        if (header.PayloadLength > options.MaxPayloadLength)
        {
            throw new WyomingProtocolException(
                $"payload_length {header.PayloadLength} exceeds maximum {options.MaxPayloadLength}");
        }
    }
}
