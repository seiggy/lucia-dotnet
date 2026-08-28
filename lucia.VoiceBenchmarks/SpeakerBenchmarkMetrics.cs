namespace lucia.VoiceBenchmarks;

public static class SpeakerBenchmarkMetrics
{
    public static double ComputeEer(IReadOnlyList<double> genuineScores, IReadOnlyList<double> impostorScores)
    {
        if (genuineScores.Count == 0)
        {
            throw new ArgumentException("At least one genuine score is required.", nameof(genuineScores));
        }

        if (impostorScores.Count == 0)
        {
            throw new ArgumentException("At least one impostor score is required.", nameof(impostorScores));
        }

        var genuine = genuineScores.OrderBy(static score => score).ToArray();
        var impostor = impostorScores.OrderBy(static score => score).ToArray();
        var uniqueValues = genuine.Concat(impostor).Distinct().OrderBy(static score => score).ToArray();

        var thresholds = new SortedSet<double>();
        foreach (var value in uniqueValues)
        {
            thresholds.Add(value);
        }

        foreach (var pair in uniqueValues.Zip(uniqueValues.Skip(1), (left, right) => (left, right)))
        {
            thresholds.Add((pair.left + pair.right) / 2d);
        }

        thresholds.Add(uniqueValues[0] - 1d);
        thresholds.Add(uniqueValues[^1] + 1d);

        var bestDifference = double.MaxValue;
        var equalErrorRate = double.MaxValue;
        foreach (var threshold in thresholds)
        {
            var falseAcceptanceRate = impostor.Count(score => score >= threshold) / (double)impostor.Length;
            var falseRejectionRate = genuine.Count(score => score < threshold) / (double)genuine.Length;
            var difference = Math.Abs(falseAcceptanceRate - falseRejectionRate);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                equalErrorRate = (falseAcceptanceRate + falseRejectionRate) / 2d;
            }
        }

        return equalErrorRate;
    }

    public static double ComputeTop1Accuracy(IReadOnlyList<SpeakerBenchmarkPrediction> predictions)
    {
        if (predictions.Count == 0)
        {
            throw new ArgumentException("At least one prediction is required.", nameof(predictions));
        }

        var total = predictions.Count;
        var correct = predictions.Count(prediction =>
            string.Equals(prediction.ActualSpeakerId, prediction.PredictedSpeakerId, StringComparison.OrdinalIgnoreCase));

        return correct / (double)total;
    }
}
