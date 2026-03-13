using System.Text.Json.Serialization;

namespace lucia.Wyoming.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModelArchitecture
{
    ZipformerTransducer,
    ZipformerCtc,
    Paraformer,
    Conformer,
    NemoFastConformer,
    NemoParakeet,
    NemoNemotron,
    NemoCanary,
    Whisper,
    SenseVoice,
    Lstm,
    Telespeech,
    Unknown,
}
