using System.Drawing;
using System.Net;
using RockfaxApi;

namespace RockfaxDesk;

/// <summary>
/// Loads photos from the UKC CDN (cdn.ukc2.com), over the same WinHTTP transport as the
/// API client. Thumbnail cache with a hard cap to keep memory bounded. Some photos have
/// no t_300h variant on the CDN (weekly top-10 entries, for instance) — those fall back
/// to the full-size image, downscaled locally.
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
    private readonly Dictionary<int, Task<Image?>> _inFlight = new();
    private readonly Queue<int> _thumbOrder = new();
    private const int CacheCap = 240;

    public async Task<Image?> GetThumbAsync(int photoId)
    {
        Task<Image?>? inFlight;
        lock (_thumbCache)
        {
            if (_thumbCache.TryGetValue(photoId, out Image? cached)) return cached;
            _inFlight.TryGetValue(photoId, out inFlight);
            if (inFlight is null)
            {
                inFlight = LoadThumbAsync(photoId);
                _inFlight[photoId] = inFlight;
            }
        }
        return await inFlight.ConfigureAwait(false);
    }

    private async Task<Image?> LoadThumbAsync(int photoId)
    {
        try
        {
            Image? image = await LoadAsync(ThumbBase + photoId + ".jpg").ConfigureAwait(false)
                           ?? await LoadAsync(FullBase + photoId + ".jpg", downscaleTo: 300).ConfigureAwait(false);
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
        finally
        {
            lock (_thumbCache) { _inFlight.Remove(photoId); }
        }
    }

    public Task<Image?> GetFullAsync(int photoId)
        => LoadAsync(FullBase + photoId + ".jpg");

    private async Task<Image?> LoadAsync(string url, int? downscaleTo = null)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                using var ms = new MemoryStream(bytes);
                using Image raw = Image.FromStream(ms);
                if (downscaleTo is int target && raw.Height > target)
                {
                    int w = raw.Width * target / raw.Height;
                    var small = new Bitmap(raw, w, target);
                    return small;
                }
                return new Bitmap(raw); // detach from the stream
            }
            catch
            {
                if (attempt == 1) return null; // one retry for transient connection drops
                await Task.Delay(300).ConfigureAwait(false);
            }
        }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
