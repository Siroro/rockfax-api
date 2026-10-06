using System.Drawing;
using System.Runtime.InteropServices;
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

    // ---- header (auth) ------------------------------------------------------
    private readonly TextBox _txtEmail = new();
    private readonly TextBox _txtPassword = new() { UseSystemPasswordChar = true };
    private readonly Label _lblUser = Ui.Label("not signed in", Ui.Muted, Ui.Small);
    private readonly Button _btnLogin;
    private readonly Button _btnLogbook;

    // ---- toolbar ------------------------------------------------------------
    private readonly TextBox _txtSearch = new();
    private readonly TextBox _txtCragFilter = new();
    private readonly Button _btnSearch;
    private readonly Button _btnAllCrags;
    private readonly Button _btnFreeCrags;
    private readonly Button _btnTop10;

    // ---- left rail ------------------------------------------------------------
    private readonly UiList _lvLeft = new() { VirtualMode = true };
    private readonly Label _lblLeftHeader = Ui.Label("results", Ui.Muted, Ui.BodyBold);
    private readonly Label _lblLeftCount = Ui.Label("", Ui.Muted, Ui.Tiny);
    private readonly List<ListViewItem> _leftItems = new();
    private List<CragPoint> _cragPoints = new();

    // ---- right content --------------------------------------------------------
    private readonly RouteView _routeView = new();
    private readonly CragView _cragView = new();
    private readonly CragMapCanvas _map = new();
    private readonly LogbookView _logbookView = new();
    private readonly Top10View _top10View = new();
    private readonly TabStrip _tabs = new();
    private SplitContainer _contentSplit;

    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Ui.Muted,
        BackColor = Ui.BgDeep,
        Font = Ui.Small,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 0, 0, 0),
        Text = "ready — Ctrl+F to search",
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            int on = 1;
            try { _ = DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { } // dark title bar
        }
    }

    public MainForm()
    {
        Text = "Rockfax Explorer — unofficial UKClimbing client";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1160, 720);
        Size = new Size(1360, 820);
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
        Font = Ui.Body;
        KeyPreview = true;

        _btnLogin = Ui.Button("Sign in", 84, primary: true);
        _btnLogbook = Ui.Button("My logbook", 100);
        _btnLogbook.Enabled = false;
        _btnSearch = Ui.Button("Search", 84, primary: true);
        _btnAllCrags = Ui.Button("All crags", 88);
        _btnFreeCrags = Ui.Button("Free crags", 92);
        _btnTop10 = Ui.Button("Top 10", 78);

        BuildLeftRail();
        BuildTabs();

        _routeView.Bind(_api, _images);
        _cragView.Bind(_api, _images);
        _top10View.Bind(_api, _images);
        _routeView.CragRequested += (id, name) => _ = OpenCragAsync(id, name);
        _cragView.RouteRequested += r => _ = OpenRouteAsync(r);
        _logbookView.RouteRequested += r => _ = OpenRouteAsync(r);
        _map.CragSelected += p => _ = OpenCragAsync(p.UkcId, p.Title);

        // ---- structure ---------------------------------------------------------
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Ui.Bg, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // toolbar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // content
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // status

        root.Controls.Add(BuildHeaderBar(), 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);
        root.Controls.Add(BuildContent(), 0, 2);
        root.Controls.Add(BuildStatusBar(), 0, 3);
        Controls.Add(root);

        // ---- events ------------------------------------------------------------
        _btnLogin.Click += async (_, _) => await LoginAsync();
        _btnLogbook.Click += async (_, _) => { _tabs.Select(3); await _logbookView.LoadAsync(_api); };
        _btnSearch.Click += async (_, _) => await SearchAsync(_txtSearch.Text);
        _btnAllCrags.Click += async (_, _) => await LoadAllCragsAsync();
        _btnFreeCrags.Click += async (_, _) => await LoadFreeCragsAsync();
        _btnTop10.Click += async (_, _) => { _tabs.Select(4); await _top10View.LoadAsync(); };
        _lvLeft.DoubleClick += (_, _) => _ = LeftItemActivated();
        _lvLeft.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { await LeftItemActivated(); } };
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchAsync(_txtSearch.Text); }
            if (e.KeyCode == Keys.Escape) { _txtSearch.Clear(); }
        };
        _txtCragFilter.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await CragFilterAsync(_txtCragFilter.Text); }
            if (e.KeyCode == Keys.Escape) { _txtCragFilter.Clear(); }
        };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F) { _txtSearch.Focus(); e.Handled = true; }
        };
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // SplitterDistance is only reliable once the container has a real size.
        _contentSplit.SplitterDistance = 390;

        // Warm the crag cache so the map/crag lists feel instant.
        try
        {
            _lblStatus.Text = "warming the crag map…";
            await EnsureCragPointsAsync();
            _lblStatus.Text = "ready — Ctrl+F to search";
        }
        catch { /* offline is fine; lists load on demand */ }

        // Dev/screenshot hooks: --goto=N [--search=text] [--freecrags]
        string[] args = Program.StartupArgs;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--goto=", StringComparison.Ordinal) && int.TryParse(arg[7..], out int tab))
                _tabs.Select(tab);
            if (arg.StartsWith("--search=", StringComparison.Ordinal))
                await SearchAsync(Uri.UnescapeDataString(arg[9..]));
        }
        if (args.Contains("--freecrags")) await LoadFreeCragsAsync();
        foreach (string arg in args)
            if (arg.StartsWith("--crag=", StringComparison.Ordinal) && int.TryParse(arg[7..], out int cragId))
                await OpenCragAsync(cragId, "");
        if (args.Contains("--top10")) { _tabs.Select(4); await _top10View.LoadAsync(); }
        Text = $"Rockfax Explorer — unofficial UKClimbing client  [tab {_tabs.SelectedIndex}]";
    }

    // ---- layout builders -----------------------------------------------------

    private Panel BuildHeaderBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Ui.BgDeep, Padding = new Padding(14, 0, 14, 0) };

        var title = Ui.Label("ROCKFAX EXPLORER", Ui.Text, Ui.H1, auto: false);
        title.AutoSize = false;
        title.Size = new Size(232, 54);
        title.TextAlign = ContentAlignment.MiddleLeft;
        var subtitle = Ui.Label("unofficial UKClimbing client", Ui.Muted, Ui.Tiny);
        subtitle.Location = new Point(244, 22);

        // Auth cluster hugs the right edge. FlowDirection.RightToLeft places the first
        // child at the right edge and flows leftward, so add in reverse visual order.
        var auth = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 820,
            BackColor = Ui.BgDeep,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0),
            WrapContents = false,
        };
        auth.Controls.Add(_btnLogbook);
        auth.Controls.Add(_lblUser);
        auth.Controls.Add(_btnLogin);
        auth.Controls.Add(Ui.Box(_txtPassword, 116, cue: "password"));
        auth.Controls.Add(Ui.Box(_txtEmail, 176, cue: "you@example.com"));

        bar.Controls.Add(auth);
        bar.Controls.Add(subtitle);
        bar.Controls.Add(title);
        return bar;
    }

    private Panel BuildToolbar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(14, 9, 14, 0) };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Ui.Bg,
            WrapContents = false,
        };
        flow.Controls.Add(Ui.Box(_txtSearch, 230, cue: "route name…  (Enter)"));
        flow.Controls.Add(_btnSearch);
        flow.Controls.Add(MakeSeparator());
        flow.Controls.Add(Ui.Box(_txtCragFilter, 170, cue: "filter crags…  (Enter)"));
        flow.Controls.Add(_btnAllCrags);
        flow.Controls.Add(_btnFreeCrags);
        flow.Controls.Add(MakeSeparator());
        flow.Controls.Add(_btnTop10);
        bar.Controls.Add(flow);
        return bar;
    }

    private static Panel MakeSeparator()
        => new() { Width = 1, Height = 26, BackColor = Ui.Border, Margin = new Padding(4, 2, 14, 0) };

    private SplitContainer BuildContent()
    {
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(10, 10, 0, 6) };
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Ui.BgDeep, Padding = Padding.Empty };
        _lvLeft.Dock = DockStyle.Fill;
        _lblLeftHeader.Dock = DockStyle.Top;
        _lblLeftHeader.Height = 26;
        _lblLeftHeader.BackColor = Ui.BgDeep;
        _lblLeftHeader.Padding = new Padding(4, 4, 0, 0);
        _lblLeftCount.Dock = DockStyle.Bottom;
        _lblLeftCount.Height = 20;
        _lblLeftCount.BackColor = Ui.BgDeep;
        _lblLeftCount.Padding = new Padding(4, 2, 0, 0);
        listHost.Controls.Add(_lvLeft);
        listHost.Controls.Add(_lblLeftCount);
        listHost.Controls.Add(_lblLeftHeader);
        left.Controls.Add(listHost);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 5,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Ui.Bg,
        };
        split.Panel1.BackColor = Ui.Bg;
        split.Panel2.BackColor = Ui.Bg;
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_tabs);
        _contentSplit = split;
        return split;
    }

    private Panel BuildStatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Ui.BgDeep, Padding = Padding.Empty };
        var right = Ui.Label("app-live.ukclimbing.com · unofficial · use your own account", Ui.Muted, Ui.Tiny);
        right.Dock = DockStyle.Right;
        right.TextAlign = ContentAlignment.MiddleRight;
        right.Padding = new Padding(0, 3, 12, 0);
        right.AutoSize = false;
        right.Size = new Size(360, 26);
        bar.Controls.Add(right);
        bar.Controls.Add(_lblStatus);
        return bar;
    }

    private void BuildLeftRail()
    {
        _lvLeft.Columns.Add("Name", 152);
        _lvLeft.Columns.Add("Grade", 48);
        _lvLeft.Columns.Add("Detail", 118);
        _lvLeft.ColumnInk[1] = Ui.Amber;
        _lvLeft.StretchLastColumn();
        _lvLeft.RetrieveVirtualItem += (_, e) => e.Item = _leftItems.Count > e.ItemIndex ? _leftItems[e.ItemIndex] : new ListViewItem();
    }

    private void BuildTabs()
    {
        _tabs.AddTab("ROUTE", _routeView);
        _tabs.AddTab("CRAG", _cragView);
        _tabs.AddTab("CRAG MAP", _map);
        _tabs.AddTab("LOGBOOK", _logbookView);
        _tabs.AddTab("TOP 10", _top10View);
    }

    // ---- shared helpers ---------------------------------------------------

    private async Task RunSafe(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) { _lblStatus.Text = $"failed: {ex.GetType().Name} — {ex.Message}"; }
    }

    private async Task RunAsync(string what, Func<Task> work)
    {
        try
        {
            _lblStatus.Text = $"{what}…";
            await work();
            _lblStatus.Text = $"{what} — done";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"{what} — failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void SetLeftItems(IEnumerable<(ListViewItem Item, string Key)> items, string header, string count)
    {
        _leftItems.Clear();
        _leftItems.AddRange(items.Select(x => x.Item));
        _lvLeft.VirtualListSize = _leftItems.Count;
        _lblLeftHeader.Text = header;
        _lblLeftCount.Text = count;
    }

    // ---- actions ----------------------------------------------------------

    private Task LoginAsync()
        => RunAsync("signing in", async () =>
        {
            var session = await _api.LoginAsync(_txtEmail.Text, _txtPassword.Text);
            _lblUser.Text = $"{session.Username} · user {session.UserId}";
            _lblUser.ForeColor = Ui.Green;
            _btnLogbook.Enabled = true;
            _tabs.Select(3);
            await _logbookView.LoadAsync(_api);
        });

    private Task SearchAsync(string query)
        => RunSafe(async () =>
        {
            if (query.Trim().Length < 3) { _lblStatus.Text = "type at least 3 characters"; return; }
            _lblStatus.Text = $"searching “{query}”…";
            using JsonDocument doc = await _api.SearchRoutesAsync(query);
            var items = new List<(ListViewItem Item, string Key)>();
            RouteSummary? first = null;
            foreach (JsonElement route in doc.RootElement.GetProperty("routes").EnumerateArray())
            {
                RouteSummary summary = RouteSummary.FromSearch(route);
                var item = new ListViewItem(summary.Name);
                item.SubItems.Add(summary.Grade);
                item.SubItems.Add(summary.CragName);
                item.Tag = summary;
                first ??= summary;
                items.Add((item, summary.Name));
            }
            SetLeftItems(items, $"routes matching “{query}”", $"{items.Count} results — double-click or press Enter to open");
            _lblStatus.Text = $"search “{query}” — {items.Count} results";
            if (first is not null) await OpenRouteAsync(first);
        });

    private async Task OpenRouteAsync(RouteSummary route)
    {
        _tabs.Select(0);
        await _routeView.ShowRouteAsync(route);
        _lblStatus.Text = $"route: {route.Name}";
    }

    private async Task OpenCragAsync(int ukcCragId, string name)
    {
        _tabs.Select(1);
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
            double lat = marker.Dbl("lat"), lng = marker.Dbl("lng");
            int nRoutes = marker.Int("nroutes");
            // Skip bad rows: swapped/garbage coordinates and int16-max route counts.
            if (lat is < 30 or > 70 || lng is < -30 or > 40 || nRoutes is <= 0 or > 5000) continue;
            points.Add(new CragPoint
            {
                Lat = (float)lat,
                Lng = (float)lng,
                UkcId = marker.Int("id"),
                RockfaxId = marker.Int("rockfaxID"),
                NRoutes = nRoutes,
                Title = marker.Str("title"),
            });
        }
        _cragPoints = points;
        _map.Points = points;
        return points;
    }

    private Task LoadAllCragsAsync()
        => RunAsync("loading all crags", async () =>
        {
            List<CragPoint> points = await EnsureCragPointsAsync();
            SetLeftItems(points
                .OrderByDescending(p => p.NRoutes)
                .Select(p =>
                {
                    var item = new ListViewItem(p.Title);
                    item.SubItems.Add(p.NRoutes.ToString());
                    item.SubItems.Add($"{p.Lat:0.00}, {p.Lng:0.00}");
                    item.Tag = p;
                    return (item, p.Title);
                }), "all crags — busiest first", $"{points.Count:N0} crags — double-click to open · see the CRAG MAP tab");
        });

    private Task LoadFreeCragsAsync()
        => RunAsync("loading free crags", async () =>
        {
            Task<JsonDocument> freeTask = _api.GetFreeCragsAsync();
            List<CragPoint> points = await EnsureCragPointsAsync();
            using JsonDocument free = await freeTask;

            var freeIds = new HashSet<int>(free.RootElement.GetProperty("free_crags").EnumerateArray()
                .Select(x => x.GetInt32()).Where(x => x != 0));
            foreach (CragPoint p in points) p.Free = freeIds.Contains(p.RockfaxId) || freeIds.Contains(p.UkcId);
            _map.Points = _cragPoints; // repaint with amber free-sample highlights

            SetLeftItems(points.Where(p => p.Free)
                .Select(p =>
                {
                    var item = new ListViewItem(p.Title);
                    item.SubItems.Add(p.NRoutes.ToString());
                    item.SubItems.Add("free sample");
                    item.Tag = p;
                    return (item, p.Title);
                }), "free-sample crags", $"{points.Count(p => p.Free)} crags highlighted amber on the CRAG MAP");
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
                    item.SubItems.Add($"{p.Lat:0.00}, {p.Lng:0.00}");
                    item.Tag = p;
                    return (item, p.Title);
                }), $"crags matching “{filter}”", $"{_leftItems.Count} matches — double-click to open");
        });

    private Task LeftItemActivated()
        => RunAsync("opening", async () =>
        {
            if (_lvLeft.SelectedIndices.Count == 0) return;
            switch (_lvLeft.Items[_lvLeft.SelectedIndices[0]].Tag)
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
