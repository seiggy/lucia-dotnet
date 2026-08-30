using System.Text.Json.Serialization;

namespace lucia.InstallerHost;

[JsonSerializable(typeof(InstallerConfigurationRequest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class InstallerJsonContext : JsonSerializerContext;
