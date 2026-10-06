using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>Your ascents and wishlist, loaded after login.</summary>
internal sealed class LogbookView : UserControl
{
    private readonly UiList _lvAscents = new();
    private readonly UiList _lvWishlist = new();
    private readonly Label _lblStatus = Ui.Label("log in to load your logbook", Ui.Muted, Ui.Small);
    private readonly Label _lblWishlist = Ui.SectionHeader("WISHLIST", 26);
    private readonly SplitContainer _split;
    private readonly TextBox _filter = new();
    private readonly Panel _filterBox;
    private readonly Label _empty;

    public event Action<RouteSummary>? RouteRequested;

    private readonly List<(ListViewItem Item, List<int> Pids)> _allAscents = new();
    private readonly Dictionary<int, string> _partnerNames = new();
    private ComboBox? _partnerJump;
    private readonly List<int> _jumpPids = new();
    private int? _partnerFilterPid;

    private void ApplyFilter()
    {
        string q = _filter.Text.Trim();
        _lvAscents.BeginUpdate();
        _lvAscents.Items.Clear();
        int shown = 0;
        foreach ((ListViewItem item, List<int> pids) in _allAscents)
        {
            if (_partnerFilterPid is int pid && !pids.Contains(pid)) continue;
            if (q.Length == 0
                || item.Text.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.SubItems.Count > 1 && item.SubItems[1].Text.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.SubItems.Count > 3 && item.SubItems[3].Text.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                _lvAscents.Items.Add(item);
                shown++;
            }
        }
        _lvAscents.EndUpdate();
        if (_partnerFilterPid is int active)
        {
            string name = _partnerNames.TryGetValue(active, out string? n) ? n : $"#{active}";
            _lblStatus.Text = $"{shown} ascents with {name}";
        }
    }

    public LogbookView()
    {
        BackColor = Ui.Bg;

        _lvAscents.Columns.Add("Date", 92);
        _lvAscents.Columns.Add("Route", 240);
        _lvAscents.Columns.Add("Grade", 66);
        _lvAscents.Columns.Add("Crag", 180);
        _lvAscents.Columns.Add("Style", 150);
        _lvAscents.Columns.Add("Partners", 170);
        _lvAscents.Columns.Add("Notes", 330);
        _lvAscents.ColumnInk[2] = Ui.Amber;
        _lvAscents.ColumnInk[4] = Ui.Accent;
        _lvWishlist.Columns.Add("Route", 280);
        _lvWishlist.Columns.Add("Grade", 66);
        _lvWishlist.Columns.Add("Crag", 200);
        _lvWishlist.Columns.Add("UKC id", 76);
        _lvWishlist.ColumnInk[1] = Ui.Amber;
        _lvWishlist.DoubleClick += (_, _) =>
        {
            if (_lvWishlist.SelectedItems.Count > 0 && _lvWishlist.SelectedItems[0].Tag is RouteSummary r)
                RouteRequested?.Invoke(r);
        };

        _lvAscents.DoubleClick += (_, _) =>
        {
            if (_lvAscents.SelectedItems.Count > 0 && _lvAscents.SelectedItems[0].Tag is RouteSummary r)
                RouteRequested?.Invoke(r);
        };

        var titleRow = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Ui.Bg, Padding = new Padding(10, 6, 10, 0) };
        var export = Ui.Button("Export CSV", 100);
        export.Dock = DockStyle.Right;
        export.Click += async (_, _) => await ExportCsvAsync();
        titleRow.Controls.Add(export);
        _partnerJump = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Ui.BgDeep,
            ForeColor = Ui.Text,
            Font = Ui.Small,
            Width = 180,
            Visible = false,
        };
        _partnerJump.SelectedIndexChanged += (_, _) =>
        {
            int idx = _partnerJump.SelectedIndex;
            _partnerFilterPid = idx > 0 && idx - 1 < _jumpPids.Count ? _jumpPids[idx - 1] : null;
            ApplyFilter();
        };
        // Added before the filter box so it docks to its right (last-added docks first).
        titleRow.Controls.Add(_partnerJump);
        _filterBox = Ui.Box(_filter, 230, 30, cue: "filter ascents…");
        _filterBox.Dock = DockStyle.Left;
        _filterBox.Visible = false;
        _filter.TextChanged += (_, _) => ApplyFilter();
        var title = Ui.Label("YOUR LOGBOOK", Ui.Text, Ui.H2);
        title.Location = new Point(12, 8);
        titleRow.Controls.Add(_filterBox);
        titleRow.Controls.Add(title);

        _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Ui.Bg, SplitterWidth = 5 };
        _split.Panel1.BackColor = Ui.Bg;
        _split.Panel2.BackColor = Ui.Bg;
        _lblStatus.Dock = DockStyle.Bottom;
        _lblStatus.AutoSize = false;
        _lblStatus.Height = 22;
        _lblStatus.BackColor = Ui.BgDeep;
        _lblStatus.Padding = new Padding(10, 2, 0, 0);
        _split.Panel1.Controls.Add(_lvAscents);
        _split.Panel1.Controls.Add(_lblStatus);
        _split.Panel2.Controls.Add(_lvWishlist);
        _split.Panel2.Controls.Add(_lblWishlist);
        _split.SplitterDistance = 420;

        _empty = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Sign in (top right) with your UKClimbing account to browse\nyour ascents and wishlist.",
            ForeColor = Ui.Muted,
            Font = Ui.H2,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Ui.Bg,
        };
        Controls.Add(_empty);
        Controls.Add(_split);
        Controls.Add(titleRow);
        _split.Visible = false;
    }

    private async Task ExportCsvAsync()
    {
        if (_allAscents.Count == 0) { _lblStatus.Text = "load your logbook first"; return; }
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"ukc-logbook-{DateTime.Today:yyyy-MM-dd}.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var lines = new List<string> { "date,route,grade,crag,style,partners,notes" };
            foreach (ListViewItem item in _lvAscents.Items)
            {
                string[] cols = item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text).ToArray();
                string csv = string.Join(",", cols.Select(CsvField));
                lines.Add(csv);
            }
            await File.WriteAllLinesAsync(dialog.FileName, lines);
            _lblStatus.Text = $"exported {_lvAscents.Items.Count} ascents to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"export failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>One RFC-4180 field: always quoted, embedded quotes doubled.</summary>
    internal static string CsvField(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    public async Task LoadAsync(RockfaxClient api)
    {
        if (!api.IsLoggedIn)
        {
            _lblStatus.Text = "sign in first (top-right)";
            return;
        }

        _lblStatus.Text = $"loading logbook for {api.Username}…";
        _allAscents.Clear();
        _partnerNames.Clear();
        _jumpPids.Clear();
        _partnerFilterPid = null;
        _lvAscents.Items.Clear();
        _lvWishlist.Items.Clear();

        // Partner ids in ascents resolve against the user's partners list.
        try
        {
            using JsonDocument partnersDoc = await api.GetPartnersAsync(api.UserId);
            // Response shape: { "partners": [ { id, name, userID, ... } ] }
            JsonElement container =
                partnersDoc.RootElement.TryGetProperty("partners", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr
                    : partnersDoc.RootElement;
            foreach (JsonElement partner in container.EnumerateArrayOrObjectValues())
            {
                if (partner.ValueKind != JsonValueKind.Object) continue;
                int pid = partner.Int("id");
                string name = partner.Str("name");
                if (pid > 0 && name.Length > 0) _partnerNames[pid] = name;
            }
        }
        catch { /* partners are decoration; ascents still load */ }
        try
        {
            using JsonDocument doc = await api.GetLogbookAsync(api.UserId);
            int total = 0, deleted = 0;
            foreach (JsonElement ascent in FindAscents(doc.RootElement))
            {
                if (ascent.Int("trash") == 1) { deleted++; continue; }
                List<int> pids = ascent.IntList("partners");
                string partners = string.Join(", ", pids.Select(pid =>
                    _partnerNames.TryGetValue(pid, out string? name) ? name : $"#{pid}"));
                var item = new ListViewItem(ascent.Str("textAscentDate"));
                item.SubItems.Add(ascent.Str("name"));
                item.SubItems.Add(ascent.Str("grade"));
                item.SubItems.Add(ascent.Str("crag"));
                item.SubItems.Add(Jx.StyleName(ascent.Int("style")));
                item.SubItems.Add(partners);
                item.SubItems.Add(ascent.Str("comment"));
                item.Tag = new RouteSummary(ascent.Str("name"), ascent.Str("grade"), "", 0, ascent.Str("crag"),
                                            ascent.Int("ukcID"), ascent.Int("rockfaxID"), 0);
                _allAscents.Add((item, pids));
                total++;
            }
            // Partner dropdown: only partners that appear on at least one ascent, sorted.
            if (_partnerJump is { } jump)
            {
                _jumpPids.Clear();
                jump.Items.Clear();
                jump.Items.Add("all partners…");
                foreach (int pid in _allAscents.SelectMany(a => a.Pids).Distinct().OrderBy(pid =>
                             _partnerNames.TryGetValue(pid, out string? n) ? n : $"#{pid}", StringComparer.OrdinalIgnoreCase))
                {
                    _jumpPids.Add(pid);
                    jump.Items.Add(_partnerNames.TryGetValue(pid, out string? n) ? n : $"#{pid}");
                }
                jump.SelectedIndex = _jumpPids.Count > 0 ? 0 : -1;
                jump.Visible = _jumpPids.Count > 0;
            }
            ApplyFilter();
            _lblStatus.Text = $"{total} ascents" + (deleted > 0 ? $"  ·  {deleted} deleted entries skipped" : "") + "  ·  double-click to open the route";
            _lvAscents.StretchLastColumn();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "logbook unavailable: " + ex.Message;
        }

        try
        {
            using JsonDocument doc = await api.GetWishlistAsync(api.UserId);
            int count = 0, deleted = 0;
            foreach (JsonElement entry in FindWishlist(doc.RootElement))
            {
                if (entry.Int("trash") == 1) { deleted++; continue; }
                var item = new ListViewItem(entry.Str("name"));
                item.SubItems.Add(entry.Str("grade"));
                item.SubItems.Add(entry.Str("crag"));
                item.SubItems.Add(entry.Int("ukcID").ToString());
                item.Tag = new RouteSummary(entry.Str("name"), entry.Str("grade"), "", 0,
                                            entry.Str("crag"), entry.Int("ukcID"), entry.Int("rockfaxID"), 0);
                _lvWishlist.Items.Add(item);
                count++;
            }
            _lblWishlist.Text = $"▍ WISHLIST ({count})" +
                (deleted > 0 ? $" · {deleted} deleted skipped" : "") +
                "  ·  double-click to open the route";
            _lvWishlist.StretchLastColumn();
        }
        catch
        {
            _lblWishlist.Text = "▍ WISHLIST unavailable";
        }
    }

    /// <summary>Ascents may come keyed by id or in an array — collect any object that looks like an ascent.</summary>
    internal static IEnumerable<JsonElement> FindAscents(JsonElement root)
    {
        foreach (JsonElement e in root.EnumerateArrayOrObjectValues())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (e.TryGetProperty("textAscentDate", out _)) yield return e;
        }
    }

    internal static IEnumerable<JsonElement> FindWishlist(JsonElement root)
    {
        foreach (JsonElement e in root.EnumerateArrayOrObjectValues())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (e.TryGetProperty("name", out _) || e.TryGetProperty("route", out _)) yield return e;
        }
    }
}
