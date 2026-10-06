using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using RockfaxApi;

namespace RockfaxDesk.Controls;

internal sealed class CragPoint
{
    public float Lat, Lng;
    public int UkcId, RockfaxId, NRoutes;
    public string Title = "";
    public bool Free;
}

/// <summary>
/// Pannable, zoomable crag map over a dark OpenStreetMap basemap (CARTO "dark_all"
/// raster tiles, Web Mercator / slippy-map projection). Crag dots glow on top;
/// hover labels, click-to-open, on-canvas zoom controls, km scale bar, attribution.
/// Free-sample crags burn amber.
/// </summary>
internal sealed class CragMapCanvas : Control
{
    private IReadOnlyList<CragPoint> _points = Array.Empty<CragPoint>();
    private (float Lat, float Lng) _center = (54.6f, -2.8f); // Britain
    private double _zoom = 5.6;                              // continuous; tiles at floor(zoom)
    private const double MinZoom = 2.5, MaxZoom = 16;
    private PointF _pan = PointF.Empty;                      // screen-px offset of the center

    private bool _dragging;
    private Point _dragMouseStart;
    private PointF _panStart;
    private CragPoint? _hover;
    private CragPoint? _selected;
    private int _clusterCount;
    private Point _hoverPoint;

    private readonly TileCache _tiles;

    public event Action<CragPoint>? CragSelected;

    public CragMapCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Ui.BgDeep;
        Cursor = Cursors.Hand;
        _tiles = new TileCache(this);
    }

    public IReadOnlyList<CragPoint> Points
    {
        get => _points;
        set
        {
            _points = value;
            Invalidate(); // keep viewport: repaints must not lose the selection or view
        }
    }

    public string Subtitle => _points.Count == 0 ? "" : $"{_points.Count:N0} crags · zoom ×{_zoom:0.0}";

    /// <summary>Saves/restores the viewport across sessions.</summary>
    public (float Zoom, float CenterLat, float CenterLng) GetView()
        => ((float)_zoom, _center.Lat, _center.Lng);

    public void ApplyView(float zoom, float centerLat, float centerLng)
    {
        _zoom = Math.Clamp((double)zoom, MinZoom, MaxZoom);
        _center = (clampLat(centerLat), centerLng);
        _pan = PointF.Empty;
        Invalidate();
    }

    public void ResetView()
    {
        _zoom = 5.6;
        _center = (54.6f, -2.8f);
        _pan = PointF.Empty;
        _hover = null;
        Invalidate();
    }

    /// <summary>Rings the dot for a crag opened elsewhere (list, route page).</summary>
    public void SelectCrag(int ukcId)
    {
        _selected = _points.FirstOrDefault(p => p.UkcId == ukcId);
        if (_selected is not null) Invalidate();
    }

    /// <summary>Centers the view on a coordinate at a given zoom (crag page "show on map").</summary>
    public void CenterOn(float lat, float lng, float zoom)
    {
        _zoom = Math.Clamp((double)zoom, MinZoom, MaxZoom);
        _center = (clampLat(lat), lng);
        _pan = PointF.Empty;
        Invalidate();
    }

    private static float clampLat(float lat) => Math.Clamp(lat, -85f, 85f);

    // ---- Web Mercator -------------------------------------------------------
    // worldSize(z) = 256 * 2^z; screen = worldPt - worldCenter + W/2/H/2 + pan.

    private double WorldSize => 256.0 * Math.Pow(2, _zoom);

    private (double X, double Y) WorldXY(double lat, double lng)
    {
        double x = (lng + 180.0) / 360.0 * WorldSize;
        double latRad = lat * Math.PI / 180.0;
        double y = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * WorldSize;
        return (x, y);
    }

    private (double Lat, double Lng) LatLngFromWorld(double x, double y)
    {
        double lng = x / WorldSize * 360.0 - 180.0;
        double n = Math.PI * (1.0 - 2.0 * y / WorldSize);
        double lat = Math.Atan(Math.Sinh(n)) * 180.0 / Math.PI;
        return (lat, lng);
    }

    private (float X, float Y) Project(CragPoint p)
    {
        (double wx, double wy) = WorldXY(p.Lat, p.Lng);
        (double cx, double cy) = WorldXY(_center.Lat, _center.Lng);
        return ((float)(Width / 2.0 + (wx - cx) + _pan.X), (float)(Height / 2.0 + (wy - cy) + _pan.Y));
    }

    private (double Lat, double Lng) ScreenToLatLng(Point screen)
    {
        (double cx, double cy) = WorldXY(_center.Lat, _center.Lng);
        return LatLngFromWorld(cx + screen.X - Width / 2.0 - _pan.X, cy + screen.Y - Height / 2.0 - _pan.Y);
    }

    // ---- painting -----------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.BgDeep);

        DrawTiles(g, e.ClipRectangle);

        if (_points.Count == 0)
        {
            TextRenderer.DrawText(g, "loading crags…", Ui.H2, ClientRectangle, Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        DrawDots(g, e.ClipRectangle);
        DrawHoverAndSelection(g);
        DrawLegend(g);
        DrawScaleBar(g);
        DrawAttribution(g);
        DrawZoomControls(g);
    }

    /// <summary>Draws the visible OpenStreetMap tiles (pre-darkened at decode); missing ones are fetched in the background.</summary>
    private void DrawTiles(Graphics g, Rectangle clip)
    {
        int intZoom = Math.Clamp((int)Math.Floor(_zoom), 0, 19);
        double scale = WorldSize / (256 << intZoom); // on-screen px per tile px
        double tilePx = 256 * scale;

        // Absolute world-px of the clip corners. WorldXY(ScreenToLatLng(corner)) already
        // yields centered-free absolute coordinates — subtracting the center again here
        // collapsed the tile range to the map's north-west corner (the invisible-tiles bug).
        (double tlLat, double tlLng) = ScreenToLatLng(new Point(clip.Left, clip.Top));
        (double brLat, double brLng) = ScreenToLatLng(new Point(clip.Right, clip.Bottom));
        (double ax, double ay) = WorldXY(tlLat, tlLng);
        (double bx, double by) = WorldXY(brLat, brLng);
        (double cxw, double cyw) = WorldXY(_center.Lat, _center.Lng);

        int max = (1 << intZoom) - 1;
        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) / tilePx));
        int x1 = Math.Min(max, (int)Math.Floor(Math.Max(ax, bx) / tilePx));
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) / tilePx));
        int y1 = Math.Min(max, (int)Math.Floor(Math.Max(ay, by) / tilePx));

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        for (int ty = y0; ty <= y1; ty++)
        {
            for (int tx = x0; tx <= x1; tx++)
            {
                // Same relationship as Project(): screen = world - center + half + pan.
                float px = (float)(tx * tilePx - cxw + Width / 2.0 + _pan.X);
                float py = (float)(ty * tilePx - cyw + Height / 2.0 + _pan.Y);
                Image? tile = _tiles.Get(intZoom, tx, ty);
                if (tile is not null)
                {
                    g.DrawImage(tile, px, py, (float)tilePx + 1f, (float)tilePx + 1f);
                }
                else
                {
                    _tiles.RequestAsync(intZoom, tx, ty);
                }
            }
        }
    }

    /// <summary>
    /// Remaps light OSM colors to a dark slate that fits the app, per pixel:
    /// f = (255 - luminance) / 255; out = base + f · weight. Land (bright) lands near
    /// the base tint, water/forests stay a touch lighter, white roads go darkest.
    /// Runs once per tile at decode time (256×256 = trivial), so painting stays a
    /// plain DrawImage with no color-matrix quirks.
    /// </summary>
    internal static Bitmap DarkenTile(Image source)
    {
        var tile = new Bitmap(source.Width, source.Height);
        using (Graphics g = Graphics.FromImage(tile))
        {
            g.Clear(Ui.BgDeep);
            g.DrawImageUnscaled(source, 0, 0);
        }
        var rect = new Rectangle(0, 0, tile.Width, tile.Height);
        var data = tile.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        unsafe
        {
            for (int y = 0; y < data.Height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < data.Width; x++)
                {
                    int i = x * 4;
                    byte b = row[i], gr = row[i + 1], r = row[i + 2]; // BGRA
                    float f = 1f - (0.299f * r + 0.587f * gr + 0.114f * b) / 255f;
                    row[i] = (byte)Math.Min(40 * f + 11, 255);
                    row[i + 1] = (byte)Math.Min(40 * f + 15, 255);
                    row[i + 2] = (byte)Math.Min(52 * f + 13, 255);
                }
            }
        }
        tile.UnlockBits(data);
        return tile;
    }

    private void DrawDots(Graphics g, Rectangle clip)
    {
        foreach (CragPoint p in _points)
        {
            (float x, float y) = Project(p);
            if (x < clip.Left - 12 || x > clip.Right + 12 || y < clip.Top - 12 || y > clip.Bottom + 12) continue;

            Color core = p.Free ? Ui.Amber : p.NRoutes > 100 ? Color.FromArgb(215, 240, 255) : Color.FromArgb(96, 175, 235);
            float r = _zoom < 6 ? 1.8f
                    : p.NRoutes > 100 ? 5f
                    : p.NRoutes > 20 ? 4f
                    : 3f;
            if (_zoom >= 10) r += 1.5f;

            using (var glow = new SolidBrush(Color.FromArgb(p.Free ? 56 : 34, core)))
                g.FillEllipse(glow, x - r * 2.2f, y - r * 2.2f, r * 4.4f, r * 4.4f);
            using (var ring = new Pen(Color.FromArgb(150, 8, 13, 25), 1.4f))
                g.DrawEllipse(ring, x - r / 2, y - r / 2, r, r);
            using var coreBrush = new SolidBrush(core);
            g.FillEllipse(coreBrush, x - r / 2, y - r / 2, r, r);
        }
    }

    private void DrawHoverAndSelection(Graphics g)
    {
        if (_hover is null && _clusterCount > 1)
        {
            string label = $"{_clusterCount} crags in this area — zoom in";
            SizeF size = TextRenderer.MeasureText(label, Ui.BodyBold);
            int lx = Math.Min(_hoverPoint.X + 12, Width - (int)size.Width - 18);
            int ly = Math.Min(Math.Max(_hoverPoint.Y - 11, 4), Height - (int)size.Height - 10);
            var box = new Rectangle(lx, ly, (int)size.Width + 12, (int)size.Height + 6);
            using (var back = new SolidBrush(Color.FromArgb(216, 10, 16, 30)))
                g.FillRectangle(back, box);
            using (var edge = new Pen(Ui.Border))
                g.DrawRectangle(edge, box);
            TextRenderer.DrawText(g, label, Ui.BodyBold, new Point(box.X + 6, box.Y + 3), Ui.Text);
            return;
        }

        foreach ((CragPoint? p, bool strong) in new[] { (_hover, false), (_selected, true) })
        {
            if (p is null) continue;
            (float x, float y) = Project(p);
            using var ring = new Pen(strong ? Ui.Accent : Color.FromArgb(210, 255, 255, 255), strong ? 2f : 1.4f);
            g.DrawEllipse(ring, x - 8, y - 8, 16, 16);

            string label = p.Free ? $"★ {p.Title}  ·  {p.NRoutes} routes  ·  free sample"
                                  : $"{p.Title}  ·  {p.NRoutes} routes";
            SizeF size = TextRenderer.MeasureText(label, Ui.BodyBold);
            int lx = Math.Min((int)x + 12, Width - (int)size.Width - 18);
            int ly = Math.Min(Math.Max((int)y - 11, 4), Height - (int)size.Height - 10);
            var box = new Rectangle(lx, ly, (int)size.Width + 12, (int)size.Height + 6);
            using (var back = new SolidBrush(Color.FromArgb(216, 10, 16, 30)))
                g.FillRectangle(back, box);
            using (var edge = new Pen(strong ? Ui.Accent : Ui.Border))
                g.DrawRectangle(edge, box);
            TextRenderer.DrawText(g, label, Ui.BodyBold, new Point(box.X + 6, box.Y + 3), strong ? Ui.Accent : Ui.Text);
        }
    }

    private void DrawLegend(Graphics g)
    {
        const string legend = "● amber = free sample    ● large = 100+ routes    drag = pan    wheel = zoom    click a dot = open crag";
        SizeF size = TextRenderer.MeasureText(legend, Ui.Small);
        var box = new Rectangle(10, Height - (int)size.Height - 30, (int)size.Width + 16, (int)size.Height + 8);
        using (var back = new SolidBrush(Color.FromArgb(170, 8, 13, 25)))
            g.FillRectangle(back, box);
        TextRenderer.DrawText(g, legend, Ui.Small, new Point(box.X + 8, box.Y + 3), Ui.Muted);
    }

    /// <summary>Km scale bar: meters per pixel = 156543.03 · cos(lat) / 2^zoom.</summary>
    private void DrawScaleBar(Graphics g)
    {
        double metersPerPx = 156543.03392 * Math.Cos(_center.Lat * Math.PI / 180) / Math.Pow(2, _zoom);
        double maxKm = 110 * metersPerPx / 1000;
        double nice = maxKm switch
        {
            >= 500 => 500, >= 250 => 250, >= 100 => 100, >= 50 => 50, >= 25 => 25,
            >= 10 => 10, >= 5 => 5, >= 2 => 2, >= 1 => 1, _ => 0.5,
        };
        int px = (int)(nice * 1000 / metersPerPx);
        int x = Width - px - 26, y = Height - 36;
        using var pen = new Pen(Color.FromArgb(200, 133, 152, 180), 2f);
        g.DrawLine(pen, x, y, x + px, y);
        g.DrawLine(pen, x, y - 4, x, y + 4);
        g.DrawLine(pen, x + px, y - 4, x + px, y + 4);
        TextRenderer.DrawText(g, nice < 1 ? $"{nice:0.0} km" : $"{nice:0} km", Ui.Small, new Point(x + px / 2 - 22, y - 22), Ui.Muted);
    }

    private void DrawAttribution(Graphics g)
    {
        const string attribution = "© OpenStreetMap contributors";
        SizeF size = TextRenderer.MeasureText(attribution, Ui.Tiny);
        int x = Width - (int)size.Width - 10;
        var box = new Rectangle(x, Height - (int)size.Height - 8, (int)size.Width + 8, (int)size.Height + 4);
        using var back = new SolidBrush(Color.FromArgb(160, 8, 13, 25));
        g.FillRectangle(back, box);
        TextRenderer.DrawText(g, attribution, Ui.Tiny, new Point(box.X + 4, box.Y + 1), Color.FromArgb(120, 138, 165));
    }

    private Rectangle ZoomInRect => new(Width - 112, 12, 32, 28);
    private Rectangle ZoomOutRect => new(Width - 76, 12, 32, 28);
    private Rectangle ZoomResetRect => new(Width - 40, 12, 28, 28);

    private void DrawZoomControls(Graphics g)
    {
        DrawZoomButton(g, ZoomInRect, "+", _zoom < MaxZoom);
        DrawZoomButton(g, ZoomOutRect, "−", _zoom > MinZoom);
        DrawZoomButton(g, ZoomResetRect, "⌂", true);
    }

    private static void DrawZoomButton(Graphics g, Rectangle r, string glyph, bool enabled)
    {
        using var face = new SolidBrush(enabled ? Ui.Card : Ui.Panel);
        g.FillRectangle(face, r);
        using var edge = new Pen(Ui.Border);
        g.DrawRectangle(edge, r);
        TextRenderer.DrawText(g, glyph, Ui.H2, r, enabled ? Ui.Text : Color.FromArgb(80, 95, 120),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    // ---- interaction ----------------------------------------------------------

    private bool InZoomArea(Point p)
        => ZoomInRect.Contains(p) || ZoomOutRect.Contains(p) || ZoomResetRect.Contains(p);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !InZoomArea(e.Location))
        {
            _dragging = true;
            _dragMouseStart = e.Location;
            _panStart = _pan;
            Cursor = Cursors.SizeAll;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            _pan = new PointF(_panStart.X + (e.X - _dragMouseStart.X), _panStart.Y + (e.Y - _dragMouseStart.Y));
            Invalidate();
        }
        else
        {
            CragPoint? nearest = InZoomArea(e.Location) ? null : Nearest(e.Location, 10);
            _hoverPoint = e.Location;
            _clusterCount = nearest is null ? ClusterAt(e.Location, 22) : 0;
            if (!ReferenceEquals(nearest, _hover) || _clusterCount > 1)
            {
                _hover = nearest;
                Cursor = InZoomArea(e.Location) ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool wasDrag = _dragging && Math.Abs(e.X - _dragMouseStart.X) + Math.Abs(e.Y - _dragMouseStart.Y) > 4;
        _dragging = false;
        Cursor = Cursors.Hand;

        if (e.Button == MouseButtons.Left)
        {
            if (ZoomInRect.Contains(e.Location)) ZoomBy(1.5, e.Location);
            else if (ZoomOutRect.Contains(e.Location)) ZoomBy(1 / 1.5, e.Location);
            else if (ZoomResetRect.Contains(e.Location)) ResetView();
            else if (!wasDrag && Nearest(e.Location, 10) is { } crag)
            {
                _selected = crag;
                CragSelected?.Invoke(crag);
            }
        }
        base.OnMouseUp(e);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        if (e is MouseEventArgs me && !InZoomArea(me.Location)) ZoomBy(1.5, me.Location);
        base.OnDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ZoomBy(e.Delta > 0 ? 1.25 : 1 / 1.25, e.Location);
        base.OnMouseWheel(e);
    }

    /// <summary>Zooms anchored at the cursor: the geo point under it stays under it.</summary>
    private void ZoomBy(double factor, Point anchor)
    {
        (double lat, double lng) = ScreenToLatLng(anchor);
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _pan = PointF.Empty;
        _center = (clampLat((float)lat), (float)lng);
        // re-anchor: shift pan so the same lat/lng returns to the cursor
        (float sx, float sy) = Project(new CragPoint { Lat = (float)lat, Lng = (float)lng });
        _pan = new PointF(anchor.X - sx, anchor.Y - sy);
        Invalidate();
    }

    private CragPoint? Nearest(Point location, int radius)
    {
        CragPoint? best = null;
        double bestDist = radius * radius;
        foreach (CragPoint p in _points)
        {
            (float x, float y) = Project(p);
            double d = (x - location.X) * (x - location.X) + (y - location.Y) * (y - location.Y);
            if (d < bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>How many crags sit within `radius` px of the point (zoomed-out dense areas).</summary>
    internal int ClusterAt(Point location, int radius)
    {
        int count = 0;
        foreach (CragPoint p in _points)
        {
            (float x, float y) = Project(p);
            double d = (x - location.X) * (x - location.X) + (y - location.Y) * (y - location.Y);
            if (d <= radius * radius) count++;
        }
        return count;
    }
}

/// <summary>
/// LRU cache + background fetcher for 256px raster tiles (CARTO dark_all, which is
/// built on OpenStreetMap data). Failed tiles are retried on the next paint pass.
/// </summary>
internal sealed class TileCache : IDisposable
{
    private const string UrlTemplate = "https://tile.openstreetmap.org/{0}/{1}/{2}.png";
    private const int Cap = 600;

    // OSM tile policy requires an identifying User-Agent.
    private readonly HttpClient _http = new(new WinHttpTransport("RockfaxExplorer/1.0 (github.com/Siroro/rockfax-api)", TimeSpan.FromSeconds(20)))
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
    private readonly Dictionary<(int Z, int X, int Y), Image> _cache = new();
    private readonly Queue<(int Z, int X, int Y)> _order = new();
    private readonly HashSet<(int Z, int X, int Y)> _inFlight = new();
    private readonly CragMapCanvas _owner;

    public TileCache(CragMapCanvas owner) => _owner = owner;

    public Image? Get(int z, int x, int y)
    {
        lock (_cache)
        {
            return _cache.TryGetValue((z, x, y), out Image? tile) ? tile : null;
        }
    }

    public void RequestAsync(int z, int x, int y)
    {
        var key = (z, x, y);
        lock (_cache)
        {
            if (_cache.ContainsKey(key) || !_inFlight.Add(key)) return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                using HttpResponseMessage response = await _http
                    .GetAsync(string.Format(UrlTemplate, z, x, y)).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return;
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                using var ms = new MemoryStream(bytes);
                using Image raw = Image.FromStream(ms);
                var tile = CragMapCanvas.DarkenTile(raw);
                lock (_cache)
                {
                    if (_cache.Count >= Cap && _order.Count > 0)
                    {
                        var evict = _order.Dequeue();
                        if (_cache.Remove(evict, out Image? old)) old.Dispose();
                        _inFlight.Remove(evict);
                    }
                    _cache[key] = tile;
                    _order.Enqueue(key);
                }
                if (_owner.IsHandleCreated) _owner.BeginInvoke(() => _owner.Invalidate());
            }
            catch
            {
                // failed tile: removed from in-flight below so the next paint retries
            }
            finally
            {
                lock (_cache) { _inFlight.Remove(key); }
            }
        });
    }

    public void Dispose() => _http.Dispose();
}
