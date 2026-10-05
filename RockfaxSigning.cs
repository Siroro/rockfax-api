using System.Security.Cryptography;
using System.Text;

namespace RockfaxApi;

/// <summary>
/// Request-signing primitives replicated from the Rockfax Android app
/// (com.rockfax.rockfax.rockfax), specifically:
///   - RFHashUtilsKt.md5()
///   - UKCAPI.getKey()            (per-endpoint "key" path segment)
///   - UKCAPIRequestInterceptor   (rfdigital-* auth headers)
/// </summary>
internal static class RockfaxSigning
{
    /// <summary>Salt appended to the key-input string before MD5 (from UKCAPI.getKey).</summary>
    internal const string PathKeySalt = "lfoengoir";

    /// <summary>Secret appended to the signing base string (UKCAPIRequestInterceptorKt.FATBOY_AUTH_API_SECRET).</summary>
    internal const string AuthApiSecret = "b916246d1b133943156cd8e0d7d65ee2";

    /// <summary>
    /// Lowercase hex MD5, zero-padded to 32 chars — identical to the app's RFHashUtilsKt.md5().
    /// </summary>
    internal static string Md5(string input)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Derives the "key" path segment that prefixes every API route.
    /// App code: "and_" + md5(input + "lfoengoir").substring(17, 29)
    /// </summary>
    /// <param name="keyInput">
    /// The endpoint-specific input string. The app uses:
    ///   ""                                     for all POST endpoints, freeCrags, weeklyTopTen
    ///   "123%7C456" (pipe-encoded ids)         for crag/route/photo/weather id-list endpoints
    ///   "t=&lt;unixtime&gt;"                    for crag markers and free listings
    ///   "userID=&lt;id&gt;"                     for wishlist / partners / logbook route details
    ///   "userID=&lt;id&gt;&amp;t=&lt;unixtime&gt;" for the logbook endpoint
    ///   "&lt;routeId&gt;"                      for route info / crag routes
    ///   "&lt;raw search text&gt;"              for route search
    /// </param>
    internal static string DerivePathKey(string keyInput)
        => "and_" + Md5(keyInput + PathKeySalt).Substring(17, 12);

    /// <summary>
    /// Builds the rfdigital-api-key header value:
    /// md5(requestDate + encodedPath + query + body + FATBOY_AUTH_API_SECRET).
    /// </summary>
    /// <param name="requestDate">Same string sent in the rfdigital-request-date header (yyyy-MM-dd'T'HH:mm:ss, local time).</param>
    /// <param name="encodedPath">URL-encoded path, e.g. /and_xxxxxxxxxxxx/logbook/v1/crag_ukc/123%7C456</param>
    /// <param name="query">Raw query string without '?', or "" when the request has none.</param>
    /// <param name="body">Exact request body bytes as a string (form string or JSON), or "" when bodyless.</param>
    internal static string DeriveApiKey(string requestDate, string encodedPath, string query, string body)
        => Md5(requestDate + encodedPath + query + body + AuthApiSecret);

    /// <summary>
    /// yyyy-MM-dd'T'HH:mm:ss in local time — the app's RFUnixtimeUtils.timestampIso8601().
    /// </summary>
    internal static string RequestDateNow()
        => DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss");
}
