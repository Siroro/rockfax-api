using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>A crag page: grade distribution chips, weather forecast cards, full route list, photos.</summary>
internal sealed class CragView : UserControl
{
    private readonly Label _empty;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ui.Bg };

    private readonly Label _lblTitle = Ui.Label("", Ui.Text, Ui.H1);
    private readonly FlowLayoutPanel _grades = new()
    {
        Dock = DockStyle.Top, Height = 34, BackColor = Ui.Bg, Padding = new Padding(12, 4, 0, 0), WrapContents = false,
    };
    private readonly FlowLayoutPanel _weather = new()
    {
        Dock = DockStyle.Top, Height = 84, BackColor = Ui.Bg, Padding = new Padding(12, 4, 0, 0), WrapContents = false,
    };
    private readonly UiList _lvRoutes = new();
    private readonly FlowLayoutPanel _photos = new()
    {
        Dock = DockStyle.Bottom, Height = 146, AutoScroll = true, BackColor = Ui.BgDeep,
        Padding = new Padding(8), WrapContents = true,
    };
    private readonly Label _lblStatus = Ui.Label("", Ui.Amber, Ui.Small);

    private RockfaxClient? _api;
    private ImageFetcher? _images;

    public event Action<RouteSummary>? RouteRequested;

    public CragView()
    {
        BackColor = Ui.Bg;

        _empty = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Open a crag from the map, from All crags, or via the ▸ link on a route page.",
            ForeColor = Ui.Muted,
            Font = Ui.H2,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Ui.Bg,
        };

        _lvRoutes.Columns.Add("Route", 320);
        _lvRoutes.Columns.Add("Grade", 70);
        _lvRoutes.Columns.Add("Tech", 60);
        _lvRoutes.Columns.Add("Stars", 56);
        _lvRoutes.Columns.Add("UKC id", 76);
        _lvRoutes.ColumnInk[1] = Ui.Amber;
        _lvRoutes.ColumnInk[3] = Ui.Green;
        _lvRoutes.DoubleClick += async (_, _) =>
        {
            if (_lvRoutes.SelectedItems.Count > 0 && _lvRoutes.SelectedItems[0].Tag is RouteSummary r)
                RouteRequested?.Invoke(r);
        };

        var titleRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Ui.Bg, Padding = new Padding(10, 2, 0, 0) };
        titleRow.Controls.Add(_lblTitle);
        var routesHeader = Ui.SectionHeader("ROUTES — double-click for details", 26);
        var weatherHeader = Ui.SectionHeader("WEATHER", 24);

        _content.Controls.Add(_lvRoutes);
        _content.Controls.Add(_photos);
        _content.Controls.Add(routesHeader);
        _content.Controls.Add(weatherHeader);
        _content.Controls.Add(_weather);
        _content.Controls.Add(_grades);
        _content.Controls.Add(titleRow);
        _lblStatus.Dock = DockStyle.Bottom;
        _lblStatus.AutoSize = false;
        _lblStatus.Height = 22;
        _lblStatus.BackColor = Ui.BgDeep;
        _lblStatus.Padding = new Padding(10, 2, 0, 0);
        _content.Controls.Add(_lblStatus);

        Controls.Add(_empty);
        Controls.Add(_content);
        _content.Visible = false;
    }

    public void Bind(RockfaxClient api, ImageFetcher images)
    {
        _api = api;
        _images = images;
    }

    public async Task ShowCragAsync(int ukcCragId, string knownTitle)
    {
        if (_api is null || _images is null) return;
        _empty.Visible = false;
        _content.Visible = true;

        _lblTitle.Text = knownTitle.Length > 0 ? knownTitle : $"Crag {ukcCragId}";
        _grades.Controls.Clear();
        _weather.Controls.Clear();
        _lvRoutes.Items.Clear();
        foreach (Control c in _photos.Controls)
            if (c is Panel { Controls.Count: > 0 } && c.Controls[0] is PictureBox pb)
                pb.Image?.Dispose();
        _photos.Controls.Clear();
        _lblStatus.Text = "loading crag…";

        string cragName = knownTitle;

        try
        {
            using JsonDocument doc = await _api.GetCragRoutesAsync(ukcCragId);
            foreach (JsonProperty cragEntry in doc.RootElement.EnumerateObject())
            {
                JsonElement crag = cragEntry.Value;
                if (crag.ValueKind != JsonValueKind.Object) continue;
                if (cragName.Length == 0) cragName = crag.Str("name");
                _lblTitle.Text = cragName.Length > 0 ? cragName : $"Crag {ukcCragId}";

                if (crag.TryGetProperty("grades", out JsonElement grades) && grades.ValueKind == JsonValueKind.Array)
                {
                    int[] g = grades.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                    if (g.Length >= 4)
                    {
                        _grades.Controls.Add(Ui.Chip($"{g[0]}  Mod–VD", Ui.Green));
                        _grades.Controls.Add(Ui.Chip($"{g[1]}  S–HS", Ui.Amber));
                        _grades.Controls.Add(Ui.Chip($"{g[2]}  VS–HVS", Color.FromArgb(251, 146, 60)));
                        _grades.Controls.Add(Ui.Chip($"{g[3]}  E1+", Ui.Red));
                    }
                }

                foreach (JsonElement route in RoutesOf(crag))
                {
                    var summary = new RouteSummary(
                        route.Str("name"), route.Str("grade"), route.Str("techGrade"), route.Int("stars"),
                        cragName, route.Int("ukcID"), route.Int("rockfaxID"), int.TryParse(cragEntry.Name, out int id) ? id : ukcCragId);
                    if (summary.UkcId == 0 && summary.RockfaxId == 0) continue;
                    var item = new ListViewItem(summary.Name);
                    item.SubItems.Add(summary.Grade);
                    item.SubItems.Add(summary.TechGrade);
                    item.SubItems.Add(Jx.Stars(summary.Stars));
                    item.SubItems.Add(summary.UkcId.ToString());
                    item.Tag = summary;
                    _lvRoutes.Items.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "routes unavailable: " + ex.Message;
        }

        try
        {
            using JsonDocument doc = await _api.GetWeatherAsync(new[] { ukcCragId }, RockfaxApi.Site.UkClimbing);
            foreach (WeatherChip chip in FormatWeather(doc))
                _weather.Controls.Add(chip);
            if (_weather.Controls.Count == 0) _weather.Controls.Add(Ui.Label("no forecast data", Ui.Muted, Ui.Small));
        }
        catch
        {
            _weather.Controls.Add(Ui.Label("weather unavailable", Ui.Muted, Ui.Small));
        }

        _lblStatus.Text = "loading photos…";
        int shown = 0;
        try
        {
            using JsonDocument doc = await _api.GetCragPhotosAsync(new[] { ukcCragId }, RockfaxApi.Site.UkClimbing);
            foreach ((int id, string title, string author) in RouteView.ParsePhotos(doc))
            {
                Image? thumb = await _images.GetThumbAsync(id);
                if (thumb is null) continue;
                _photos.Controls.Add(RouteView.PhotoCard(_images, id, title, author, thumb));
                shown++;
            }
        }
        catch
        {
            // photos are decoration; ignore failures
        }
        _lblStatus.Text = _lvRoutes.Items.Count > 0
            ? $"{_lvRoutes.Items.Count} routes — double-click for route details" + (shown > 0 ? $"  ·  {shown} photos" : "")
            : "no routes listed";
    }

    private static IEnumerable<JsonElement> RoutesOf(JsonElement crag)
    {
        if (crag.TryGetProperty("routes", out JsonElement routes))
        {
            foreach (JsonElement route in routes.EnumerateArrayOrObjectValues()) yield return route;
        }
    }

    internal static List<WeatherChip> FormatWeather(JsonDocument doc)
    {
        var chips = new List<WeatherChip>();
        foreach (JsonElement area in doc.RootElement.EnumerateArrayOrObjectValues())
        {
            if (area.ValueKind != JsonValueKind.Object) continue;
            foreach (JsonProperty day in area.EnumerateObject())
            {
                if (day.Value.ValueKind != JsonValueKind.Object || !day.Value.TryGetProperty("dly", out JsonElement dly)) continue;
                chips.Add(new WeatherChip
                {
                    Day = day.Name.Length > 2 ? day.Name[2..] : day.Name,
                    Temp = dly.Int("t"),
                    RainPct = dly.Int("cor"),
                    Wind = dly.Int("ws"),
                    Code = dly.Int("wc"),
                });
            }
            break; // one forecast area is enough for the strip
        }
        return chips;
    }
}

/// <summary>One painted weather-day card.</summary>
internal sealed class WeatherChip : Control
{
    public string Day = "";
    public int Temp, RainPct, Wind, Code;

    public WeatherChip()
    {
        Size = new Size(88, 72);
        BackColor = Ui.Card;
        Margin = new Padding(0, 0, 6, 0);
        DoubleBuffered = true;
    }

    private static string CodeText(int wc)
    {
        if (wc == 0) return "clear";
        if (wc <= 2) return "p.sun";
        if (wc == 3) return "cloud";
        if (wc <= 48) return "fog";
        if (wc <= 57) return "drizzle";
        if (wc <= 67) return "rain";
        if (wc <= 77) return "snow";
        if (wc <= 82) return "showers";
        if (wc >= 95) return "storm";
        return "w" + wc;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Ui.Card);
        using var border = new Pen(Ui.Border);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        TextRenderer.DrawText(g, Day, Ui.BodyBold, new Point(10, 7), Ui.Accent);
        Color tempInk = Temp >= 18 ? Ui.Amber : Temp <= 4 ? Ui.Accent : Ui.Text;
        TextRenderer.DrawText(g, $"{Temp}°", Ui.MonoBig, new Point(8, 22), tempInk);
        TextRenderer.DrawText(g, CodeText(Code), Ui.Tiny, new Point(10, 47), Ui.Muted);
        TextRenderer.DrawText(g, $"rain {RainPct}%  wind {Wind}", Ui.Tiny, new Point(10, 58), RainPct >= 60 ? Ui.Accent : Ui.Muted);
    }
}
