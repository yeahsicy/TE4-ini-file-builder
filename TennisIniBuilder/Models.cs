using System.Text.Json;
using System.Text.Json.Serialization;

namespace TennisIniBuilder;

/// <summary>A player's accumulated data across ranking snapshots plus real bio fields.</summary>
public sealed class PlayerRecord
{
    public string Id = "";
    public string Name = "";
    public string Country = "";
    public string Dob = "";
    public Dictionary<int, int> Ranks = new();   // year -> year-end rank

    // Enriched bio (null when not published anywhere)
    public string? Hand;        // "Right", "Left 2HBH", ...
    public int? Height;         // cm
    public int? Weight;         // kg
    public int? CareerHigh;     // best singles ranking ever
}

/// <summary>Bio sidecar record. ATP: c,d,h,w,ph,bh. WTA: h,ph,ch (+bh from Wikipedia).</summary>
public sealed class Bio
{
    [JsonPropertyName("c")] public string? Country { get; set; }
    [JsonPropertyName("d")] public string? Dob { get; set; }
    [JsonPropertyName("h")][JsonConverter(typeof(FlexibleIntConverter))] public int? Height { get; set; }
    [JsonPropertyName("w")][JsonConverter(typeof(FlexibleIntConverter))] public int? Weight { get; set; }
    [JsonPropertyName("ph")] public string? PlayHand { get; set; }
    [JsonPropertyName("bh")] public string? Backhand { get; set; }
    [JsonPropertyName("ch")][JsonConverter(typeof(FlexibleIntConverter))] public int? CareerHigh { get; set; }
}

/// <summary>Reads an int that a source may encode as a JSON number or string (e.g. ATP heights).</summary>
public sealed class FlexibleIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return null;
            case JsonTokenType.Number: return reader.GetInt32();
            case JsonTokenType.String:
                var s = reader.GetString();
                return int.TryParse(s, out var v) ? v : null;
            default: return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value);
    }
}
