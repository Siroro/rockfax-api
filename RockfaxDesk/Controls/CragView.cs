using System.Drawing;
using System.Globalization;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>A crag page: grade distribution chips, weather forecast cards, full route list, photos.</summary>
internal sealed class CragView : UserControl
{
    private readonly Label _empty;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ui.Bg };

    private readonly Label _lblTitle = Ui.Label("", Ui.Text, Ui.H1);
    private readonly FlowLayoutPanel _metaChips = new()
    {
        Dock = DockStyle.Right, BackColor = Ui.Bg, WrapContents = false, Padding = new Padding(0, 8, 10, 0),
    };
    private readonly FlowLayoutPanel _grades = new()
    {
        Dock = DockStyle.Top, Height = 34, BackColor = Ui.Bg, Padding = new Padding(12, 4, 0, 0), WrapContents = false,
    };
    private readonly FlowLayoutPanel _weather = new()
    {
        Dock = DockStyle.Top, Height = 84, BackColor = Ui.Bg, Padding = new Padding(12, 4, 0, 0), WrapContents = false,
    };
    private readonly UiList _lvRoutes = new();
    private readonly List<(RouteSummary Summary, string Buttress, int Band)> _allRoutes = new();
    private readonly List<int> _groupFirstItems = new();
    private ComboBox? _buttressJump;
    private int _bandFilter = -1; // -1 = all
    private readonly FlowLayoutPanel _photos = RouteView.NewPhotoStrip();
    private readonly Label _lblStatus = Ui.Label("", Ui.Amber, Ui.Small);

    private RockfaxClient? _api;
    private ImageFetcher? _images;

    public event Action<RouteSummary>? RouteRequested;
    public event Action<int>? MapRequested;

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
        _lvRoutes.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem? hit = _lvRoutes.HitTest(e.Location).Item;
            if (hit?.Tag is not RouteSummary route) return;
            hit.Selected = true;
            string url = $"https://www.ukclimbing.com/logbook/route.php?id={route.UkcId}";
            Ui.Menu(
                ("Open route", () => RouteRequested?.Invoke(route)),
                ("Open on UKC ↗", () => Jx.OpenBrowser(url)),
                ("Copy UKC link", () =>
                {
                    try { Clipboard.SetText(url); _lblStatus.Text = "link copied to the clipboard"; }
                    catch { _lblStatus.Text = "clipboard is busy — try again"; }
                })
            ).Show(_lvRoutes, e.Location);
        };

        var titleRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Ui.Bg, Padding = new Padding(10, 2, 0, 0) };
        titleRow.Controls.Add(_lblTitle);
        titleRow.Controls.Add(_metaChips);
        _buttressJump = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Ui.BgDeep,
            ForeColor = Ui.Text,
            Font = Ui.Small,
            Width = 200,
            Visible = false,
        };
        _buttressJump.SelectedIndexChanged += (_, _) =>
        {
            if (_buttressJump.SelectedIndex is int idx && idx > 0 && idx - 1 < _groupFirstItems.Count)
            {
                _lvRoutes.EnsureVisible(_groupFirstItems[idx - 1]);
            }
        };
        var routesHeader = Ui.SectionHeader("ROUTES — double-click for details", 26);
        var weatherHeader = Ui.SectionHeader("WEATHER", 24);

        _content.Controls.Add(_lvRoutes);
        _content.Controls.Add(_photos);
        _content.Controls.Add(_buttressJump);
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
        _allRoutes.Clear();
        _bandFilter = -1;
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
            JsonElement root = doc.RootElement;

            // Response shape: { "routes": [ {name, grade, ukcID, buttress, ...} ],
            //                   "crags":  { "<id>": { name, gradeColors: [4], areaName } } }
            if (root.TryGetProperty("crags", out JsonElement crags))
            {
                foreach (JsonProperty cragEntry in crags.EnumerateObject())
                {
                    JsonElement crag = cragEntry.Value;
                    if (crag.ValueKind != JsonValueKind.Object) continue;
                    if (cragName.Length == 0) cragName = crag.Str("name");
                    string area = crag.Str("areaName");
                    if (area.Length > 0 && _metaChips.Controls.Count == 1)
                        _metaChips.Controls.Add(Ui.Chip(area, Ui.Accent));

                    if (crag.TryGetProperty("gradeColors", out JsonElement grades) && grades.ValueKind == JsonValueKind.Array)
                    {
                        int[] g = grades.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                        if (g.Length >= 4)
                        {
                            _gradeCounts = g;
                            BuildGradeChips();
                        }
                    }
                }
            }
            _lblTitle.Text = cragName.Length > 0 ? cragName : $"Crag {ukcCragId}";
            _metaChips.Controls.Clear();
            var mapBtn = Ui.Button("show on map", 104);
            mapBtn.Click += (_, _) => MapRequested?.Invoke(ukcCragId);
            var ukcLink = Ui.Button("on UKC \u2197", 92);
            ukcLink.Click += (_, _) => Jx.OpenBrowser($"https://www.ukclimbing.com/logbook/crag.php?id={ukcCragId}");
            _metaChips.Controls.Add(mapBtn);
            _metaChips.Controls.Add(ukcLink);
            _metaChips.Controls.Add(Ui.Chip($"UKC #{ukcCragId}", Ui.Muted));

            _allRoutes.Clear();
            if (root.TryGetProperty("routes", out JsonElement routes) && routes.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement route in routes.EnumerateArray())
                {
                    var summary = new RouteSummary(
                        route.Str("name"), route.Str("grade"), route.Str("techGrade"), route.Int("stars"),
                        cragName, route.Int("ukcID"), route.Int("rockfaxID"), ukcCragId);
                    if (summary.UkcId == 0 && summary.RockfaxId == 0) continue;
                    _allRoutes.Add((summary, route.Str("buttress"), GradeBand(summary.Grade)));
                }
            }
            ApplyRouteFilter();
            _lvRoutes.StretchLastColumn();
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

    private int[] _gradeCounts = Array.Empty<int>();

    internal static int GradeBand(string grade)
    {
        if (grade.StartsWith("M") || grade.StartsWith("D") || grade.StartsWith("VD")
            || grade.StartsWith("V Diff") || grade.StartsWith("HVD")) return 0;
        if (grade.StartsWith("S") || grade.StartsWith("HS")) return 1;
        if (grade.StartsWith("VS") || grade.StartsWith("HVS")) return 2;
        if (grade.StartsWith("E")) return 3;
        return -1; // sport, boulder, winter, unknown
    }

    private void BuildGradeChips()
    {
        _grades.Controls.Clear();
        string[] labels = { "Mod-VD", "S-HS", "VS-HVS", "E1+" };
        Color[] inks = { Ui.Green, Ui.Amber, Color.FromArgb(251, 146, 60), Ui.Red };
        for (int band = 0; band < 4; band++)
        {
            if (_gradeCounts.Length > band && _gradeCounts[band] == 0) continue; // no noise chips for empty bands
            Color ink = inks[band];
            var chip = Ui.Chip($"{_gradeCounts[band]}  {labels[band]}",
                               _bandFilter == band ? Ui.Bg : ink,
                               _bandFilter == band ? ink : Ui.Card);
            int captured = band;
            chip.Cursor = Cursors.Hand;
            chip.Click += (_, _) =>
            {
                _bandFilter = _bandFilter == captured ? -1 : captured;
                BuildGradeChips();
                ApplyRouteFilter();
            };
            _grades.Controls.Add(chip);
        }
    }

    private void ApplyRouteFilter()
    {
        _lvRoutes.BeginUpdate();
        _lvRoutes.Groups.Clear();
        _lvRoutes.Items.Clear();
        var groups = new Dictionary<string, ListViewGroup>();
        foreach ((RouteSummary summary, string buttress, int band) in _allRoutes)
        {
            if (_bandFilter >= 0 && band != _bandFilter) continue;
            string b = buttress.Length > 0 ? buttress : "General";
            if (!groups.TryGetValue(b, out ListViewGroup? group))
            {
                group = new ListViewGroup(b);
                groups[b] = group;
                _lvRoutes.Groups.Add(group);
            }
            var item = new ListViewItem(summary.Name, group);
            item.SubItems.Add(summary.Grade);
            item.SubItems.Add(summary.TechGrade);
            item.SubItems.Add(Jx.Stars(summary.Stars));
            item.SubItems.Add(summary.UkcId.ToString());
            item.Tag = summary;
            _lvRoutes.Items.Add(item);
        }
        _lvRoutes.ShowGroups = true;
        _lvRoutes.EndUpdate();
        if (_bandFilter >= 0 && _allRoutes.Count > 0)
        {
            string[] labels = { "Mod-VD", "S-HS", "VS-HVS", "E1+" };
            _lblStatus.Text = $"{_lvRoutes.Items.Count} of {_allRoutes.Count} routes ({labels[_bandFilter]} band) — double-click for details";
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
                string raw = day.Name.Length > 2 ? day.Name[2..] : day.Name; // e.g. "Sa26-10-03" -> "26-10-03"
                chips.Add(new WeatherChip
                {
                    Day = FormatDay(raw),
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

    /// <summary>"26-10-03" -> "Sat 3 Oct" (or "Today"); falls back to the raw string.</summary>
    private static string FormatDay(string raw)
        => DateTime.TryParseExact(raw, "yy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt)
            ? dt.Date == DateTime.Today ? "Today" : dt.ToString("ddd d MMM", CultureInfo.InvariantCulture)
            : raw;
}

/// <summary>One painted weather-day card.</summary>
internal sealed class WeatherChip : Control
{
    public string Day = "";
    public int Temp, RainPct, Wind, Code;

    public WeatherChip()
    {
        Size = new Size(100, 72);
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
        TextRenderer.DrawText(g, $"{RainPct}% rain", Ui.Tiny, new Point(10, 47),
            RainPct >= 60 ? Ui.Accent : Ui.Muted, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, $"{CodeText(Code)} · wind {Wind}", Ui.Tiny, new Point(10, 58),
            Ui.Muted, TextFormatFlags.EndEllipsis);
    }
}
