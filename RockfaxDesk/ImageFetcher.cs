using System.Drawing;
using System.Net;
using RockfaxApi;

namespace RockfaxDesk;

/// <summary>
/// Loads photos from the UKC CDN (cdn.ukc2.com), over the same WinHTTP transport as the
/// API client. Thumbnail cache with a hard cap to keep memory bounded.
/// </summary>
internal sealed class ImageFetcher : IDisposable
{
    internal const string ThumbBase = "https://cdn.ukc2.com/t_300h/";
    internal const string FullBase = "https://cdn.ukc2.com/i/";

    private readonly HttpClient _http = new(new WinHttpTransport("okhttp/3.8.0", TimeSpan.FromSeconds(30)))
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly Dictionary<int, Image> _thumbCache = new();
    private readonly Queue<int> _thumbOrder = new();
    private const int CacheCap = 240;

    public async Task<Image?> GetThumbAsync(int photoId)
    {
        lock (_thumbCache)
        {
            if (_thumbCache.TryGetValue(photoId, out Image? cached)) return cached;
        }

        Image? image = await LoadAsync(ThumbBase + photoId + ".jpg").ConfigureAwait(false);
        if (image is null) return null;

        lock (_thumbCache)
        {
            if (_thumbCache.Count >= CacheCap && _thumbOrder.Count > 0)
            {
                int evict = _thumbOrder.Dequeue();
                if (_thumbCache.Remove(evict, out Image? old)) old.Dispose();
            }
            _thumbCache[photoId] = image;
            _thumbOrder.Enqueue(photoId);
        }
        return image;
    }

    public Task<Image?> GetFullAsync(int photoId)
        => LoadAsync(FullBase + photoId + ".jpg");

    private async Task<Image?> LoadAsync(string url)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            using var ms = new MemoryStream(bytes);
            using Image raw = Image.FromStream(ms);
            return new Bitmap(raw); // detach from the stream
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
