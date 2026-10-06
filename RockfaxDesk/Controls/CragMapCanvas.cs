using System.Drawing;
using System.Drawing.Drawing2D;

namespace RockfaxDesk.Controls;

internal sealed class CragPoint
{
    public float Lat, Lng;
    public int UkcId, RockfaxId, NRoutes;
    public string Title = "";
    public bool Free;
}

/// <summary>
/// Pannable, zoomable scatter plot of every UKC crag marker — the dots themselves draw
/// the shape of Britain. Drag to pan, wheel or on-canvas buttons to zoom, hover for a
/// label, click a dot to open its crag. Dots glow; size follows route count; free crags
/// burn amber.
/// </summary>
internal sealed class CragMapCanvas : Control
{
    private IReadOnlyList<CragPoint> _points = Array.Empty<CragPoint>();

    // Fixed viewport over Britain + Ireland. The marker feed contains overseas crags and
    // outright bad rows (lat/lng 1000, swapped coordinates) — auto-fitting to the data
    // collapses the map, so the frame is pinned and out-of-frame points are culled.
    private const float MinLat = 49.6f, MaxLat = 61.2f, MinLng = -11.2f, MaxLng = 2.6f;
    private float _zoom = 1f;
    private PointF _pan = new(70f, 26f);
    private bool _dragging;
    private Point _dragMouseStart;
    private PointF _panStart;
    private CragPoint? _hover;
    private CragPoint? _selected;
    private int _clusterCount;
    private Point _hoverPoint;

    private const double LatKm = 111.0;          // km per degree of latitude
    private const double LngKm = 111.0 * 0.5878; // cos(54°) — mid-Britain squeeze

    public event Action<CragPoint>? CragSelected;

    public CragMapCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Ui.BgDeep;
        Cursor = Cursors.Hand;
    }

    public IReadOnlyList<CragPoint> Points
    {
        get => _points;
        set
        {
            _points = value;
            ResetView();
        }
    }

    public string Subtitle => _points.Count == 0 ? "" : $"{_points.Count:N0} crags · zoom ×{_zoom:0.0}";

    /// <summary>Saves/restores the viewport across sessions (zoom + pan).</summary>
    public (float Zoom, float PanX, float PanY) GetView() => (_zoom, _pan.X, _pan.Y);

    public void ApplyView(float zoom, float panX, float panY)
    {
        _zoom = Math.Clamp(zoom, 0.4f, 220f);
        _pan = new PointF(panX, panY);
        Invalidate();
    }

    /// <summary>Centers the view on a coordinate at a given zoom (crag page "map" button).</summary>
    public void CenterOn(float lat, float lng, float zoom)
    {
        _zoom = Math.Clamp(zoom, 0.4f, 220f);
        (float x, float y) = Project(new CragPoint { Lat = lat, Lng = lng });
        _pan = new PointF(_pan.X + (Width / 2f - x), _pan.Y + (Height / 2f - y));
        Invalidate();
    }

    /// <summary>Rings the dot for a crag opened elsewhere (list, route page).</summary>
    public void SelectCrag(int ukcId)
    {
        _selected = _points.FirstOrDefault(p => p.UkcId == ukcId);
        if (_selected is not null) Invalidate();
    }

    public void ResetView()
    {
        _zoom = 1f;
        _pan = new PointF(70f, 26f);
        _hover = null;
        // keep _selected: repaints (free-crags overlay) must not lose the ring
        Invalidate();
    }

    private float Fit => (float)Math.Min(Width / Math.Max((MaxLng - MinLng) * LngKm, 1),
                                         Height / Math.Max((MaxLat - MinLat) * LatKm, 1));

    private (float X, float Y) Project(CragPoint p)
    {
        double scale = Fit * _zoom;
        float x = (float)((p.Lng - MinLng) * LngKm * scale) + _pan.X;
        float y = (float)((MaxLat - p.Lat) * LatKm * scale) + _pan.Y;
        return (x, y);
    }

    private (float, float) ScreenToMap(Point screen)
    {
        double scale = Fit * _zoom;
        return ((float)((screen.X - _pan.X) / scale), (float)((screen.Y - _pan.Y) / scale));
    }

    // ---- painting -----------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.BgDeep);

        if (_points.Count == 0)
        {
            TextRenderer.DrawText(g, "loading crags…", Ui.H2, ClientRectangle, Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        DrawGraticule(g);
        DrawDots(g, e.ClipRectangle);
        DrawHoverAndSelection(g);
        DrawScaleBar(g);
        DrawLegend(g);
        DrawZoomControls(g);
    }

    private void DrawGraticule(Graphics g)
    {
        using var gridPen = new Pen(Color.FromArgb(30, 42, 64), 1f);
        for (int lat = (int)Math.Ceiling(MinLat); lat <= MaxLat; lat++)
        {
            (_, float y) = Project(new CragPoint { Lat = lat, Lng = MinLng });
            g.DrawLine(gridPen, 0, y, Width, y);
            TextRenderer.DrawText(g, $"{lat}°N", Ui.Tiny, new Point(6, (int)y + 2), Color.FromArgb(95, 112, 140));
        }
        for (int lng = (int)Math.Ceiling(MinLng); lng <= MaxLng; lng++)
        {
            (float x, _) = Project(new CragPoint { Lat = MaxLat, Lng = lng });
            g.DrawLine(gridPen, x, 0, x, Height);
            TextRenderer.DrawText(g, $"{(lng < 0 ? $"{-lng}°W" : $"{lng}°E")}", Ui.Tiny,
                new Point((int)x + 4, Height - 16), Color.FromArgb(95, 112, 140));
        }
    }

    private void DrawDots(Graphics g, Rectangle clip)
    {
        bool small = _zoom < 1.6f;
        foreach (CragPoint p in _points)
        {
            (float x, float y) = Project(p);
            if (x < clip.Left - 12 || x > clip.Right + 12 || y < clip.Top - 12 || y > clip.Bottom + 12) continue;

            Color core = p.Free ? Ui.Amber : p.NRoutes > 100 ? Color.FromArgb(215, 240, 255) : Color.FromArgb(96, 175, 235);
            float r = small ? 1.6f : p.NRoutes > 100 ? 4.5f : p.NRoutes > 20 ? 3.5f : 2.5f;

            using (var glow = new SolidBrush(Color.FromArgb(p.Free ? 48 : 26, core)))
                g.FillEllipse(glow, x - r * 2.4f, y - r * 2.4f, r * 4.8f, r * 4.8f);
            using var coreBrush = new SolidBrush(core);
            if (small) g.FillRectangle(coreBrush, x - 1, y - 1, 2, 2);
            else g.FillEllipse(coreBrush, x - r / 2, y - r / 2, r, r);
        }
    }

    private void DrawHoverAndSelection(Graphics g)
    {
        // Zoomed-out dense area: no single dot under the cursor but several nearby —
        // show a cluster count instead of one name.
        if (_hover is null && _clusterCount > 1)
        {
            string label = $"{_clusterCount} crags in this area — zoom in";
            SizeF size = TextRenderer.MeasureText(label, Ui.BodyBold);
            var box = new Rectangle(_hoverPoint.X + 12, _hoverPoint.Y - 11, (int)size.Width + 12, (int)size.Height + 6);
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
            g.DrawEllipse(ring, x - 7, y - 7, 14, 14);

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

    /// <summary>Draws a km scale bar, bottom-right above the zoom controls.</summary>
    private void DrawScaleBar(Graphics g)
    {
        double kmPerPx = 1.0 / (Fit * _zoom);
        double maxKm = 100 * kmPerPx;
        double nice = maxKm switch
        {
            >= 500 => 500, >= 250 => 250, >= 100 => 100, >= 50 => 50, >= 25 => 25,
            >= 10 => 10, >= 5 => 5, >= 2 => 2, _ => 1,
        };
        int px = (int)(nice / kmPerPx);
        int x = Width - px - 24, y = Height - 34;
        using var pen = new Pen(Color.FromArgb(200, 133, 152, 180), 2f);
        g.DrawLine(pen, x, y, x + px, y);
        g.DrawLine(pen, x, y - 4, x, y + 4);
        g.DrawLine(pen, x + px, y - 4, x + px, y + 4);
        TextRenderer.DrawText(g, $"{nice:0} km", Ui.Small, new Point(x + px / 2 - 24, y - 22), Ui.Muted);
    }

    private Rectangle ZoomInRect => new(Width - 112, 12, 32, 28);
    private Rectangle ZoomOutRect => new(Width - 76, 12, 32, 28);
    private Rectangle ZoomResetRect => new(Width - 40, 12, 28, 28);

    private void DrawZoomControls(Graphics g)
    {
        DrawZoomButton(g, ZoomInRect, "+", _zoom < 220f);
        DrawZoomButton(g, ZoomOutRect, "−", _zoom > 0.4f);
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
            if (ZoomInRect.Contains(e.Location)) ZoomBy(1.5f, e.Location);
            else if (ZoomOutRect.Contains(e.Location)) ZoomBy(1 / 1.5f, e.Location);
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
        // Convention: double-click zooms in around the cursor.
        if (e is MouseEventArgs me && !InZoomArea(me.Location)) ZoomBy(1.5f, me.Location);
        base.OnDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ZoomBy(e.Delta > 0 ? 1.25f : 1 / 1.25f, e.Location);
        base.OnMouseWheel(e);
    }

    private void ZoomBy(float factor, Point anchor)
    {
        (float bx, float by) = ScreenToMap(anchor);
        _zoom = Math.Clamp(_zoom * factor, 0.4f, 220f);
        (float ax, float ay) = ScreenToMap(anchor);
        _pan = new PointF(_pan.X + (ax - bx), _pan.Y + (ay - by)); // keep the anchored point fixed
        Invalidate();
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
}
