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

    public event Action<RouteSummary>? RouteRequested;

    public LogbookView()
    {
        BackColor = Ui.Bg;

        _lvAscents.Columns.Add("Date", 92);
        _lvAscents.Columns.Add("Route", 250);
        _lvAscents.Columns.Add("Grade", 70);
        _lvAscents.Columns.Add("Crag", 210);
        _lvAscents.Columns.Add("Style", 200);
        _lvAscents.Columns.Add("Notes", 420);
        _lvAscents.ColumnInk[2] = Ui.Amber;
        _lvAscents.ColumnInk[4] = Ui.Accent;
        _lvWishlist.Columns.Add("Route", 280);
        _lvWishlist.Columns.Add("Crag", 240);

        _lvAscents.DoubleClick += (_, _) =>
        {
            if (_lvAscents.SelectedItems.Count > 0 && _lvAscents.SelectedItems[0].Tag is RouteSummary r)
                RouteRequested?.Invoke(r);
        };

        var titleRow = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Ui.Bg, Padding = new Padding(10, 4, 0, 0) };
        titleRow.Controls.Add(Ui.Label("YOUR LOGBOOK", Ui.Text, Ui.H2));

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

        var empty = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Sign in (top right) with your UKClimbing account to browse\nyour ascents and wishlist.",
            ForeColor = Ui.Muted,
            Font = Ui.H2,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Ui.Bg,
        };
        Controls.Add(empty);
        Controls.Add(_split);
        Controls.Add(titleRow);
        _split.Visible = false;
        Tag = empty; // revealed by LoadAsync
    }

    public async Task LoadAsync(RockfaxClient api)
    {
        if (!api.IsLoggedIn)
        {
            _lblStatus.Text = "sign in first (top-right)";
            return;
        }

        _lblStatus.Text = $"loading logbook for {api.Username}…";
        _lvAscents.Items.Clear();
        _lvWishlist.Items.Clear();
        try
        {
            using JsonDocument doc = await api.GetLogbookAsync(api.UserId);
            int total = 0, deleted = 0;
            foreach (JsonElement ascent in FindAscents(doc.RootElement))
            {
                if (ascent.Int("trash") == 1) { deleted++; continue; }
                var item = new ListViewItem(ascent.Str("textAscentDate"));
                item.SubItems.Add(ascent.Str("name"));
                item.SubItems.Add(ascent.Str("grade"));
                item.SubItems.Add(ascent.Str("crag"));
                item.SubItems.Add(Jx.StyleName(ascent.Int("style")));
                item.SubItems.Add(ascent.Str("comment"));
                item.Tag = new RouteSummary(ascent.Str("name"), ascent.Str("grade"), "", 0, ascent.Str("crag"),
                                            ascent.Int("ukcID"), ascent.Int("rockfaxID"), 0);
                _lvAscents.Items.Add(item);
                total++;
            }
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
            int count = 0;
            foreach (JsonElement entry in FindWishlist(doc.RootElement))
            {
                var item = new ListViewItem(entry.Str("name"));
                item.SubItems.Add(entry.Str("crag"));
                _lvWishlist.Items.Add(item);
                count++;
            }
            _lblWishlist.Text = $"▍ WISHLIST ({count})";
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
