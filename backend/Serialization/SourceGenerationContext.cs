using System.Text.Json.Serialization;

namespace ReVerse.Capture.Serialization;

[JsonSerializable(typeof(Dictionary<string, Capturing.ResponseOverrideEntry>))]
[JsonSerializable(typeof(Capturing.ResponseOverrideEntry))]
[JsonSerializable(typeof(Dictionary<string, Capturing.ContractEntry>))]
[JsonSerializable(typeof(Capturing.ContractEntry))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}