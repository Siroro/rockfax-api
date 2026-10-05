using System.Text.Json.Serialization;

namespace RockfaxApi;

/// <summary>
/// Typed view of GET /logbook/v1/route_info_ukc/{id} — matches the app's RouteInfoUKC
/// Gson model (field names are the Java field names).
/// </summary>
public sealed class RouteInfo
{
    [JsonPropertyName("desc")]
    public string Description { get; set; } = "";

    [JsonPropertyName("fa")]
    public string FirstAscent { get; set; } = "";

    [JsonPropertyName("faDate")]
    public string FirstAscentDate { get; set; } = "";

    [JsonPropertyName("rockfaxDesc")]
    public string RockfaxDescription { get; set; } = "";

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("pitches")]
    public int Pitches { get; set; }
}
