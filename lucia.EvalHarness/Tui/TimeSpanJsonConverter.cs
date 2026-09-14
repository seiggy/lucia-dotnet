using System.Text.Json;
using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Tui;

internal sealed class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        TimeSpan.FromMilliseconds(reader.GetDouble());

    public override void Write(
        Utf8JsonWriter writer,
        TimeSpan value,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.TotalMilliseconds);
}
