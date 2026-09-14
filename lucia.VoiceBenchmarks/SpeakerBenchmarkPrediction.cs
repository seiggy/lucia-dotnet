namespace lucia.VoiceBenchmarks;

public sealed record SpeakerBenchmarkPrediction
{
    public string ActualSpeakerId { get; init; }
    public string PredictedSpeakerId { get; init; }
    public double Score { get; init; }

    public SpeakerBenchmarkPrediction(string actualSpeakerId, string predictedSpeakerId, double score)
    {
        ActualSpeakerId = actualSpeakerId;
        PredictedSpeakerId = predictedSpeakerId;
        Score = score;
    }
}
