using RockfaxDesk.Controls;
using Xunit;

namespace RockfaxApi.Tests;

/// <summary>
/// WinForms controls need an STA thread; each test marshals its body onto one.
/// No handles are created — the canvas math runs on properties alone.
/// </summary>
public class MapProjectionTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "test thread timed out");
        if (error is not null) throw error;
    }

    [Fact]
    public void Screen_center_maps_back_to_the_view_center() => RunSta(() =>
    {
        using var map = new CragMapCanvas();
        map.Size = new Size(800, 600);
        map.CenterOn(53.35f, -1.83f, 10f);

        (double lat, double lng) = map.ScreenToLatLng(new Point(400, 300));
        Assert.InRange(lat, 53.34, 53.36);
        Assert.InRange(lng, -1.84, -1.82);
    });

    [Fact]
    public void Web_mercator_roundtrip_is_lossless_to_sub_pixel() => RunSta(() =>
    {
        using var map = new CragMapCanvas();
        map.CenterOn(54.5f, -3.0f, 12f);

        foreach ((double lat, double lng) in new[] { (-40.0, 120.0), (0.0, 0.0), (60.0, -5.0) })
        {
            (double x, double y) = map.WorldXY(lat, lng);
            (double backLat, double backLng) = map.LatLngFromWorld(x, y);
            Assert.InRange(backLat, lat - 1e-6, lat + 1e-6);
            Assert.InRange(backLng, lng - 1e-6, lng + 1e-6);
        }
    });

    [Fact]
    public void CenterOn_clamps_latitude_and_zoom() => RunSta(() =>
    {
        using var map = new CragMapCanvas();
        map.CenterOn(99f, 200f, 99f);

        (double lat, _) = map.ScreenToLatLng(new Point(map.Width / 2, map.Height / 2));
        Assert.InRange(lat, -85.01, 85.01);
    });
}

/// <summary>Pins the per-pixel tile remap: light OSM paper darkens, dark features lift.</summary>
public class TileDarkeningTests
{
    [Fact]
    public void White_land_becomes_nearly_black()
    {
        using var src = new Bitmap(4, 4);
        using (var g = Graphics.FromImage(src)) g.Clear(Color.White);
        using Bitmap dark = CragMapCanvas.DarkenTile(src);

        Color px = dark.GetPixel(2, 2);
        Assert.Equal(255, px.A);
        Assert.True(px.R <= 20 && px.G <= 20 && px.B <= 20, $"expected near-black, got {px}");
    }

    [Fact]
    public void Black_water_lifts_to_slate_instead_of_crushing() 
    {
        using var src = new Bitmap(4, 4);
        using (var g = Graphics.FromImage(src)) g.Clear(Color.Black);
        using Bitmap dark = CragMapCanvas.DarkenTile(src);

        Color px = dark.GetPixel(2, 2);
        // f = 1 → B=51, G=55, R=65 per the remap formula.
        Assert.True(px.R is >= 45 and < 70, $"expected slate, got {px}");
        Assert.True(px.G is >= 45 and < 70, $"expected slate, got {px}");
        Assert.True(px.B is >= 45 and < 70, $"expected slate, got {px}");
    }

    [Fact]
    public void Tile_dimensions_are_preserved()
    {
        using var src = new Bitmap(256, 256);
        using (var g = Graphics.FromImage(src)) g.Clear(Color.Gray);
        using Bitmap dark = CragMapCanvas.DarkenTile(src);
        Assert.Equal(256, dark.Width);
        Assert.Equal(256, dark.Height);
    }
}
