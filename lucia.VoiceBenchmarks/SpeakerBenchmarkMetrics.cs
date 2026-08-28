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

        var rankedScores = genuineScores
            .Select(static score => (Score: score, IsGenuine: true))
            .Concat(impostorScores.Select(static score => (Score: score, IsGenuine: false)))
            .OrderByDescending(static item => item.Score)
            .GroupBy(static item => item.Score);
        var acceptedGenuine = 0;
        var acceptedImpostor = 0;
        var bestDifference = double.MaxValue;
        var equalErrorRate = double.MaxValue;
        var previousFalseAcceptanceRate = 0d;
        var previousDelta = -1d;

        UpdateCandidate(falseAcceptanceRate: 0d, falseRejectionRate: 1d);
        foreach (var scoreGroup in rankedScores)
        {
            foreach (var score in scoreGroup)
            {
                if (score.IsGenuine)
                {
                    acceptedGenuine++;
                }
                else
                {
                    acceptedImpostor++;
                }
            }

            var falseAcceptanceRate = acceptedImpostor / (double)impostorScores.Count;
            var falseRejectionRate = (genuineScores.Count - acceptedGenuine) / (double)genuineScores.Count;
            var delta = falseAcceptanceRate - falseRejectionRate;
            if (delta == 0d)
            {
                return falseAcceptanceRate;
            }
            if (previousDelta < 0d && delta > 0d)
            {
                var weight = -previousDelta / (delta - previousDelta);
                return previousFalseAcceptanceRate
                    + (weight * (falseAcceptanceRate - previousFalseAcceptanceRate));
            }

            UpdateCandidate(falseAcceptanceRate, falseRejectionRate);
            previousFalseAcceptanceRate = falseAcceptanceRate;
            previousDelta = delta;
        }

        return equalErrorRate;

        void UpdateCandidate(double falseAcceptanceRate, double falseRejectionRate)
        {
            var difference = Math.Abs(falseAcceptanceRate - falseRejectionRate);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                equalErrorRate = (falseAcceptanceRate + falseRejectionRate) / 2d;
            }
        }
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

    public static (double FalseAcceptanceRate, double FalseRejectionRate) ComputeErrorRates(
        IReadOnlyList<double> genuineScores,
        IReadOnlyList<double> impostorScores,
        double threshold)
    {
        if (genuineScores.Count == 0 || impostorScores.Count == 0)
        {
            throw new ArgumentException("Genuine and impostor scores are required.");
        }

        var falseAcceptanceRate =
            impostorScores.Count(score => score >= threshold) / (double)impostorScores.Count;
        var falseRejectionRate =
            genuineScores.Count(score => score < threshold) / (double)genuineScores.Count;
        return (falseAcceptanceRate, falseRejectionRate);
    }

    public static double ComputeNormalizedMinDcf(
        IReadOnlyList<double> genuineScores,
        IReadOnlyList<double> impostorScores,
        double targetPrior)
    {
        if (targetPrior is <= 0d or >= 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(targetPrior));
        }

        var thresholds = genuineScores
            .Concat(impostorScores)
            .Distinct()
            .Append(double.PositiveInfinity);
        var minimumCost = thresholds.Min(threshold =>
        {
            var (falseAcceptanceRate, falseRejectionRate) =
                ComputeErrorRates(genuineScores, impostorScores, threshold);
            return (targetPrior * falseRejectionRate)
                + ((1d - targetPrior) * falseAcceptanceRate);
        });

        return minimumCost / Math.Min(targetPrior, 1d - targetPrior);
    }
}
