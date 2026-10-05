using System.Text.Json.Serialization;

namespace RockfaxApi;

/// <summary>
/// One logbook ascent to upload. The app (RFAscentUtils.getAscentJsonObject) wraps these in
/// an outer JSON object keyed by a client-generated UUID; the server replies with the same
/// keys plus { success, ukcID } per entry. <see cref="RockfaxClient.AddAscentsAsync"/> handles that wrapping.
/// </summary>
public sealed class AscentUpload
{
    /// <summary>Client-side identity for this upload (server echoes it back). A fresh GUID works.</summary>
    [JsonIgnore]
    public string ObjectId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("routeUKCID")]
    public int RouteUkcId { get; set; }

    [JsonPropertyName("routeRockfaxID")]
    public int RouteRockfaxId { get; set; }

    /// <summary>Ascent date formatted as the app sends it (yyyy-MM-dd).</summary>
    [JsonPropertyName("textAscentDate")]
    public string AscentDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");

    /// <summary>UKC climbing style id (10 = Soloed, 20s = led, 30s = followed, ...). See RFAscentUtils for the full map.</summary>
    [JsonPropertyName("styleID")]
    public int StyleId { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>UKC partner user ids that were on the ascent.</summary>
    [JsonPropertyName("partners")]
    public List<int> PartnerIds { get; set; } = new();

    /// <summary>Unix seconds of the last edit; defaults to now.</summary>
    [JsonPropertyName("editDate")]
    public long EditDate { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Existing server ascent id, only set when editing a previously uploaded ascent.</summary>
    [JsonPropertyName("ukcID")]
    public int? UkcAscentId { get; set; }

    /// <summary>1 marks the ascent as deleted server-side.</summary>
    [JsonPropertyName("trash")]
    public int Trash { get; set; }
}
