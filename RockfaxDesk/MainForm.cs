using System.Drawing;
using System.Text.Json;
using RockfaxApi;
using RockfaxDesk.Controls;

namespace RockfaxDesk;

/// <summary>
/// Rockfax API Explorer — search, route/crag pages, a zoomable dot-map of every UKC crag,
/// photo galleries, and (with login) your logbook and wishlist.
/// </summary>
public sealed class MainForm : Form
{
    private readonly RockfaxClient _api = new();
    private readonly ImageFetcher _images = new();

    // ---- top strips -------------------------------------------------------

    private readonly TextBox _txtEmail = new() { Left = 55, Top = 10, Width = 180, PlaceholderText = "you@example.com" };
    private readonly TextBox _txtPassword = new() { Left = 295, Top = 10, Width = 120, UseSystemPasswordChar = true, PlaceholderText = "password" };
    private readonly Button _btnLogin = new() { Text = "Login", Left = 422, Top = 8, Width = 62, Height = 25 };
    private readonly Label _lblUser = new() { Text = "not logged in — public data still works", Left = 500, Top = 13, AutoSize = true, ForeColor = Color.FromArgb(150, 200, 240) };
    private readonly Button _btnLogbook = new() { Text = "My logbook", Left = 850, Top = 8, Width = 95, Height = 25, Enabled = false };

    private readonly TextBox _txtSearch = new() { Left = 10, Top = 44, Width = 250, PlaceholderText = "route name… (Enter)" };
    private readonly Button _btnSearch = new() { Text = "Search routes", Left = 266, Top = 42, Width = 95, Height = 25 };
    private readonly TextBox _txtCragFilter = new() { Left = 372, Top = 44, Width = 170, PlaceholderText = "filter crags…" };
    private readonly Button _btnAllCrags = new() { Text = "All crags", Left = 548, Top = 42, Width = 80, Height = 25 };
    private readonly Button _btnFreeCrags = new() { Text = "Free crags", Left = 632, Top = 42, Width = 85, Height = 25 };
    private readonly Button _btnTop10 = new() { Text = "Top 10 photos", Left = 722, Top = 42, Width = 105, Height = 25 };

    // ---- left list --------------------------------------------------------

    private readonly ListView _lvLeft = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(20, 28, 40), ForeColor = Color.Gainsboro,
        VirtualMode = true,
    };
    private readonly Label _lblLeftHeader = new()
    {
        Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(150, 200, 240),
        Font = new Font("Segoe UI Semibold", 9.5f), Padding = new Padding(8, 2, 0, 0), Text = "results",
    };
    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Bottom, Height = 22, BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9f), Padding = new Padding(6, 2, 0, 0),
        Text = "ready — search “Stanage”, or click All crags / Free crags, or open the Crag map tab",
    };

    private List<CragPoint> _cragPoints = new();
    private readonly List<ListViewItem> _leftItems = new();

    // ---- right tabs -------------------------------------------------------

    private readonly RouteView _routeView = new();
    private readonly CragView _cragView = new();
    private readonly CragMapCanvas _map = new();
    private readonly LogbookView _logbookView = new();
    private readonly Top10View _top10View = new();
    private readonly Label _mapNote = new()
    {
        Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(150, 200, 240),
        Font = new Font("Segoe UI", 9.5f), Padding = new Padding(8, 2, 0, 0),
    };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "Rockfax API Explorer (unofficial)";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1240;
        Height = 780;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(20, 28, 40);
        ForeColor = Color.Gainsboro;

        _lvLeft.Columns.Add("Name", 190);
        _lvLeft.Columns.Add("Grade", 60);
        _lvLeft.Columns.Add("Crag", 110);
        _lvLeft.RetrieveVirtualItem += (_, e) => e.Item = _leftItems.Count > e.ItemIndex ? _leftItems[e.ItemIndex] : new ListViewItem();

        _routeView.Bind(_api, _images);
        _cragView.Bind(_api, _images);
        _top10View.Bind(_api, _images);
        _routeView.CragRequested += (id, name) => _ = OpenCragAsync(id, name);
        _cragView.RouteRequested += r => _ = OpenRouteAsync(r);
        _logbookView.RouteRequested += r => _ = OpenRouteAsync(r);
        _map.CragSelected += p => _ = OpenCragAsync(p.UkcId, p.Title);

        var routeTab = new TabPage("Route") { BackColor = Color.FromArgb(24, 32, 44) };
        routeTab.Controls.Add(_routeView);
        var cragTab = new TabPage("Crag") { BackColor = Color.FromArgb(24, 32, 44) };
        cragTab.Controls.Add(_cragView);
        var mapTab = new TabPage("Crag map") { BackColor = Color.FromArgb(18, 26, 38) };
        mapTab.Controls.Add(_map);
        mapTab.Controls.Add(_mapNote);
        var logbookTab = new TabPage("Logbook") { BackColor = Color.FromArgb(24, 32, 44) };
        logbookTab.Controls.Add(_logbookView);
        var top10Tab = new TabPage("Top 10") { BackColor = Color.FromArgb(24, 32, 44) };
        top10Tab.Controls.Add(_top10View);
        _tabs.TabPages.AddRange(new[] { routeTab, cragTab, mapTab, logbookTab, top10Tab });

        var left = new Panel { Dock = DockStyle.Left, Width = 380, BackColor = Color.FromArgb(20, 28, 40) };
        left.Controls.Add(_lvLeft);
        left.Controls.Add(_lblLeftHeader);

        var top = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Color.FromArgb(20, 28, 40) };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Email:", Left = 12, Top = 13, AutoSize = true },
            _txtEmail,
            new Label { Text = "Password:", Left = 238, Top = 13, AutoSize = true },
            _txtPassword,
            _btnLogin,
            _lblUser,
            _btnLogbook,
            _txtSearch,
            _btnSearch,
            _txtCragFilter,
            _btnAllCrags,
            _btnFreeCrags,
            _btnTop10,
        });

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_tabs);
        split.SplitterDistance = 380;

        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(_lblStatus);

        _btnLogin.Click += async (_, _) => await LoginAsync();
        _btnLogbook.Click += async (_, _) => { _tabs.SelectedIndex = 3; await _logbookView.LoadAsync(_api); };
        _btnSearch.Click += async (_, _) => await SearchAsync(_txtSearch.Text);
        _txtSearch.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchAsync(_txtSearch.Text); } };
        _btnAllCrags.Click += async (_, _) => await LoadAllCragsAsync();
        _btnFreeCrags.Click += async (_, _) => await LoadFreeCragsAsync();
        _btnTop10.Click += async (_, _) => { _tabs.SelectedIndex = 4; await _top10View.LoadAsync(); };
        _lvLeft.DoubleClick += (_, _) => _ = LeftItemActivated();
        _txtCragFilter.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await CragFilterAsync(_txtCragFilter.Text); } };
    }

    // ---- shared helpers ---------------------------------------------------

    private async Task RunAsync(string what, Func<Task> work)
    {
        try
        {
            _lblStatus.Text = $"{what}…";
            await work();
            _lblStatus.Text = $"{what}: done";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"{what}: FAILED — {ex.Message}";
        }
    }

    private void SetLeftItems(IEnumerable<ListViewItem> items, string header)
    {
        _leftItems.Clear();
        _leftItems.AddRange(items.ToList());
        _lvLeft.VirtualListSize = _leftItems.Count;
        _lblLeftHeader.Text = header;
    }

    // ---- actions ----------------------------------------------------------

    private Task LoginAsync()
        => RunAsync("login", async () =>
        {
            var session = await _api.LoginAsync(_txtEmail.Text, _txtPassword.Text);
            _lblUser.Text = $"logged in as {session.Username} (id {session.UserId})";
            _btnLogbook.Enabled = true;
            _tabs.SelectedIndex = 3;
            await _logbookView.LoadAsync(_api);
        });

    private Task SearchAsync(string query)
        => RunAsync($"search “{query}”", async () =>
        {
            if (query.Trim().Length < 3) { _lblStatus.Text = "type at least 3 characters"; return; }
            using JsonDocument doc = await _api.SearchRoutesAsync(query);
            var items = new List<ListViewItem>();
            foreach (JsonElement route in doc.RootElement.GetProperty("routes").EnumerateArray())
            {
                RouteSummary summary = RouteSummary.FromSearch(route);
                var item = new ListViewItem(summary.Name);
                item.SubItems.Add(summary.Grade);
                item.SubItems.Add(summary.CragName);
                item.Tag = summary;
                items.Add(item);
            }
            SetLeftItems(items, $"routes matching “{query}” — double-click to open");
            if (items.Count > 0 && items[0].Tag is RouteSummary first) await OpenRouteAsync(first);
        });

    private async Task OpenRouteAsync(RouteSummary route)
    {
        _tabs.SelectedIndex = 0;
        await _routeView.ShowRouteAsync(route);
        _lblStatus.Text = $"route: {route.Name}";
    }

    private async Task OpenCragAsync(int ukcCragId, string name)
    {
        _tabs.SelectedIndex = 1;
        await _cragView.ShowCragAsync(ukcCragId, name);
        _lblStatus.Text = $"crag: {name}";
    }

    /// <summary>Fetches markers once (shared by map + crag lists) and paints the map.</summary>
    private async Task<List<CragPoint>> EnsureCragPointsAsync()
    {
        if (_cragPoints.Count > 0) return _cragPoints;
        using JsonDocument doc = await _api.GetCragMarkersAsync();
        var points = new List<CragPoint>();
        foreach (JsonElement marker in doc.RootElement.GetProperty("markers").EnumerateArray())
        {
            points.Add(new CragPoint
            {
                Lat = (float)marker.Dbl("lat"),
                Lng = (float)marker.Dbl("lng"),
                UkcId = marker.Int("id"),
                RockfaxId = marker.Int("rockfaxID"),
                NRoutes = marker.Int("nroutes"),
                Title = marker.Str("title"),
            });
        }
        _cragPoints = points;
        _map.Points = points;
        _mapNote.Text = _map.Subtitle;
        return points;
    }

    private Task LoadAllCragsAsync()
        => RunAsync("all crags", async () =>
        {
            List<CragPoint> points = await EnsureCragPointsAsync();
            SetLeftItems(points
                .OrderByDescending(p => p.NRoutes)
                .Select(p =>
                {
                    var item = new ListViewItem(p.Title);
                    item.SubItems.Add(p.NRoutes.ToString());
                    item.SubItems.Add($"{p.Lat:0.000}, {p.Lng:0.000}");
                    item.Tag = p;
                    return item;
                }), $"{points.Count:N0} crags (busiest first) — double-click to open");
            _lblStatus.Text = "crag list loaded — also check the Crag map tab";
        });

    private Task LoadFreeCragsAsync()
        => RunAsync("free crags", async () =>
        {
            Task<JsonDocument> freeTask = _api.GetFreeCragsAsync();
            List<CragPoint> points = await EnsureCragPointsAsync();
            using JsonDocument free = await freeTask;

            var freeIds = new HashSet<int>(free.RootElement.GetProperty("free_crags").EnumerateArray()
                .Select(x => x.GetInt32()).Where(x => x != 0));
            foreach (CragPoint p in points) p.Free = freeIds.Contains(p.RockfaxId) || freeIds.Contains(p.UkcId);
            _map.Points = _cragPoints; // re-paint with free-sample highlights
            _mapNote.Text = _map.Subtitle + "   (orange = free sample)";

            SetLeftItems(points.Where(p => p.Free)
                .Select(p =>
                {
                    var item = new ListViewItem(p.Title);
                    item.SubItems.Add(p.NRoutes.ToString());
                    item.SubItems.Add("free sample");
                    item.Tag = p;
                    return item;
                }), $"{points.Count(p => p.Free)} free-sample crags — double-click to open");
        });

    private Task CragFilterAsync(string filter)
        => RunAsync($"crags matching “{filter}”", async () =>
        {
            List<CragPoint> points = await EnsureCragPointsAsync();
            SetLeftItems(points
                .Where(p => p.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Title)
                .Select(p =>
                {
                    var item = new ListViewItem(p.Title);
                    item.SubItems.Add(p.NRoutes.ToString());
                    item.SubItems.Add($"{p.Lat:0.000}, {p.Lng:0.000}");
                    item.Tag = p;
                    return item;
                }), $"crags matching “{filter}” — double-click to open");
        });

    private Task LeftItemActivated()
        => RunAsync("open item", async () =>
        {
            if (_lvLeft.SelectedItems.Count == 0) return;
            switch (_lvLeft.SelectedItems[0].Tag)
            {
                case RouteSummary route:
                    await OpenRouteAsync(route);
                    break;
                case CragPoint crag:
                    await OpenCragAsync(crag.UkcId, crag.Title);
                    break;
            }
        });
}
