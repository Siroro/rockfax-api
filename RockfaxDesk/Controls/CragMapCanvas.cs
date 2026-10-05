using System.Drawing;

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
/// the shape of Britain. Drag to pan, wheel to zoom, click a dot to open its crag.
/// </summary>
internal sealed class CragMapCanvas : Control
{
    private IReadOnlyList<CragPoint> _points = Array.Empty<CragPoint>();
    private float _minLat = 49f, _maxLat = 61f, _minLng = -9f, _maxLng = 2f;
    private float _zoom = 1f;
    private PointF _pan = new(60f, 20f); // account for left/top crag-free margins
    private bool _dragging;
    private Point _dragMouseStart;
    private PointF _panStart;
    private CragPoint? _hover;

    public event Action<CragPoint>? CragSelected;

    public CragMapCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    public IReadOnlyList<CragPoint> Points
    {
        get => _points;
        set
        {
            _points = value;
            if (_points.Count > 0)
            {
                _minLat = _points.Min(p => p.Lat); _maxLat = _points.Max(p => p.Lat);
                _minLng = _points.Min(p => p.Lng); _maxLng = _points.Max(p => p.Lng);
            }
            _zoom = 1f;
            _pan = new PointF(60f, 20f);
            Invalidate();
        }
    }

    public string Subtitle => _points.Count switch
    {
        0 => "",
        _ => $"{_points.Count:N0} crags   zoom ×{_zoom:0.0}   — drag to pan, wheel to zoom, click a dot",
    };

    private (float X, float Y) Project(CragPoint p)
    {
        // Equirectangular with a mid-latitude squeeze on longitude so proportions look right.
        const double latScale = 111f; // km per degree
        double lngScale = 111f * Math.Cos(54 * Math.PI / 180);
        double spanX = (_maxLng - _minLng) * lngScale;
        double spanY = (_maxLat - _minLat) * latScale;
        double fit = Math.Min(Width / Math.Max(spanX, 1), Height / Math.Max(spanY, 1));
        float x = (float)(((p.Lng - _minLng) * lngScale) * fit * _zoom) + _pan.X;
        float y = (float)((_maxLat - p.Lat) * latScale * fit * _zoom) + _pan.Y;
        return (x, y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Color.FromArgb(18, 26, 38));
        if (_points.Count == 0)
        {
            using var hint = new Font("Segoe UI", 10f);
            g.DrawString("loading crags…", hint, Brushes.DimGray, 12, 12);
            return;
        }

        bool small = _zoom < 1.6;
        using var normal = new SolidBrush(Color.FromArgb(120, 190, 240));
        using var free = new SolidBrush(Color.FromArgb(255, 170, 60));
        using var big = new SolidBrush(Color.FromArgb(180, 225, 255));
        var clip = e.ClipRectangle;

        foreach (CragPoint p in _points)
        {
            (float x, float y) = Project(p);
            if (x < clip.Left - 6 || x > clip.Right + 6 || y < clip.Top - 6 || y > clip.Bottom + 6) continue;
            Brush brush = p.Free ? free : small ? normal : big;
            if (small) g.FillRectangle(brush, x, y, 2, 2);
            else
            {
                float s = p.NRoutes > 100 ? 5 : p.NRoutes > 20 ? 4 : 3;
                g.FillEllipse(brush, x - s / 2, y - s / 2, s, s);
            }
        }

        if (_hover is not null)
        {
            (float hx, float hy) = Project(_hover);
            using var pen = new Pen(Color.White, 1.5f);
            g.DrawEllipse(pen, hx - 6, hy - 6, 12, 12);
            string label = $"{_hover.Title}  ({_hover.NRoutes} routes)";
            using var font = new Font("Segoe UI Semibold", 9f);
            SizeF size = g.MeasureString(label, font);
            g.FillRectangle(new SolidBrush(Color.FromArgb(40, 40, 40)), hx + 10, hy - 8, size.Width + 8, size.Height + 4);
            g.DrawString(label, font, Brushes.White, hx + 14, hy - 6);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
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
            CragPoint? nearest = Nearest(e.Location, 10);
            if (!ReferenceEquals(nearest, _hover))
            {
                _hover = nearest;
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
        if (!wasDrag && e.Button == MouseButtons.Left && Nearest(e.Location, 10) is { } crag)
            CragSelected?.Invoke(crag);
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        (float bx, float by) = ScreenToMap(e.Location);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.25f : 0.8f), 0.4f, 220f);
        (float ax, float ay) = ScreenToMap(e.Location);
        _pan = new PointF(_pan.X + (ax - bx), _pan.Y + (ay - by)); // keep the point under the cursor fixed
        Invalidate();
        base.OnMouseWheel(e);
    }

    private (float, float) ScreenToMap(Point screen)
    {
        // Map-space position of a screen point (the pan terms removed, zoom-dependent scale fixed).
        const double latScale = 111f; // km per degree
        double lngScale = 111f * Math.Cos(54 * Math.PI / 180);
        double spanX = (_maxLng - _minLng) * lngScale, spanY = (_maxLat - _minLat) * latScale;
        double fit = Math.Min(Width / Math.Max(spanX, 1), Height / Math.Max(spanY, 1));
        return ((float)((screen.X - _pan.X) / (fit * _zoom)), (float)((screen.Y - _pan.Y) / (fit * _zoom)));
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
