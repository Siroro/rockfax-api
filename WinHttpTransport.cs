using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace RockfaxApi;

/// <summary>
/// An <see cref="HttpMessageHandler"/> built directly on the Windows WinHTTP API
/// (winhttp.dll) — no subprocess, no external binary, fully in-process.
///
/// Why: app-live.ukclimbing.com sits behind Cloudflare bot management which issues a
/// managed challenge to requests made with .NET SocketsHttpHandler's TLS fingerprint
/// (regardless of headers, HTTP version or TLS version), while WinHTTP's native
/// ClientHello — like curl's and the Android app's OkHttp — is served normally when
/// the request carries the app's User-Agent. This is an unmodified, OS-shipped HTTP
/// stack: nothing about the handshake is forged or impersonated.
///
/// Scope: implements exactly what the Rockfax API needs (GET/POST, custom headers,
/// optional body, response headers including multiple Set-Cookie lines). HTTPS only.
/// </summary>
public sealed class WinHttpTransport : HttpMessageHandler
{
    // ---- WinHTTP imports (unicode variants) ---------------------------------

    private const int WINHTTP_ACCESS_TYPE_NO_PROXY = 1;
    private const int WINHTTP_FLAG_SECURE = 0x00800000;
    private const int WINHTTP_ADDREQ_FLAG_ADD = 0x20000000;
    private const int WINHTTP_QUERY_FLAG_NUMBER = 0x20000000;
    private const int WINHTTP_QUERY_STATUS_CODE = 19;
    private const int WINHTTP_QUERY_RAW_HEADERS_CRLF = 22;
    private const int WINHTTP_OPTION_REDIRECT_POLICY = 88;
    private const int WINHTTP_OPTION_REDIRECT_POLICY_DISALLOW_HTTPS_TO_HTTP = 2;

    [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr WinHttpOpen(string userAgent, int accessType, IntPtr proxy, IntPtr proxyBypass, int flags);

    [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr WinHttpConnect(IntPtr session, string host, int port, int reserved);

    [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr WinHttpOpenRequest(IntPtr connection, string verb, string objectName,
        string? version, IntPtr referer, IntPtr acceptTypes, int flags);

    [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WinHttpAddRequestHeaders(IntPtr request, string headers, int headerLength, int modifiers);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpSendRequest(IntPtr request, IntPtr headers, int headersLength,
        IntPtr optional, int optionalLength, int totalLength, IntPtr context);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpReceiveResponse(IntPtr request, IntPtr reserved);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpQueryHeaders(IntPtr request, int infoLevel, IntPtr name, byte[]? buffer, ref int bufferLength, IntPtr index);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpQueryDataAvailable(IntPtr request, out int available);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpReadData(IntPtr request, byte[] buffer, int bytesToRead, out int bytesRead);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpSetOption(IntPtr request, int option, ref int value, int length);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpSetTimeouts(IntPtr session, int resolveTimeout, int connectTimeout, int sendTimeout, int receiveTimeout);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpCloseHandle(IntPtr handle);

    private static readonly bool IsSupported = OperatingSystem.IsWindows();

    private readonly string _userAgent;
    private readonly TimeSpan _timeout;

    public WinHttpTransport(string userAgent, TimeSpan? timeout = null)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("WinHttpTransport requires Windows.");
        _userAgent = userAgent;
        _timeout = timeout ?? TimeSpan.FromSeconds(300);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // WinHTTP calls are blocking; run them off the caller's thread. The token
        // prevents a request that has not started yet; an in-flight WinHTTP call is
        // bounded by the per-transport timeout instead.
        return await Task.Run(() => SendCore(request), cancellationToken).ConfigureAwait(false);
    }

    private HttpResponseMessage SendCore(HttpRequestMessage request)
    {
        Uri uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
        if (uri.Scheme != "https" && uri.Scheme != "http")
            throw new NotSupportedException($"Scheme {uri.Scheme} is not supported.");

        IntPtr session = IntPtr.Zero, connection = IntPtr.Zero, hRequest = IntPtr.Zero;
        try
        {
            session = WinHttpOpen(_userAgent, WINHTTP_ACCESS_TYPE_NO_PROXY, IntPtr.Zero, IntPtr.Zero, 0);
            if (session == IntPtr.Zero) throw WinHttpError("WinHttpOpen");
            WinHttpSetTimeouts(session, 30_000, 30_000,
                (int)_timeout.TotalMilliseconds, (int)_timeout.TotalMilliseconds);

            connection = WinHttpConnect(session, uri.Host, uri.Port, 0);
            if (connection == IntPtr.Zero) throw WinHttpError("WinHttpConnect");

            string pathAndQuery = uri.PathAndQuery is "/" && string.IsNullOrEmpty(uri.Query) ? "/" : uri.PathAndQuery;
            hRequest = WinHttpOpenRequest(connection, request.Method.Method, pathAndQuery,
                null, IntPtr.Zero, IntPtr.Zero,
                uri.Scheme == "https" ? WINHTTP_FLAG_SECURE : 0);
            if (hRequest == IntPtr.Zero) throw WinHttpError("WinHttpOpenRequest");

            // Follow redirects (the photo CDN 301s cdn.ukc2.com -> ukc2.com) but never
            // downgrade https -> http. API endpoints never redirect.
            int redirectPolicy = WINHTTP_OPTION_REDIRECT_POLICY_DISALLOW_HTTPS_TO_HTTP;
            WinHttpSetOption(hRequest, WINHTTP_OPTION_REDIRECT_POLICY, ref redirectPolicy, sizeof(int));

            // Copy caller headers. WinHTTP owns Host; SendRequest owns Content-Length.
            var headerBuilder = new StringBuilder();
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                AppendHeader(headerBuilder, header.Key, header.Value);
            if (request.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                {
                    if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    AppendHeader(headerBuilder, header.Key, header.Value);
                }
            }
            if (headerBuilder.Length > 0)
            {
                if (!WinHttpAddRequestHeaders(hRequest, headerBuilder.ToString(), -1, WINHTTP_ADDREQ_FLAG_ADD))
                    throw WinHttpError("WinHttpAddRequestHeaders");
            }

            byte[]? body = request.Content is null
                ? null
                : request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

            IntPtr bodyPtr = IntPtr.Zero;
            try
            {
                int bodyLength = body?.Length ?? 0;
                if (body is { Length: > 0 })
                {
                    bodyPtr = Marshal.AllocHGlobal(bodyLength);
                    Marshal.Copy(body, 0, bodyPtr, bodyLength);
                }
                if (!WinHttpSendRequest(hRequest, IntPtr.Zero, 0, bodyPtr, bodyLength, bodyLength, IntPtr.Zero))
                    throw WinHttpError("WinHttpSendRequest");
            }
            finally
            {
                if (bodyPtr != IntPtr.Zero) Marshal.FreeHGlobal(bodyPtr);
            }

            if (!WinHttpReceiveResponse(hRequest, IntPtr.Zero))
                throw WinHttpError("WinHttpReceiveResponse");

            return BuildResponse(request, hRequest);
        }
        finally
        {
            if (hRequest != IntPtr.Zero) WinHttpCloseHandle(hRequest);
            if (connection != IntPtr.Zero) WinHttpCloseHandle(connection);
            if (session != IntPtr.Zero) WinHttpCloseHandle(session);
        }
    }

    private static void AppendHeader(StringBuilder builder, string name, IEnumerable<string> values)
    {
        if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) return;
        if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase)) return;
        foreach (string value in values)
            builder.Append(name).Append(": ").Append(value).Append("\r\n");
    }

    private static HttpResponseMessage BuildResponse(HttpRequestMessage request, IntPtr hRequest)
    {
        // Status code (numeric query).
        int statusCode = 0;
        int size = sizeof(int);
        byte[] codeBuffer = BitConverter.GetBytes(statusCode);
        if (!WinHttpQueryHeaders(hRequest, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                IntPtr.Zero, codeBuffer, ref size, IntPtr.Zero))
            throw WinHttpError("WinHttpQueryHeaders(status)");
        statusCode = BitConverter.ToInt32(codeBuffer, 0);

        // Full raw header block: preserves every line, including repeated Set-Cookie.
        int rawSize = 0;
        WinHttpQueryHeaders(hRequest, WINHTTP_QUERY_RAW_HEADERS_CRLF, IntPtr.Zero, null, ref rawSize, IntPtr.Zero);
        var response = new HttpResponseMessage((HttpStatusCode)statusCode) { RequestMessage = request };
        if (rawSize > 0)
        {
            byte[] rawBuffer = new byte[rawSize];
            if (WinHttpQueryHeaders(hRequest, WINHTTP_QUERY_RAW_HEADERS_CRLF, IntPtr.Zero, rawBuffer, ref rawSize, IntPtr.Zero))
            {
                string headers = Encoding.Unicode.GetString(rawBuffer, 0, rawSize);
                foreach (string line in headers.Split('\n'))
                {
                    string trimmed = line.TrimEnd('\r');
                    int colon = trimmed.IndexOf(':');
                    if (colon <= 0) continue;
                    string name = trimmed[..colon].Trim();
                    string value = trimmed[(colon + 1)..].Trim();
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!response.Headers.TryAddWithoutValidation(name, value))
                        response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        // Body via the read loop.
        using var body = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        while (WinHttpQueryDataAvailable(hRequest, out int available) && available > 0)
        {
            int totalRead = 0;
            while (totalRead < available)
            {
                if (!WinHttpReadData(hRequest, buffer, Math.Min(available - totalRead, buffer.Length), out int read) || read == 0)
                    throw WinHttpError("WinHttpReadData");
                body.Write(buffer, 0, read);
                totalRead += read;
            }
        }
        response.Content = new ByteArrayContent(body.ToArray());
        return response;
    }

    private static RockfaxApiException WinHttpError(string operation)
        => new($"{operation} failed (WinHTTP error {Marshal.GetLastWin32Error()}).", null, null);

}
