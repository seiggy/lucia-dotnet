namespace lucia.Wyoming.Audio;

/// <summary>
/// Thread-safe ring buffer for streaming float audio samples.
/// </summary>
public sealed class AudioBuffer
{
    private readonly float[] _buffer;
    private readonly object _syncRoot = new();
    private int _writeIndex;
    private int _count;

    public AudioBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _buffer = new float[capacity];
    }

    /// <summary>
    /// Gets the number of samples available to read.
    /// </summary>
    public int Available
    {
        get
        {
            lock (_syncRoot)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Writes samples into the buffer, overwriting the oldest data when full.
    /// </summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_syncRoot)
        {
            foreach (var sample in samples)
            {
                _buffer[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % _buffer.Length;

                if (_count < _buffer.Length)
                {
                    _count++;
                }
            }
        }
    }

    /// <summary>
    /// Reads and removes up to <paramref name="count"/> samples from the buffer.
    /// </summary>
    public float[] Read(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        lock (_syncRoot)
        {
            var samplesToRead = Math.Min(count, _count);
            if (samplesToRead == 0)
            {
                return [];
            }

            var result = new float[samplesToRead];
            var readIndex = (_writeIndex - _count + _buffer.Length) % _buffer.Length;
            var firstSegmentLength = Math.Min(samplesToRead, _buffer.Length - readIndex);

            _buffer.AsSpan(readIndex, firstSegmentLength).CopyTo(result);

            var remaining = samplesToRead - firstSegmentLength;
            if (remaining > 0)
            {
                _buffer.AsSpan(0, remaining).CopyTo(result.AsSpan(firstSegmentLength));
            }

            _count -= samplesToRead;
            return result;
        }
    }

    /// <summary>
    /// Clears all buffered samples.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            Array.Clear(_buffer);
            _writeIndex = 0;
            _count = 0;
        }
    }
}
