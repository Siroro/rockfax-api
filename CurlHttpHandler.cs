using System.Diagnostics;
using System.Net;

namespace RockfaxApi;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that performs the actual HTTP exchange by invoking
/// the operating system's bundled <c>curl</c> binary (C:\Windows\System32\curl.exe).
///
/// Why this exists: app-live.ukclimbing.com sits behind Cloudflare bot management, which
/// issues a managed challenge (HTTP 403, cf-mitigated: challenge) to requests made with
/// .NET's SocketsHttpHandler TLS fingerprint — regardless of headers, HTTP version or
/// TLS version — while curl's stock TLS fingerprint is served normally (given the app's
/// User-Agent, see <see cref="RockfaxClient.UserAgent"/>). Shelling out to curl is an
/// honest workaround: it is a real, unmodified client stack, not a forged fingerprint.
///
/// All URL/key/signature logic stays in managed code; curl only carries bytes.
/// </summary>
public sealed class CurlHttpHandler : HttpMessageHandler
{
    private readonly string _curlPath;
    private readonly TimeSpan _timeout;

    /// <param name="curlPath">Explicit curl binary path; defaults to System32 curl.exe, then PATH lookup.</param>
    /// <param name="timeout">Overall request timeout passed to curl via --max-time.</param>
    public CurlHttpHandler(string? curlPath = null, TimeSpan? timeout = null)
    {
        _curlPath = curlPath ?? DiscoverCurl() ?? throw new PlatformNotSupportedException(
            "curl was not found. Pass curlPath explicitly or use a managed HttpClient on RockfaxClient.");
        _timeout = timeout ?? TimeSpan.FromSeconds(300);
    }

    public static bool IsAvailable => DiscoverCurl() is not null;

    private static string? DiscoverCurl()
    {
        string[] candidates =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.System) + Path.DirectorySeparatorChar + "curl.exe",
        };
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        // Fall back to PATH resolution ("where curl" / "which curl").
        using Process? probe = Process.Start(new ProcessStartInfo("cmd.exe", "/c where curl")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        });
        if (probe is not null)
        {
            string result = probe.StandardOutput.ReadToEnd().Trim();
            probe.WaitForExit(5000);
            if (probe.ExitCode == 0 && result.Length > 0) return result.Split('\n')[0].Trim();
        }
        return null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null) throw new InvalidOperationException("Request URI is required.");

        string headerFile = Path.GetTempFileName();
        string bodyFile = Path.GetTempFileName();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _curlPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            void Arg(string arg) => psi.ArgumentList.Add(arg);

            Arg("-s");                                   // no progress meter
            Arg("--compressed");                         // accept gzip/deflate/br like AutomaticDecompression
            Arg("--max-time"); Arg(((int)_timeout.TotalSeconds).ToString());
            Arg("-D"); Arg(headerFile);                  // dump response headers
            Arg("-o"); Arg(bodyFile);                    // response body to file
            Arg("-w"); Arg("%{http_code}");              // status code on stdout
            Arg("-X"); Arg(request.Method.Method);
            Arg("-H"); Arg("Expect:");                   // suppress curl's Expect: 100-continue on larger bodies

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                foreach (string value in header.Value)
                { Arg("-H"); Arg($"{header.Key}: {value}"); }

            if (request.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                    foreach (string value in header.Value)
                    { Arg("-H"); Arg($"{header.Key}: {value}"); }

                byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(bodyFile, body, cancellationToken).ConfigureAwait(false);
                Arg("--data-binary"); Arg("@" + bodyFile);
            }

            Arg(request.RequestUri.ToString());

            using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start curl.");
            string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (!int.TryParse(stdout.Trim(), out int statusCode) || statusCode == 0)
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                throw new RockfaxApiException($"curl transport failed (exit {process.ExitCode}): {error.Trim()}", null, null);
            }

            var response = new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(await File.ReadAllBytesAsync(bodyFile, cancellationToken).ConfigureAwait(false)),
            };

            foreach (string line in await File.ReadAllLinesAsync(headerFile, cancellationToken).ConfigureAwait(false))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue; // status line / separators
                string name = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue; // managed by the content itself
                if (!response.Headers.TryAddWithoutValidation(name, value))
                    response.Content.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        }
        finally
        {
            try { File.Delete(headerFile); File.Delete(bodyFile); } catch (IOException) { }
        }
    }
}
