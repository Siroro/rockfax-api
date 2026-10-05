using System.Text.Json.Serialization;

namespace RockfaxApi;

/// <summary>
/// Session returned by POST /user/v1/auth_ukc and /user/v1/refresh_ukc.
/// Field names match the server JSON exactly.
/// </summary>
public sealed class AuthResponse
{
    /// <summary>Server-side "expires" field: unix seconds when the access token expires.</summary>
    [JsonPropertyName("expires")]
    public long ExpiresUnix { get; set; }

    /// <summary>Long-lived token used to obtain new access tokens.</summary>
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    /// <summary>UKC numeric user id.</summary>
    [JsonPropertyName("userID")]
    public int UserId { get; set; }

    /// <summary>Account display name.</summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    /// <summary>
    /// Short-lived access token. The server also returns it in a Set-Cookie
    /// (api_auth=&lt;token&gt;:&lt;deviceId&gt;); the app parses it out of the cookie,
    /// this client reads the cookie directly.
    /// </summary>
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }
}
