using System.Text.Json.Serialization;

namespace RockfaxApi;

/// <summary>
/// One wishlist entry to upload (RFAscentUtils.getWishlistJsonObject):
/// { "&lt;uuid&gt;": { "route": id, "trash": 0, "editDate": unix } }.
/// "route" holds the UKC route id for ukc-site uploads or the Rockfax route id for rockfax-site uploads.
/// </summary>
public sealed class WishlistUpload
{
    [JsonIgnore]
    public string ObjectId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("route")]
    public int RouteId { get; set; }

    [JsonPropertyName("trash")]
    public int Trash { get; set; }

    [JsonPropertyName("editDate")]
    public long EditDate { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
