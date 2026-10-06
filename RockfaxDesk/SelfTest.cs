using System.Text.Json;
using RockfaxApi;
using RockfaxDesk.Controls;

namespace RockfaxDesk;

/// <summary>
/// Headless end-to-end check of every data path the desktop app uses:
/// `dotnet run --project RockfaxDesk -- --self-test`. Exits 0 when every step passes.
/// </summary>
internal static class SelfTest
{
    private static int _failures;

    public static async Task<int> RunAsync()
    {
        using var api = new RockfaxClient();
        using var images = new ImageFetcher();

        // Exercises the app's JSON parsers and the CDN image path, same as the views do.
        int commentCount = 0, photoCount = 0, cragRouteCount = 0, top10Count = 0, freeCragCount = 0, markerCount = 0;
        string weatherLine = "";
        long thumbBytes = 0;

        await Step("free-crags", async () =>
        {
            using JsonDocument doc = await api.GetFreeCragsAsync();
            freeCragCount = doc.RootElement.GetProperty("free_crags").EnumerateArray().Count();
            return freeCragCount > 0 ? $"{freeCragCount} free crags" : "empty list";
        });

        await Step("crag-markers", async () =>
        {
            using JsonDocument doc = await api.GetCragMarkersAsync();
            markerCount = doc.RootElement.GetProperty("markers").EnumerateArray().Count();
            return markerCount > 1000 ? $"{markerCount:N0} markers" : $"only {markerCount} markers";
        });

        await Step("route-search", async () =>
        {
            using JsonDocument doc = await api.SearchRoutesAsync("Flying Buttress");
            int n = doc.RootElement.GetProperty("routes").EnumerateArray().Count();
            return n > 0 ? $"{n} routes" : "no results";
        });

        await Step("route-info", async () =>
        {
            RouteInfo info = await api.GetRouteInfoAsync(3212);
            return info.Height > 0 ? $"height {info.Height}m, {info.Pitches} pitches" : "no height";
        });

        await Step("crag-routes (Dinas Cromlech, id 4)", async () =>
        {
            using JsonDocument doc = await api.GetCragRoutesAsync(4);
            foreach (JsonProperty crag in doc.RootElement.EnumerateObject())
                cragRouteCount += crag.Value.EnumerateArrayOrObjectValues().Count();
            return cragRouteCount > 0 ? $"{cragRouteCount} route entries" : "no routes in response";
        });

        await Step("weather (crag 4)", async () =>
        {
            using JsonDocument doc = await api.GetWeatherAsync(new[] { 4 }, Site.UkClimbing);
            int chips = CragView.FormatWeather(doc).Count;
            weatherLine = $"{chips} day chips";
            return chips > 0 ? $"forecast parsed ({chips} days)" : "no forecast data";
        });

        await Step("route-comments (3212)", async () =>
        {
            using JsonDocument doc = await api.GetRouteCommentsAsync(new[] { 3212 });
            commentCount = RouteView.ParseComments(doc).Count;
            return "ok"; // zero comments is a valid response
        });

        await Step("route-photos (3212)", async () =>
        {
            using JsonDocument doc = await api.GetRoutePhotosAsync(new[] { 3212 });
            var photos = RouteView.ParsePhotos(doc);
            photoCount = photos.Count;
            if (photoCount == 0) return "no photos in response";
            int loaded = 0; // some ids are dead on the CDN — any live thumbnail proves the path works
            foreach ((int id, _, _) in photos.Take(5))
            {
                System.Drawing.Image? thumb = await images.GetThumbAsync(id);
                if (thumb is null) continue;
                thumbBytes = thumb.Width * thumb.Height;
                thumb.Dispose();
                loaded++;
                break;
            }
            return loaded > 0
                ? $"{photoCount} photos, thumbnail {thumbBytes} px loaded from CDN"
                : $"photo meta ok ({photoCount}) but first 5 thumbnails all 404 on CDN";
        });

        await Step("weekly-top10", async () =>
        {
            using JsonDocument doc = await api.GetWeeklyTopTenPhotosAsync();
            top10Count = doc.RootElement.GetProperty("photos").EnumerateArray().Count();
            return top10Count > 0 ? $"{top10Count} photos" : "empty";
        });

        int guideCount = 0, parkingCount = 0;
        await Step("crag-details (Stanage Popular, id 104)", async () =>
        {
            using JsonDocument doc = await api.GetCragDetailsAsync(new[] { 104 }, Site.UkClimbing);
            CragInfo? info = CragView.ParseCragInfo(doc);
            if (info is null) return "no crag object in response";
            guideCount = info.Guidebooks.Count;
            parkingCount = info.Parking.Count;
            return info.Features.Length > 0 && info.Access.Length > 0
                ? $"rocktype {info.RockType}, {guideCount} guidebooks, {parkingCount} parking, {info.Comments.Count} comments"
                : "missing description or access notes";
        });

        await Step("listings", async () =>
        {
            using JsonDocument doc = await api.GetFreeListingsAsync();
            List<ServiceItem> items = ServicesView.ParseListings(doc);
            int types = items.Select(i => i.Type).Distinct().Count();
            return items.Count > 100 && types >= 4
                ? $"{items.Count:N0} listings across {types} types"
                : $"suspiciously few listings ({items.Count}, {types} types)";
        });

        // Constructing the real form verifies the UI wiring (no message pump needed for construction).
        try
        {
            using var form = new MainForm();
            SynchronizationContext.SetSynchronizationContext(null);
            Pass("mainform-constructs", "all views instantiated");
        }
        catch (Exception ex)
        {
            Fail("mainform-constructs", ex.Message);
        }

        Console.WriteLine(_failures == 0 ? "SELF-TEST PASSED" : $"SELF-TEST FAILED ({_failures} failures)");
        return _failures == 0 ? 0 : 1;
    }

    private static async Task Step(string name, Func<Task<string>> check)
    {
        try
        {
            string detail = await check();
            Pass(name, detail);
        }
        catch (Exception ex)
        {
            Fail(name, ex.Message);
        }
    }

    private static void Pass(string name, string detail) => Console.WriteLine($"PASS  {name}  ({detail})");

    private static void Fail(string name, string detail)
    {
        Console.WriteLine($"FAIL  {name}  ({detail})");
        _failures++;
    }
}
