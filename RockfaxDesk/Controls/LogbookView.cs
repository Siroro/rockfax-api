using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>Your ascents and wishlist, loaded after login.</summary>
internal sealed class LogbookView : UserControl
{
    private readonly Label _lblTitle = new()
    {
        Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI Semibold", 13f),
        ForeColor = Color.FromArgb(235, 245, 255), Padding = new Padding(8, 4, 0, 0), Text = "Logbook",
    };
    private readonly ListView _lvAscents = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(24, 32, 44), ForeColor = Color.Gainsboro,
    };
    private readonly ListView _lvWishlist = new()
    {
        Dock = DockStyle.Bottom, Height = 160, View = View.Details, FullRowSelect = true, HideSelection = false,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(24, 32, 44), ForeColor = Color.Gainsboro,
    };
    private readonly Label _lblWishlist = new()
    {
        Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.FromArgb(150, 200, 240),
        Font = new Font("Segoe UI Semibold", 9.5f), Padding = new Padding(8, 0, 0, 0), Text = "Wishlist",
    };
    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Top, Height = 20, ForeColor = Color.FromArgb(255, 170, 60),
        Font = new Font("Segoe UI", 9f), Padding = new Padding(8, 0, 0, 0), Text = "log in to load your logbook",
    };

    public event Action<RouteSummary>? RouteRequested;

    public LogbookView()
    {
        BackColor = Color.FromArgb(24, 32, 44);
        _lvAscents.Columns.Add("Date", 90);
        _lvAscents.Columns.Add("Route", 240);
        _lvAscents.Columns.Add("Grade", 70);
        _lvAscents.Columns.Add("Crag", 200);
        _lvAscents.Columns.Add("Style", 190);
        _lvAscents.Columns.Add("Notes", 320);
        _lvWishlist.Columns.Add("Route", 260);
        _lvWishlist.Columns.Add("Crag", 220);
        _lvAscents.DoubleClick += (_, _) =>
        {
            if (_lvAscents.SelectedItems.Count > 0 && _lvAscents.SelectedItems[0].Tag is RouteSummary r)
                RouteRequested?.Invoke(r);
        };

        Controls.Add(_lvAscents);
        Controls.Add(_lvWishlist);
        Controls.Add(_lblWishlist);
        Controls.Add(_lblStatus);
        Controls.Add(_lblTitle);
    }

    public async Task LoadAsync(RockfaxClient api)
    {
        if (!api.IsLoggedIn)
        {
            _lblStatus.Text = "log in first (top-left)";
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
            _lblStatus.Text = $"{total} ascents" + (deleted > 0 ? $" ({deleted} deleted entries skipped)" : "");
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
            _lblWishlist.Text = $"Wishlist ({count})";
        }
        catch
        {
            _lblWishlist.Text = "Wishlist unavailable";
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
