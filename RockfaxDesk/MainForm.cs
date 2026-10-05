using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk;

/// <summary>
/// Basic desktop front-end for the Rockfax API client: route search, route details,
/// free crags, crag markers, and (with your UKClimbing login) your logbook.
/// UI is built in code — no designer file.
/// </summary>
public sealed class MainForm : Form
{
    private readonly RockfaxClient _api = new();

    private readonly TextBox _txtEmail = new() { Left = 50, Top = 12, Width = 190, PlaceholderText = "you@example.com" };
    private readonly TextBox _txtPassword = new() { Left = 280, Top = 12, Width = 140, UseSystemPasswordChar = true, PlaceholderText = "password" };
    private readonly Button _btnLogin = new() { Text = "Login", Left = 432, Top = 10, Width = 70 };
    private readonly Label _lblUser = new() { Text = "not logged in", Left = 520, Top = 15, AutoSize = true };

    private readonly TextBox _txtSearch = new() { Left = 10, Top = 48, Width = 300, PlaceholderText = "route name, e.g. Flying Buttress" };
    private readonly Button _btnSearch = new() { Text = "Search routes", Left = 318, Top = 46, Width = 100 };
    private readonly Button _btnFreeCrags = new() { Text = "Free crags", Left = 426, Top = 46, Width = 90 };
    private readonly Button _btnMarkers = new() { Text = "All crags", Left = 522, Top = 46, Width = 80 };
    private readonly Button _btnLogbook = new() { Text = "My logbook", Left = 608, Top = 46, Width = 95, Enabled = false };

    private readonly ListView _lvResults = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
    };
    private readonly TextBox _txtDetails = new()
    {
        Dock = DockStyle.Bottom,
        Multiline = true,
        ReadOnly = true,
        Height = 170,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9F),
    };
    private readonly Label _lblStatus = new() { Text = "ready", Dock = DockStyle.Bottom, Height = 20, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(4, 2, 0, 0) };

    public MainForm()
    {
        Text = "Rockfax API Explorer (unofficial)";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 940;
        Height = 660;

        _lvResults.Columns.Add("Name", 240);
        _lvResults.Columns.Add("Grade", 70);
        _lvResults.Columns.Add("Stars", 50);
        _lvResults.Columns.Add("Crag", 220);
        _lvResults.Columns.Add("Id", 70);

        var top = new Panel { Dock = DockStyle.Top, Height = 78 };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Email:", Left = 10, Top = 15, AutoSize = true },
            _txtEmail,
            new Label { Text = "Password:", Left = 220, Top = 15, AutoSize = true },
            _txtPassword,
            _btnLogin,
            _lblUser,
            _txtSearch,
            _btnSearch,
            _btnFreeCrags,
            _btnMarkers,
            _btnLogbook,
        });

        Controls.Add(_lvResults);
        Controls.Add(_txtDetails);
        Controls.Add(_lblStatus);
        Controls.Add(top);

        _btnLogin.Click += async (_, _) => await LoginAsync(_txtEmail.Text, _txtPassword.Text);
        _btnSearch.Click += async (_, _) => await SearchUiAsync(_txtSearch.Text);
        _btnFreeCrags.Click += async (_, _) => await LoadFreeCragsUiAsync();
        _btnMarkers.Click += async (_, _) => await LoadMarkersUiAsync();
        _btnLogbook.Click += async (_, _) => await LoadLogbookUiAsync();
        _lvResults.SelectedIndexChanged += async (_, _) => await ShowSelectedRouteAsync();
        _txtSearch.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SearchUiAsync(_txtSearch.Text);
            }
        };
    }

    private void Status(string text) => _lblStatus.Text = text;

    private async Task RunAsync(string what, Func<Task> work)
    {
        try
        {
            Status($"{what}...");
            await work();
            Status($"{what}: done");
        }
        catch (Exception ex)
        {
            Status($"{what}: FAILED — {ex.Message}");
            _txtDetails.Text = ex is RockfaxApiException rae && rae.ResponseBody is not null
                ? rae.ResponseBody
                : ex.ToString();
        }
    }

    // ---- actions ---------------------------------------------------------

    private Task LoginAsync(string email, string password)
        => RunAsync("login", async () =>
        {
            var session = await _api.LoginAsync(email, password);
            _lblUser.Text = $"logged in as {session.Username} (id {session.UserId})";
            _btnLogbook.Enabled = true;
        });

    private Task SearchUiAsync(string query)
        => RunAsync($"search '{query}'", async () =>
        {
            if (query.Trim().Length < 3)
            {
                Status("search: type at least 3 characters");
                return;
            }
            _lvResults.Items.Clear();
            using JsonDocument doc = await _api.SearchRoutesAsync(query);
            foreach (JsonElement route in doc.RootElement.GetProperty("routes").EnumerateArray())
            {
                var item = new ListViewItem(route.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "");
                item.SubItems.Add(GetString(route, "grade"));
                item.SubItems.Add(GetString(route, "stars"));
                item.SubItems.Add(GetString(route, "ukcCragName"));
                item.SubItems.Add(GetString(route, "ukcID"));
                item.Tag = route.TryGetProperty("ukcID", out JsonElement id) && id.ValueKind == JsonValueKind.Number
                    ? id.GetInt32()
                    : null;
                _lvResults.Items.Add(item);
            }
        });

    private Task ShowSelectedRouteAsync()
        => RunAsync("route info", async () =>
        {
            if (_lvResults.SelectedItems.Count == 0 || _lvResults.SelectedItems[0].Tag is not int routeId) return;
            RouteInfo info = await _api.GetRouteInfoAsync(routeId);
            ListViewItem selected = _lvResults.SelectedItems[0];
            _txtDetails.Text =
                $"""
                 {selected.Text}  ({selected.SubItems[1].Text})  —  {selected.SubItems[3].Text}
                 First ascent: {info.FirstAscent} {info.FirstAscentDate}
                 Height: {info.Height} m    Pitches: {info.Pitches}

                 {info.Description}

                 {info.RockfaxDescription}
                 """;
        });

    private Task LoadFreeCragsUiAsync()
        => RunAsync("free crags", async () =>
        {
            _lvResults.Items.Clear();
            Dictionary<int, string> names = await LoadCragNamesAsync();
            using JsonDocument doc = await _api.GetFreeCragsAsync();
            foreach (JsonElement id in doc.RootElement.GetProperty("free_crags").EnumerateArray())
            {
                int cragId = id.GetInt32();
                if (cragId == 0) continue; // 0 is a sentinel value in the app, not a real crag
                var item = new ListViewItem(names.GetValueOrDefault(cragId, "(unnamed)"));
                item.SubItems.Add("");
                item.SubItems.Add("");
                item.SubItems.Add("free sample");
                item.SubItems.Add(cragId.ToString());
                _lvResults.Items.Add(item);
            }
        });

    private Task LoadMarkersUiAsync()
        => RunAsync("all crags", async () =>
        {
            _lvResults.Items.Clear();
            using JsonDocument doc = await _api.GetCragMarkersAsync();
            foreach (JsonElement marker in doc.RootElement.GetProperty("markers").EnumerateArray())
            {
                var item = new ListViewItem(GetString(marker, "title"));
                item.SubItems.Add("");
                item.SubItems.Add(GetString(marker, "nroutes"));
                item.SubItems.Add($"{GetString(marker, "lat")}, {GetString(marker, "lng")}");
                item.SubItems.Add(GetString(marker, "rockfaxID"));
                _lvResults.Items.Add(item);
            }
        });

    private Task LoadLogbookUiAsync()
        => RunAsync("logbook", async () =>
        {
            using JsonDocument doc = await _api.GetLogbookAsync(_api.UserId);
            _txtDetails.Text = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        });

    private async Task<Dictionary<int, string>> LoadCragNamesAsync()
    {
        using JsonDocument doc = await _api.GetCragMarkersAsync();
        var names = new Dictionary<int, string>();
        foreach (JsonElement marker in doc.RootElement.GetProperty("markers").EnumerateArray())
        {
            if (marker.TryGetProperty("rockfaxID", out JsonElement id) && id.ValueKind == JsonValueKind.Number)
                names[id.GetInt32()] = GetString(marker, "title");
        }
        return names;
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : "";

    // ---- headless test hooks (used by SelfTest; no window required) -------

    internal async Task<int> LoadFreeCragsForTestAsync()
    {
        await LoadFreeCragsUiAsync();
        return _lvResults.Items.Count;
    }

    internal async Task<int> LoadMarkersForTestAsync()
    {
        await LoadMarkersUiAsync();
        return _lvResults.Items.Count;
    }

    internal async Task<(int Count, string FirstName)> SearchForTestAsync(string query)
    {
        await SearchUiAsync(query);
        return (_lvResults.Items.Count, _lvResults.Items.Count > 0 ? _lvResults.Items[0].Text : "");
    }

    internal async Task<(int Height, int Pitches)> RouteInfoForTestAsync(int routeId)
    {
        RouteInfo info = await _api.GetRouteInfoAsync(routeId);
        return (info.Height, info.Pitches);
    }
}
