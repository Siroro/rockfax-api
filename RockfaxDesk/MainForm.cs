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
    private Button _btnHelp = new();

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
    private SplitContainer? _contentSplit;

    private readonly Label _spinLabel = new()
    {
        Dock = DockStyle.Left,
        Width = 30,
        ForeColor = Ui.Accent,
        BackColor = Ui.BgDeep,
        Font = Ui.Mono,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "",
    };
    private readonly System.Windows.Forms.Timer _spinTimer = new() { Interval = 110 };
    private readonly System.Windows.Forms.Timer _searchDebounce = new() { Interval = 450 };
    private int _busy;
    private int _spinFrame;

    private static readonly string[] SpinFrames = { "\u28CB", "\u2899", "\u28B9", "\u2839", "\u2838", "\u2834", "\u2826", "\u2827", "\u2807", "\u280F" };

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
        _cragView.MapRequested += id =>
        {
            CragPoint? p = _cragPoints.FirstOrDefault(x => x.UkcId == id || x.RockfaxId == id);
            if (p is null) { _lblStatus.Text = "crag location unknown"; return; }
            _tabs.Select(2);
            _map.CenterOn(p.Lat, p.Lng, 4f);
            _map.SelectCrag(id);
            _lblStatus.Text = $"map centred on {p.Title}";
        };
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

        _spinTimer.Tick += (_, _) =>
        {
            _spinLabel.Text = _busy > 0 ? SpinFrames[_spinFrame++ % SpinFrames.Length] : "";
        };
        _spinTimer.Start();

        // Live search: fire 450ms after the last keystroke (Enter still works).
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            if (_txtSearch.Text.Trim().Length >= 3) _ = SearchAsync(_txtSearch.Text);
        };

        // ---- events ------------------------------------------------------------
        _btnLogin.Click += async (_, _) => await LoginAsync();
        _btnLogbook.Click += async (_, _) => { _tabs.Select(3); await _logbookView.LoadAsync(_api); };
        _btnSearch.Click += async (_, _) => await SearchAsync(_txtSearch.Text);
        _btnAllCrags.Click += async (_, _) => await LoadAllCragsAsync();
        _btnFreeCrags.Click += async (_, _) => await LoadFreeCragsAsync();
        _btnTop10.Click += async (_, _) => { _tabs.Select(4); await _top10View.LoadAsync(); };
        _lvLeft.DoubleClick += (_, _) => _ = LeftItemActivated();
        _lvLeft.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { await LeftItemActivated(); } };
        _txtSearch.TextChanged += (_, _) =>
        {
            _searchDebounce.Stop();
            if (_txtSearch.Text.Trim().Length >= 3) _searchDebounce.Start();
        };
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _searchDebounce.Stop(); await SearchAsync(_txtSearch.Text); }
            if (e.KeyCode == Keys.Escape) { _txtSearch.Clear(); }
        };
        _txtCragFilter.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await CragFilterAsync(_txtCragFilter.Text); }
            if (e.KeyCode == Keys.Escape) { _txtCragFilter.Clear(); }
        };
        KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F) { _txtSearch.Focus(); e.Handled = true; }
            if (e.KeyCode == Keys.F5) { await ReloadCurrentAsync(); e.Handled = true; }
            if (e.KeyCode == Keys.F1) { _btnHelp.PerformClick(); e.Handled = true; }
        };
        _lblStatus.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_lblStatus.Text);
                string was = _lblStatus.Text;
                _lblStatus.Text = "copied to clipboard";
                CopyReset();
                void CopyReset()
                {
                    var t = new System.Windows.Forms.Timer { Interval = 1400 };
                    t.Tick += (_, _) => { t.Stop(); t.Dispose(); _lblStatus.Text = was; };
                    t.Start();
                }
            }
            catch { /* clipboard can be locked \u2014 ignore */ }
        };
    }

    private void SetBusy(bool busy)
    {
        _busy = Math.Max(0, _busy + (busy ? 1 : -1));
        UseWaitCursor = _busy > 0;
        _btnSearch.Enabled = _btnAllCrags.Enabled = _btnFreeCrags.Enabled = _btnTop10.Enabled = _busy == 0;
        if (_busy > 0) _spinTimer.Start();
        else if (_busy == 0) { _spinTimer.Stop(); _spinLabel.Text = ""; }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // SplitterDistance is only reliable once the container has a real size; the
        // restored value is applied at the end of OnShown (after layout settles).

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
        // (hooks above are used by the screenshot harness: shot.ps1)

        // Apply the persisted splitter width now that the layout has real sizes.
        // Min sizes keep the rail usable no matter what was saved or dragged.
        if (_contentSplit is { } split)
        {
            split.Panel1MinSize = 250;
            split.Panel2MinSize = 480;
            try { split.SplitterDistance = Math.Clamp(_restoreSplitter, split.Panel1MinSize, split.Width - split.Panel2MinSize); }
            catch { split.SplitterDistance = 390; }
        }
        _lblStatus.Text = "ready — Ctrl+F to search";
    }

    // ---- window state persistence ---------------------------------------------

    private static string StatePath
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RockfaxDesk");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "window.json");
        }
    }

    private sealed record SavedWindowState(int X, int Y, int W, int H, int Splitter, bool Maximized,
                                           float MapZoom, float MapPanX, float MapPanY);

    private void SaveWindowState()
    {
        try
        {
            if (_contentSplit is null) return;
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width < MinimumSize.Width) return;
            (float mz, float mx, float my) = _map.GetView();
            var s = new SavedWindowState(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                                    _contentSplit.SplitterDistance, WindowState == FormWindowState.Maximized,
                                    mz, mx, my);
            File.WriteAllText(StatePath, System.Text.Json.JsonSerializer.Serialize(s));
        }
        catch { /* best effort */ }
    }

    private int _restoreSplitter = 390;
    private (float Zoom, float X, float Y)? _restoreMap;

    private void RestoreWindowState()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            var s = System.Text.Json.JsonSerializer.Deserialize<SavedWindowState>(File.ReadAllText(StatePath));
            if (s is null) return;
            if (s.W < MinimumSize.Width || s.H < MinimumSize.Height) return;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Math.Max(s.X, -4), Math.Max(s.Y, -4));
            Size = new Size(s.W, s.H);
            _restoreSplitter = s.Splitter;
            if (s.MapZoom > 0) _restoreMap = (s.MapZoom, s.MapPanX, s.MapPanY);
            if (s.Maximized) WindowState = FormWindowState.Maximized;
        }
        catch { /* best effort */ }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveWindowState();
        base.OnFormClosing(e);
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

        _btnHelp = Ui.Button("?", 30);
        _btnHelp.Click += (_, _) =>
            MessageBox.Show(
                "Rockfax Explorer \u2014 unofficial UKClimbing client\n\n" +
                "Reverse engineered from the Rockfax Android app for personal interoperability.\n" +
                "Not affiliated with Rockfax or UKClimbing; use your own account and keep request volume sane.\n\n" +
                "Shortcuts:\n" +
                "  Ctrl+F  focus search\n" +
                "  Enter   search / open selection\n" +
                "  Esc     clear the search box\n" +
                "  F5      refresh current route/crag/list\n" +
                "  Map:    drag = pan \u00b7 wheel or +/- = zoom \u00b7 click a dot = open crag\n" +
                "  Photos: \u2190/\u2192 or wheel = previous/next \u00b7 click = close",
                "About Rockfax Explorer", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        auth.Controls.Add(_btnHelp);
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
        bar.Controls.Add(_spinLabel);
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
            if (_busy > 0) { _lblStatus.Text = "busy — wait for the current request"; return; }
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

    private RouteSummary? _lastRoute;
    private (int Id, string Name)? _lastCrag;

    private async Task OpenRouteAsync(RouteSummary route)
    {
        _lastRoute = route;
        _tabs.Select(0);
        await _routeView.ShowRouteAsync(route);
        _lblStatus.Text = $"route: {route.Name}";
    }

    private async Task OpenCragAsync(int ukcCragId, string name)
    {
        _lastCrag = (ukcCragId, name);
        _tabs.Select(1);
        _map.SelectCrag(ukcCragId); // ring the dot on the map too
        await _cragView.ShowCragAsync(ukcCragId, name);
        _lblStatus.Text = $"crag: {name}";
    }

    private Task ReloadCurrentAsync()
        => RunAsync("refreshing", async () =>
        {
            if (_busy > 1) { _lblStatus.Text = "busy — wait for the current request"; return; }
            if (_tabs.SelectedIndex == 0 && _lastRoute is not null) { await _routeView.ShowRouteAsync(_lastRoute); _lblStatus.Text = $"route: {_lastRoute.Name}"; }
            else if (_tabs.SelectedIndex == 1 && _lastCrag is not null) { await _cragView.ShowCragAsync(_lastCrag.Value.Id, _lastCrag.Value.Name); _lblStatus.Text = $"crag: {_lastCrag.Value.Name}"; }
            else if (_tabs.SelectedIndex == 4) { await _top10View.LoadAsync(); }
            else if (_tabs.SelectedIndex == 3 && _api.IsLoggedIn) { await _logbookView.LoadAsync(_api); }
            else _lblStatus.Text = "nothing to refresh on this tab";
        });

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
        if (_restoreMap is { } view) { _map.ApplyView(view.Zoom, view.X, view.Y); _restoreMap = null; }
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
                    item.SubItems.Add(p.Free ? "free sample" : $"{p.Lat:0.00}, {p.Lng:0.00}");
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
            if (_busy > 0) { _lblStatus.Text = "busy — wait for the current request"; return; }
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
