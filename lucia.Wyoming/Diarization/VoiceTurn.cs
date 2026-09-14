namespace lucia.Wyoming.Diarization;

public sealed record VoiceTurn(
    string Text,
    ReadOnlyMemory<float> Audio,
    int SampleRate,
    SpeakerIdentification? Speaker,
    DateTimeOffset CapturedAt);
