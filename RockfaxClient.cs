using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RockfaxApi;

/// <summary>
/// Unofficial .NET client for the Rockfax / UKClimbing app API, replicated from the
/// Rockfax Android app (com.rockfax.rockfax.rockfax, class com.rockfax.rockfax.ukcapi.php.UKCAPI).
///
/// Wire protocol:
///   - Every route is prefixed with a "key" path segment: and_ + md5(keyInput + "lfoengoir")[17..29).
///   - When logged in, every request carries:
///       Cookie:                     api_auth=&lt;accessToken&gt;:&lt;deviceId&gt;;
///       rfdigital-access-token:     &lt;accessToken&gt;
///       rfdigital-request-date:     yyyy-MM-dd'T'HH:mm:ss (local time)
///       rfdigital-api-key:          md5(requestDate + path + query + body + secret)
///
/// Use your own UKClimbing account credentials, and keep request volume reasonable —
/// this is an unofficial client and the service owner has no obligation to tolerate it.
/// </summary>
public sealed class RockfaxClient : IDisposable
{
    public const string DefaultBaseUrl = "https://app-live.ukclimbing.com";
    public const string DefaultNodeBaseUrl = "https://dev.api.rfd.lol";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>Install/device identifier sent as deviceID and embedded in the auth cookie. The app uses a random UUID truncated to 31 chars.</summary>
    public string DeviceId { get; }

    /// <summary>Reported device name during login (the app sends Build.MODEL).</summary>
    public string DeviceName { get; set; } = "RockfaxApi";

    /// <summary>
    /// User-Agent. The API sits behind Cloudflare bot management which challenges
    /// unknown agents, so this defaults to the OkHttp version the app ships with
    /// (okhttp3.internal.Version in the APK reports 3.8.0).
    /// </summary>
    public string UserAgent { get; set; } = "okhttp/3.8.0";

    public string BaseUrl { get; }
    public string NodeBaseUrl { get; }

    // ---- Session state -------------------------------------------------

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public int UserId { get; private set; }
    public string? Username { get; private set; }
    public long TokenExpiresUnix { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

    public RockfaxClient(string? deviceId = null, string? baseUrl = null, string? nodeBaseUrl = null, HttpClient? http = null,
                         bool preferCurlTransport = true)
    {
        BaseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        NodeBaseUrl = (nodeBaseUrl ?? DefaultNodeBaseUrl).TrimEnd('/');
        DeviceId = string.IsNullOrEmpty(deviceId)
            ? Guid.NewGuid().ToString()[..31]  // mirrors RFRealm: UUID.randomUUID().toString().substring(0, 31)
            : deviceId!;

        HttpMessageHandler handler;
        if (http is not null)
        {
            handler = new PassthroughHandler(http);
        }
        else if (preferCurlTransport && CurlHttpHandler.IsAvailable)
        {
            // The API's Cloudflare bot management challenges .NET's TLS fingerprint;
            // the OS curl binary is served normally. See CurlHttpHandler remarks.
            handler = new CurlHttpHandler();
        }
        else
        {
            handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };
        }

        _http = http ?? new HttpClient(handler);
        _ownsHttp = true;
        _http.Timeout = Timeout.InfiniteTimeSpan; // per-request timeout lives in the handler (curl --max-time / 300s)
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    /// <summary>Wraps a caller-supplied HttpClient without adding another handler layer.</summary>
    private sealed class PassthroughHandler(HttpClient client) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // =====================================================================
    //  Auth
    // =====================================================================

    /// <summary>POST {key}/user/v1/auth_ukc — log in with UKClimbing credentials and store the session.</summary>
    public async Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        string body = FormBody(
            ("email", email),
            ("password", password),
            ("deviceID", DeviceId),
            ("device_name", DeviceName));

        AuthResponse session = await PostAuthAsync("user/v1/auth_ukc", body, ct).ConfigureAwait(false);
        return session;
    }

    /// <summary>POST {key}/user/v1/refresh_ukc — exchange the refresh token for a new access token.</summary>
    public async Task<AuthResponse> RefreshAccessTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(RefreshToken))
            throw new InvalidOperationException("No refresh token stored; call LoginAsync first.");

        string body = FormBody(
            ("userID", UserId.ToString()),
            ("refreshToken", RefreshToken),
            ("deviceID", DeviceId));

        return await PostAuthAsync("user/v1/refresh_ukc", body, ct).ConfigureAwait(false);
    }

    private async Task<AuthResponse> PostAuthAsync(string path, string formBody, CancellationToken ct)
    {
        (string rawJson, string? accessToken) = await PostFormForAuthAsync(path, formBody, ct).ConfigureAwait(false);

        AuthResponse response = JsonSerializer.Deserialize<AuthResponse>(rawJson)
            ?? throw new RockfaxApiException("Auth response body was not valid JSON.", null, rawJson);
        response.AccessToken ??= accessToken;

        AccessToken = response.AccessToken
            ?? throw new RockfaxApiException("Auth succeeded but no access token was returned (neither in body nor Set-Cookie).", null, rawJson);
        RefreshToken = response.RefreshToken;
        UserId = response.UserId;
        Username = response.Username;
        TokenExpiresUnix = response.ExpiresUnix;
        return response;
    }

    // =====================================================================
    //  User
    // =====================================================================

    /// <summary>POST {key}/user/v1/app_permissions_v3/ — body { user_id, device_id }.</summary>
    public Task<JsonDocument> GetAppPermissionsAsync(int userId, CancellationToken ct = default)
    {
        string json = new JsonObject
        {
            ["user_id"] = userId,
            ["device_id"] = DeviceId,
        }.ToJsonString();
        return PostJsonAsync($"user/{V1}/app_permissions_v3/", keyInput: "", jsonBody: json, ct: ct);
    }

    // =====================================================================
    //  Logbook
    // =====================================================================

    /// <summary>GET {key}/logbook/v1/crag_{site}/{ids} — crag details (ukc / rockfax / ukh).</summary>
    public Task<JsonDocument> GetCragDetailsAsync(IReadOnlyCollection<int> cragIds, Site site = Site.Rockfax, CancellationToken ct = default)
    {
        string encoded = EncodeIds(cragIds);
        return GetAsync($"logbook/{V1}/crag_{site.ToRouteSegment()}/{encoded}", keyInput: encoded, ct: ct);
    }

    /// <summary>GET {key}/logbook/v1/crag_routes_ukc/{id} — all routes at a UKC crag.</summary>
    public Task<JsonDocument> GetCragRoutesAsync(int cragId, CancellationToken ct = default)
        => GetAsync($"logbook/{V1}/crag_routes_ukc/{cragId}", keyInput: cragId.ToString(), ct: ct);

    /// <summary>GET {key}/logbook/v1/crags_ukc/t={t} — every UKC crag map marker, or those updated since unix time t.</summary>
    public Task<JsonDocument> GetCragMarkersAsync(long updatedSince = 0, CancellationToken ct = default)
        => GetAsync($"logbook/{V1}/crags_ukc/t={updatedSince}", keyInput: $"t={updatedSince}", ct: ct);

    /// <summary>GET {key}/logbook/v1/logbook_{site}/userID={userId}&amp;t={t} — the user's ascents, or those updated since unix time t.</summary>
    public Task<JsonDocument> GetLogbookAsync(int userId, long updatedSince = 0, Site site = Site.UkClimbing, CancellationToken ct = default)
        => GetAsync($"logbook/{V1}/logbook_{site.ToRouteSegment()}/userID={userId}&t={updatedSince}",
                    keyInput: $"userID={userId}&t={updatedSince}", ct: ct);

    /// <summary>GET {key}/logbook/v1/logbook_route_details_{site}/userID={userId} — crag/route details for everything in the user's logbook.</summary>
    public Task<JsonDocument> GetLogbookRouteDetailsAsync(int userId, Site site = Site.UkClimbing, CancellationToken ct = default)
        => GetAsync($"logbook/{V1}/logbook_route_details_{site.ToRouteSegment()}/userID={userId}",
                    keyInput: $"userID={userId}", ct: ct);

    /// <summary>GET {key}/logbook/v1/partners/userID={userId} — the user's climbing partners.</summary>
    public Task<JsonDocument> GetPartnersAsync(int userId, CancellationToken ct = default)
        => GetAsync($"logbook/{V1}/partners/userID={userId}", keyInput: $"userID={userId}", ct: ct);

    /// <summary>GET {key}/logbook/v1/wishlist_{site}/userID={userId}.</summary>
    public Task<JsonDocument> GetWishlistAsync(int userId, Site site = Site.UkClimbing, CancellationToken ct = default)
        => GetAsync($"logbook/{V1}/wishlist_{site.ToRouteSegment()}/userID={userId}",
                    keyInput: $"userID={userId}", ct: ct);

    /// <summary>GET {key}/logbook/v1/route_comments_{site}/{ids}.</summary>
    public Task<JsonDocument> GetRouteCommentsAsync(IReadOnlyCollection<int> routeIds, Site site = Site.UkClimbing, CancellationToken ct = default)
    {
        string encoded = EncodeIds(routeIds);
        return GetAsync($"logbook/{V1}/route_comments_{site.ToRouteSegment()}/{encoded}", keyInput: encoded, ct: ct);
    }

    /// <summary>GET {key}/logbook/v1/route_info_ukc/{id} — typed route description.</summary>
    public async Task<RouteInfo> GetRouteInfoAsync(int routeId, CancellationToken ct = default)
    {
        using JsonDocument doc = await GetAsync($"logbook/{V1}/route_info_ukc/{routeId}", keyInput: routeId.ToString(), ct: ct)
            .ConfigureAwait(false);
        return doc.Deserialize<RouteInfo>(SerializeOptions)
            ?? throw new RockfaxApiException($"Route info for id {routeId} could not be parsed.", null, null);
    }

    /// <summary>GET {key}/logbook/v1/app_search_route_ukc/{query} — search UKC routes by name.</summary>
    public Task<JsonDocument> SearchRoutesAsync(string query, CancellationToken ct = default)
        => GetAsync($"logbook/{V1}/app_search_route_ukc/{Uri.EscapeDataString(query)}", keyInput: query, ct: ct);

    /// <summary>GET {key}/logbook/v2/weather_{site}/{ids} — note: the app calls this one with v2.</summary>
    public Task<JsonDocument> GetWeatherAsync(IReadOnlyCollection<int> cragIds, Site site = Site.Rockfax, CancellationToken ct = default)
    {
        string encoded = EncodeIds(cragIds);
        return GetAsync($"logbook/{V2}/weather_{site.ToRouteSegment()}/{encoded}", keyInput: encoded, ct: ct);
    }

    /// <summary>
    /// POST {key}/logbook/v1/logbook_add_{site}/ — upload ascents. The payload is wrapped as
    /// { "&lt;objectId&gt;": {ascent fields}, ... }; the server responds per key with { success, ukcID }.
    /// </summary>
    public Task<JsonDocument> AddAscentsAsync(int userId, IReadOnlyList<AscentUpload> ascents, Site site = Site.UkClimbing, CancellationToken ct = default)
    {
        var wrapper = new JsonObject();
        foreach (AscentUpload ascent in ascents)
            wrapper[ascent.ObjectId] = JsonSerializer.SerializeToNode(ascent, SerializeOptions);

        return PostFormAsync(
            $"logbook/{V1}/logbook_add_{site.ToRouteSegment()}/",
            ("userID", userId.ToString()),
            ("jsonData", wrapper.ToJsonString()));
    }

    /// <summary>POST {key}/logbook/v1/wishlist_add_{site}/.</summary>
    public Task<JsonDocument> AddWishlistAsync(int userId, IReadOnlyList<WishlistUpload> entries, Site site = Site.UkClimbing, CancellationToken ct = default)
    {
        var wrapper = new JsonObject();
        foreach (WishlistUpload entry in entries)
            wrapper[entry.ObjectId] = JsonSerializer.SerializeToNode(entry, SerializeOptions);

        return PostFormAsync(
            $"logbook/{V1}/wishlist_add_{site.ToRouteSegment()}/",
            ("userID", userId.ToString()),
            ("jsonData", wrapper.ToJsonString()));
    }

    /// <summary>POST {key}/logbook/v1/partner_add/ — add a partner by name; response contains the new ukcID.</summary>
    public Task<JsonDocument> AddPartnerAsync(int userId, string name, CancellationToken ct = default)
        => PostFormAsync(
            $"logbook/{V1}/partner_add/",
            ("userID", userId.ToString()),
            ("name", name),
            ("appID", DeviceId));

    // =====================================================================
    //  Store
    // =====================================================================

    /// <summary>
    /// GET {key}/store/v1/freeCrags/ — ids of crags available as free samples.
    /// Response: { free_crags: [...], key: md5 checksum } (checksum input: "rockfax_app/crags/packages/maps_and_overviews/" + concatenated ids).
    /// </summary>
    public Task<JsonDocument> GetFreeCragsAsync(CancellationToken ct = default)
        => GetAsync($"store/{V1}/freeCrags/", keyInput: "", ct: ct);

    // =====================================================================
    //  Photos
    // =====================================================================

    /// <summary>GET {key}/photos/v1/check_crags_{site}/{ids} — photo counts per crag.</summary>
    public Task<JsonDocument> GetCragPhotoCountsAsync(IReadOnlyCollection<int> cragIds, Site site = Site.Rockfax, CancellationToken ct = default)
    {
        string encoded = EncodeIds(cragIds);
        return GetAsync($"photos/{V1}/check_crags_{site.ToRouteSegment()}/{encoded}", keyInput: encoded, ct: ct);
    }

    /// <summary>GET {key}/photos/v1/crag_{site}/{ids}/{timestamp} — crag photos changed since unix time timestamp.</summary>
    public Task<JsonDocument> GetCragPhotosAsync(IReadOnlyCollection<int> cragIds, Site site = Site.Rockfax, int updatedSince = 0, CancellationToken ct = default)
    {
        string encoded = EncodeIds(cragIds);
        return GetAsync($"photos/{V1}/crag_{site.ToRouteSegment()}/{encoded}/{updatedSince}", keyInput: encoded, ct: ct);
    }

    /// <summary>GET {key}/photos/v1/{listingType}_photos/{ids}.</summary>
    public Task<JsonDocument> GetListingPhotosAsync(string listingType, IReadOnlyCollection<int> listingIds, CancellationToken ct = default)
    {
        string encoded = EncodeIds(listingIds);
        return GetAsync($"photos/{V1}/{listingType}_photos/{encoded}", keyInput: encoded, ct: ct);
    }

    /// <summary>GET {key}/photos/v1/route_{site}/{ids}.</summary>
    public Task<JsonDocument> GetRoutePhotosAsync(IReadOnlyCollection<int> routeIds, Site site = Site.UkClimbing, CancellationToken ct = default)
    {
        string encoded = EncodeIds(routeIds);
        return GetAsync($"photos/{V1}/route_{site.ToRouteSegment()}/{encoded}", keyInput: encoded, ct: ct);
    }

    /// <summary>GET {key}/photos/v1/weekly_top10_{site}.</summary>
    public Task<JsonDocument> GetWeeklyTopTenPhotosAsync(Site site = Site.UkClimbing, CancellationToken ct = default)
        => GetAsync($"photos/{V1}/weekly_top10_{site.ToRouteSegment()}", keyInput: "", ct: ct);

    // =====================================================================
    //  Listings (classifieds)
    // =====================================================================

    /// <summary>GET {key}/listings/v1/free/t={t} — free classified listings, or those updated since unix time t.</summary>
    public Task<JsonDocument> GetFreeListingsAsync(long updatedSince = 0, CancellationToken ct = default)
        => GetAsync($"listings/{V1}/free/t={updatedSince}", keyInput: $"t={updatedSince}", ct: ct);

    // =====================================================================
    //  Analytics
    // =====================================================================

    /// <summary>POST {key}/analytics/v1/timesplit/ (Content-Type: application/json).</summary>
    public Task<JsonDocument> UploadUsageAnalyticsAsync(JsonObject payload, CancellationToken ct = default)
        => PostJsonAsync($"analytics/{V1}/timesplit/", keyInput: "", jsonBody: payload.ToJsonString(),
                         contentType: "application/json", ct: ct);

    // =====================================================================
    //  Node API (separate service)
    // =====================================================================

    /// <summary>
    /// POST {nodeBaseUrl}/v1/rockfax/featured_areas — featured areas for the subscription type
    /// named in the JSON body (the app posts the active SubscriptionTypes value).
    /// </summary>
    public Task<JsonDocument> GetFeaturedAreasAsync(JsonObject payload, CancellationToken ct = default)
        => PostJsonAsync("/v1/rockfax/featured_areas", keyInput: null, jsonBody: payload.ToJsonString(),
                         contentType: "application/json", nodeApi: true, ct: ct);

    // =====================================================================
    //  Plumbing
    // =====================================================================

    private const string V1 = "v1";
    private const string V2 = "v2";

    /// <summary>Ids joined exactly as they appear on the wire (Retrofit encodes '|' as %7C).</summary>
    private static string EncodeIds(IReadOnlyCollection<int> ids)
        => string.Join("%7C", ids);

    private static string FormBody(params (string Name, string Value)[] fields)
        => string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Name)}={Uri.EscapeDataString(f.Value)}"));

    private Task<JsonDocument> GetAsync(string path, string keyInput, CancellationToken ct)
        => SendAsync(HttpMethod.Get, path, keyInput, query: null, content: null, bodyForSignature: "", ct: ct);

    private Task<JsonDocument> PostFormAsync(string path, params (string Name, string Value)[] fields)
        => PostFormAsync(path, ct: default, fields);

    private Task<JsonDocument> PostFormAsync(string path, CancellationToken ct, params (string Name, string Value)[] fields)
    {
        string body = FormBody(fields);
        return SendAsync(HttpMethod.Post, path, keyInput: "", query: null,
                         content: new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
                         bodyForSignature: body, ct: ct);
    }

    private Task<JsonDocument> PostJsonAsync(string path, string? keyInput, string jsonBody, string? contentType = null, bool nodeApi = false, CancellationToken ct = default)
    {
        // The app posts app_permissions_v3 with RequestBody.create(null, json) — no content type.
        HttpContent content = contentType is null
            ? new StringContent(jsonBody, Encoding.UTF8)
            : new StringContent(jsonBody, Encoding.UTF8, contentType);
        return SendAsync(HttpMethod.Post, path, keyInput, query: null, content, jsonBody, ct, nodeApi);
    }

    private async Task<(string Json, string? AccessTokenFromCookie)> PostFormForAuthAsync(string path, string formBody, CancellationToken ct)
    {
        string url = BuildUrl(path, keyInput: "", nodeApi: false, out string wirePath);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(formBody, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };

        // Auth requests themselves are unauthenticated (no session yet) but still carry the
        // rfdigital signature when a session exists — same global interceptor behaviour.
        using HttpResponseMessage response = await SendCoreAsync(request, wirePath, query: "", bodyForSignature: formBody, autoRefresh: false, ct).ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string? cookieToken = ExtractAccessTokenFromSetCookie(response);
        return (json, cookieToken);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? keyInput, string? query,
                                               HttpContent? content, string bodyForSignature, CancellationToken ct, bool nodeApi = false)
    {
        string url = BuildUrl(path, keyInput, nodeApi, out string wirePath);

        await EnsureFreshTokenAsync(ct).ConfigureAwait(false);

        using var request = new HttpRequestMessage(method, url) { Content = content };
        using HttpResponseMessage response = await SendCoreAsync(request, wirePath, query ?? "", bodyForSignature, autoRefresh: true, ct).ConfigureAwait(false);

        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new RockfaxApiException($"Response was not valid JSON ({(int)response.StatusCode}).", response.StatusCode, json, ex);
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, string wirePath, string query,
                                                          string bodyForSignature, bool autoRefresh, CancellationToken ct)
    {
        // Mirror UKCAPIRequestInterceptor: when a session exists every request gets the
        // cookie + the three rfdigital-* headers; otherwise the request goes out bare.
        if (IsLoggedIn)
        {
            string requestDate = RockfaxSigning.RequestDateNow();
            string apiKey = RockfaxSigning.DeriveApiKey(requestDate, wirePath, query, bodyForSignature);

            request.Headers.TryAddWithoutValidation("Cookie", $"api_auth={AccessToken}:{DeviceId};");
            request.Headers.TryAddWithoutValidation("rfdigital-access-token", AccessToken);
            request.Headers.TryAddWithoutValidation("rfdigital-request-date", requestDate);
            request.Headers.TryAddWithoutValidation("rfdigital-api-key", apiKey);
        }

        HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new RockfaxApiException($"API returned {(int)response.StatusCode} {response.ReasonPhrase} for {request.Method} {wirePath}",
                                          response.StatusCode, error);
        }
        return response;
    }

    /// <summary>Auto-refresh when the access token is within a day of expiry (the app refreshes on the same boundary).</summary>
    private async Task EnsureFreshTokenAsync(CancellationToken ct)
    {
        if (!IsLoggedIn || TokenExpiresUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds() > 86400)
            return;

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TokenExpiresUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds() > 86400) return;
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private string BuildUrl(string path, string? keyInput, bool nodeApi, out string wirePath)
    {
        string host = nodeApi ? NodeBaseUrl : BaseUrl;

        // Node-API paths are absolute (/v1/rockfax/...); UKC API paths sit under the {key} segment.
        if (nodeApi || keyInput is null)
        {
            string p = path.StartsWith('/') ? path : "/" + path;
            wirePath = p;
            return host + p;
        }

        string key = RockfaxSigning.DerivePathKey(keyInput);
        wirePath = "/" + key + (path.StartsWith('/') ? path : "/" + path);
        return host + wirePath;
    }

    /// <summary>Parses api_auth=&lt;token&gt;:&lt;deviceId&gt; out of Set-Cookie, as the app's CookieUtilsKt does.</summary>
    private static string? ExtractAccessTokenFromSetCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
            return null;

        foreach (string cookie in cookies)
        {
            if (!cookie.StartsWith("api_auth=", StringComparison.Ordinal)) continue;
            int start = "api_auth=".Length;
            int end = cookie.IndexOf(':', start);
            if (end <= start) continue;
            string token = cookie[start..end];
            return token.Length == 0 ? null : token;
        }
        return null;
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}

/// <summary>Thrown for non-2xx API responses or unparsable payloads.</summary>
public sealed class RockfaxApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? ResponseBody { get; }

    internal RockfaxApiException(string message, HttpStatusCode? statusCode, string? responseBody, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
