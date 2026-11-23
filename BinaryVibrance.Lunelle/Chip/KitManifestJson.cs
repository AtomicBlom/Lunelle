using System.Text.Json.Serialization;

namespace BinaryVibrance.Lunelle.Chip;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(KitManifest))]
internal sealed partial class KitManifestJson : JsonSerializerContext;
