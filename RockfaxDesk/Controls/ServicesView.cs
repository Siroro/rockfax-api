using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>One UKC listing (shop / gear / courses / club / wall / stay).</summary>
internal sealed record ServiceItem(string Type, string Name, string Address, string Tel, string Email,
                                   string Web, string Notes, string Times, string Cost);

/// <summary>The UKC services directory from listings/v1/free — shops, gear shops, courses,
/// clubs, walls and places to stay, with type + text filters. Fetched once per run.</summary>
internal sealed class ServicesView : UserControl
{
    private static List<ServiceItem>? _cache; // one 4 MB fetch per app run is plenty

    private readonly UiList _lv = new() { VirtualMode = true };
    private readonly List<ListViewItem> _rows = new();
    private List<ServiceItem> _all = new();
    private string _typeFilter = "";
    private readonly TextBox _filter = new();
    private readonly Label _lblCount = Ui.Label("", Ui.Muted, Ui.Small);
    private readonly TextBox _detail = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Ui.Panel, ForeColor = Ui.Text, Font = Ui.Small,
    };
    private readonly Label _lblStatus = Ui.Label("", Ui.Amber, Ui.Small);
    private RockfaxClient? _api;

    private static readonly (string Key, string Label)[] Types =
    {
        ("shop", "Shops"), ("goods", "Gear"), ("learn", "Courses"),
        ("club", "Clubs"), ("wall", "Walls"), ("stay", "Stay"),
    };

    public ServicesView()
    {
        DoubleBuffered = true;
        BackColor = Ui.Bg;

        _lv.Columns.Add("Name", 220);
        _lv.Columns.Add("Type", 64);
        _lv.Columns.Add("Address", 360);
        _lv.Columns.Add("Phone", 130);
        _lv.Columns.Add("Web", 190);
        _lv.ColumnInk[1] = Ui.Accent;
        _lv.RetrieveVirtualItem += (_, e) => e.Item = _rows.Count > e.ItemIndex ? _rows[e.ItemIndex] : new ListViewItem();
        _lv.SelectedIndexChanged += ShowDetail;
        _lv.DoubleClick += (_, _) =>
        {
            if (FindSelected() is { Web: { Length: > 0 } s }) Jx.OpenBrowser(s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? s : "https://" + s);
        };
        _lv.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem? hit = _lv.HitTest(e.Location).Item;
            if (hit?.Tag is not ServiceItem item) return;
            hit.Selected = true;
            Ui.Menu(
                ("Copy address", () => TryCopy(item.Address)),
                ("Copy phone", () => TryCopy(item.Tel)),
                item.Web.Length > 0 ? ("Open website", () => Jx.OpenBrowser(
                    item.Web.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? item.Web : "https://" + item.Web)) : ("—", () => { })
            ).Show(_lv, e.Location);
        };

        var titleRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Ui.Bg, Padding = new Padding(10, 4, 10, 0) };
        var refresh = Ui.Button("Refresh", 84);
        refresh.Dock = DockStyle.Right;
        refresh.Click += async (_, _) => await LoadAsync(force: true);
        titleRow.Controls.Add(refresh);
        titleRow.Controls.Add(Ui.Label("SERVICES & SHOPS — UKC listings", Ui.Text, Ui.H2));

        var filterRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 40, BackColor = Ui.Bg, WrapContents = false, Padding = new Padding(10, 4, 0, 0),
        };
        var allBtn = Ui.Button("All", 64);
        allBtn.Click += (_, _) => { _typeFilter = ""; ApplyFilter(); };
        filterRow.Controls.Add(allBtn);
        foreach ((string key, string label) in Types)
        {
            string captured = key;
            var btn = Ui.Button(label, 84);
            btn.Click += (_, _) => { _typeFilter = captured; ApplyFilter(); };
            filterRow.Controls.Add(btn);
        }
        var box = Ui.Box(_filter, 230, 30, cue: "filter by name or place…");
        filterRow.Controls.Add(box);
        _lblCount.Dock = DockStyle.Top;
        _lblCount.Height = 22;
        _lblCount.Padding = new Padding(12, 2, 0, 0);
        _filter.TextChanged += (_, _) => ApplyFilter();

        var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Ui.Panel, Padding = new Padding(8, 6, 8, 4) };
        detailHost.Controls.Add(_detail);
        _lblStatus.Dock = DockStyle.Bottom;
        _lblStatus.AutoSize = false;
        _lblStatus.Height = 22;
        _lblStatus.BackColor = Ui.BgDeep;
        _lblStatus.Padding = new Padding(10, 2, 0, 0);

        Controls.Add(_lv);
        Controls.Add(detailHost);
        Controls.Add(_lblStatus);
        Controls.Add(_lblCount);
        Controls.Add(filterRow);
        Controls.Add(titleRow);
    }

    private void TryCopy(string text)
    {
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); _lblStatus.Text = "copied to the clipboard"; }
        catch { _lblStatus.Text = "clipboard is busy — try again"; }
    }

    private ServiceItem? FindSelected()
        => _lv.SelectedIndices.Count > 0 && _lv.SelectedIndices[0] < _rows.Count
           && _rows[_lv.SelectedIndices[0]].Tag is ServiceItem s ? s : null;

    private void ShowDetail(object? sender, EventArgs e)
    {
        if (FindSelected() is not { } item) return;
        var parts = new List<string>();
        if (item.Address.Length > 0) parts.Add(item.Address);
        if (item.Tel.Length > 0) parts.Add("tel " + item.Tel);
        if (item.Email.Length > 0) parts.Add(item.Email);
        if (item.Web.Length > 0) parts.Add(item.Web);
        if (item.Times.Length > 0) parts.Add("hours: " + Jx.StripHtml(item.Times));
        if (item.Cost.Length > 0) parts.Add("cost: " + item.Cost);
        if (item.Notes.Length > 0) parts.Add("\r\n" + Jx.StripHtml(item.Notes));
        _detail.Text = parts.Count > 0 ? string.Join("\r\n", parts) : "no further details — double-click opens the website when one is listed";
    }

    public void Bind(RockfaxClient api) => _api = api;

    /// <summary>Fetches the directory once per run (Refresh re-fetches on demand).</summary>
    public async Task LoadAsync(bool force = false)
    {
        if (_api is null) return;
        if (!force && _cache is not null)
        {
            _all = _cache;
            if (_rows.Count == 0) ApplyFilter();
            return;
        }
        _lblStatus.Text = "loading the services directory…";
        try
        {
            using JsonDocument doc = await _api.GetFreeListingsAsync();
            _cache = ParseListings(doc);
            _all = _cache;
            ApplyFilter();
            _lblStatus.Text = "";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "listings unavailable: " + ex.Message;
        }
    }

    private void ApplyFilter()
    {
        string q = _filter.Text.Trim();
        _rows.Clear();
        foreach (ServiceItem s in _all)
        {
            if (_typeFilter.Length > 0 && s.Type != _typeFilter) continue;
            if (q.Length > 0
                && !s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !s.Address.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var item = new ListViewItem(s.Name.Length > 0 ? s.Name : "(unnamed)");
            item.SubItems.Add(TypeLabel(s.Type));
            item.SubItems.Add(s.Address);
            item.SubItems.Add(s.Tel);
            item.SubItems.Add(s.Web);
            item.Tag = s;
            _rows.Add(item);
        }
        _lv.VirtualListSize = _rows.Count;
        _detail.Clear();
        var counts = Types.Select(t => (t.Label, _all.Count(x => x.Type == t.Key && IncludeInFilter(x))))
                          .Where(t => t.Item2 > 0).Select(t => $"{t.Label} {t.Item2}");
        _lblCount.Text = _rows.Count == _all.Count
            ? $"{_all.Count:N0} listings — {string.Join(" · ", counts)}"
            : $"{_rows.Count:N0} of {_all.Count:N0} listings — double-click opens the website";
        _lv.StretchLastColumn();
    }

    private bool IncludeInFilter(ServiceItem s)
    {
        string q = _filter.Text.Trim();
        return q.Length == 0 || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || s.Address.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    internal static string TypeLabel(string type) => type switch
    {
        "shop" => "shop", "goods" => "gear", "learn" => "courses",
        "club" => "club", "wall" => "wall", "stay" => "stay",
        _ => type,
    };

    /// <summary>Parses { "items": { "shop_2": {...} } } — url/tel arrive as HTML links
    /// with *_raw plain-text twins; the raw fields win when present.</summary>
    internal static List<ServiceItem> ParseListings(JsonDocument doc)
    {
        var result = new List<ServiceItem>();
        JsonElement items = doc.RootElement.TryGetProperty("items", out JsonElement it) && it.ValueKind == JsonValueKind.Object
            ? it
            : doc.RootElement;
        foreach (JsonProperty p in items.EnumerateObject())
        {
            JsonElement e = p.Value;
            if (e.ValueKind != JsonValueKind.Object) continue;
            string type = e.Str("type");
            if (type.Length == 0)
            {
                int cut = p.Name.IndexOf('_');
                type = cut > 0 ? p.Name[..cut] : p.Name;
            }
            string web = e.Str("url_raw");
            if (web.Length == 0) web = e.Str("urlRaw");
            if (web.Length == 0) web = Jx.StripHtml(e.Str("url"));
            string tel = e.Str("tel_raw");
            if (tel.Length == 0) tel = Jx.StripHtml(e.Str("tel"));
            result.Add(new ServiceItem(type, e.Str("name"), e.Str("address"), tel, e.Str("email"),
                                       web, e.Str("notes"), e.Str("times"), e.Str("cost")));
        }
        return result;
    }
}
