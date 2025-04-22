

using System.Text.Json.Serialization;

namespace Quartz.Core;
public record struct QuartzMessageHeader(
    [property: JsonPropertyName("UID")] string Uid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("dataType")] string DataType,
    [property: JsonPropertyName("receiver")] string Receiver,
    [property: JsonPropertyName("type")] string Type)
{
    [JsonPropertyName("dataLen")] public int DataLen { get; init; }
    [JsonPropertyName("interval")] public int Interval { get; init; }
}
