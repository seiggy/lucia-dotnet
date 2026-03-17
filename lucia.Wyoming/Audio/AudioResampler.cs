namespace lucia.Wyoming.Audio;

/// <summary>
/// Resamples PCM float audio between sample rates using linear interpolation.
/// </summary>
public static class AudioResampler
{
    /// <summary>
    /// Resamples the input audio from <paramref name="inputRate"/> to <paramref name="outputRate"/>.
    /// </summary>
    public static float[] Resample(ReadOnlySpan<float> input, int inputRate, int outputRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputRate);

        if (input.IsEmpty)
        {
            return [];
        }

        if (inputRate == outputRate)
        {
            return input.ToArray();
        }

        if (input.Length == 1)
        {
            return [input[0]];
        }

        var outputLength = Math.Max(1, (int)Math.Round(input.Length * (double)outputRate / inputRate));
        var result = new float[outputLength];
        var positionIncrement = inputRate / (double)outputRate;

        for (var i = 0; i < outputLength; i++)
        {
            var position = i * positionIncrement;
            var leftIndex = Math.Min((int)position, input.Length - 1);
            var rightIndex = Math.Min(leftIndex + 1, input.Length - 1);
            var fraction = position - leftIndex;
            var left = input[leftIndex];
            var right = input[rightIndex];

            result[i] = (float)(left + ((right - left) * fraction));
        }

        return result;
    }
}
