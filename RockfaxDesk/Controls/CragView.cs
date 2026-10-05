using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>A crag page: grade distribution, weather forecast, full route list, photos.</summary>
internal sealed class CragView : UserControl
{
    private readonly Label _lblTitle = new()
    {
        Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI Semibold", 15f),
        ForeColor = Color.FromArgb(235, 245, 255), Padding = new Padding(8, 4, 0, 0),
    };
    private readonly Label _lblGrades = new()
    {
        Dock = DockStyle.Top, Height = 24, Font = new Font("Consolas", 10f),
        ForeColor = Color.FromArgb(150, 200, 240), Padding = new Padding(10, 0, 0, 0),
    };
    private readonly Label _lblWeather = new()
    {
        Dock = DockStyle.Top, Height = 58, Font = new Font("Consolas", 9.5f),
        ForeColor = Color.FromArgb(190, 235, 190), Padding = new Padding(10, 4, 0, 0),
    };
    private readonly ListView _lvRoutes = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(24, 32, 44), ForeColor = Color.Gainsboro,
    };
    private readonly FlowLayoutPanel _photos = new()
    {
        Dock = DockStyle.Bottom, Height = 128, AutoScroll = true, BackColor = Color.FromArgb(18, 26, 38),
        Padding = new Padding(6), WrapContents = false,
    };
    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Bottom, Height = 18, ForeColor = Color.FromArgb(255, 170, 60),
        Font = new Font("Segoe UI", 8.5f), Padding = new Padding(8, 0, 0, 0),
    };

    private RockfaxClient? _api;
    private ImageFetcher? _images;

    public event Action<RouteSummary>? RouteRequested;

    public CragView()
    {
        BackColor = Color.FromArgb(24, 32, 44);
        _lvRoutes.Columns.Add("Route", 280);
        _lvRoutes.Columns.Add("Grade", 80);
        _lvRoutes.Columns.Add("Tech", 60);
        _lvRoutes.Columns.Add("Stars", 50);
        _lvRoutes.Columns.Add("UKC id", 70);
        _lvRoutes.DoubleClick += async (_, _) =>
        {
            if (_lvRoutes.SelectedItems.Count > 0 && _lvRoutes.SelectedItems[0].Tag is RouteSummary r)
                RouteRequested?.Invoke(r);
        };

        Controls.Add(_lvRoutes);
        Controls.Add(_photos);
        Controls.Add(_lblStatus);
        Controls.Add(_lblWeather);
        Controls.Add(_lblGrades);
        Controls.Add(_lblTitle);
    }

    public void Bind(RockfaxClient api, ImageFetcher images)
    {
        _api = api;
        _images = images;
    }

    public async Task ShowCragAsync(int ukcCragId, string knownTitle)
    {
        if (_api is null || _images is null) return;
        _lblTitle.Text = knownTitle.Length > 0 ? knownTitle : $"Crag {ukcCragId}";
        _lblGrades.Text = _lblWeather.Text = "";
        _lvRoutes.Items.Clear();
        foreach (Control c in _photos.Controls) if (c is PictureBox pb) pb.Image?.Dispose();
        _photos.Controls.Clear();
        _lblStatus.Text = "loading crag…";

        string cragName = knownTitle;

        // Routes + crag metadata (crag_routes_ukc returns the crag object keyed by id).
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
                        _lblGrades.Text = $"● {g[0]} green (D–VD)    ● {g[1]} orange (S–HS)    ● {g[2]} red (VS–HVS)    ● {g[3]} black (E1+)"
                            .Replace("●", "•");
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

        // Weather forecast for the crag.
        try
        {
            using JsonDocument doc = await _api.GetWeatherAsync(new[] { ukcCragId }, RockfaxApi.Site.UkClimbing);
            string forecast = FormatWeather(doc);
            if (forecast.Length > 0) _lblWeather.Text = forecast;
        }
        catch
        {
            _lblWeather.Text = "weather unavailable";
        }

        // Crag photos.
        _lblStatus.Text = "loading photos…";
        int shown = 0;
        try
        {
            using JsonDocument doc = await _api.GetCragPhotosAsync(new[] { ukcCragId }, RockfaxApi.Site.UkClimbing);
            foreach ((int id, string title, string author) in RouteView.ParsePhotos(doc))
            {
                Image? thumb = await _images.GetThumbAsync(id);
                if (thumb is null) continue;
                var box = new PictureBox
                {
                    Image = thumb, Size = new Size(118, 118), SizeMode = PictureBoxSizeMode.Zoom,
                    Tag = id, Padding = new Padding(2), Cursor = Cursors.Hand,
                };
                int photoId = id;
                box.Click += async (_, _) => await PhotoDialog.ShowAsync(_images, photoId, title, author);
                _photos.Controls.Add(box);
                shown++;
            }
        }
        catch
        {
            // photos are decoration; ignore failures
        }
        _lblStatus.Text = _lvRoutes.Items.Count > 0 ? $"{_lvRoutes.Items.Count} routes — double-click for route details" : "no routes listed";
        if (shown > 0) _lblStatus.Text += $"   ·   {shown} photos";
    }

    private static IEnumerable<JsonElement> RoutesOf(JsonElement crag)
    {
        if (crag.TryGetProperty("routes", out JsonElement routes))
        {
            foreach (JsonElement route in routes.EnumerateArrayOrObjectValues()) yield return route;
        }
    }

    internal static string FormatWeather(JsonDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        foreach (JsonElement area in doc.RootElement.EnumerateArrayOrObjectValues())
        {
            if (area.ValueKind != JsonValueKind.Object) continue;
            foreach (JsonProperty day in area.EnumerateObject())
            {
                if (day.Value.ValueKind != JsonValueKind.Object || !day.Value.TryGetProperty("dly", out JsonElement dly)) continue;
                string code = WeatherCode(dly.Int("wc"));
                int temp = dly.Int("t"), rain = dly.Int("cor"), wind = dly.Int("ws");
                sb.Append($"{day.Name[2..]}: {temp}deg {code} rain{rain}% wind{wind}   ");
            }
            break; // one forecast area is enough for a summary line
        }
        return sb.ToString();
    }

    private static string WeatherCode(int wc)
    {
        if (wc == 0) return "clear";
        if (wc == 1 || wc == 2) return "psun";
        if (wc == 3) return "cloud";
        if (wc >= 45 && wc <= 48) return "fog";
        if (wc >= 51 && wc <= 57) return "drizzle";
        if (wc >= 61 && wc <= 67) return "rain";
        if (wc >= 71 && wc <= 77) return "snow";
        if (wc >= 80 && wc <= 82) return "showers";
        if (wc >= 95) return "storm";
        return "w" + wc;
    }
}
