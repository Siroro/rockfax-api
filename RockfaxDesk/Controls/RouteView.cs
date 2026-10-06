using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>Everything about one route: header chips, description/FA, comments, photo strip.</summary>
internal sealed class RouteView : UserControl
{
    private readonly SplitContainer _split;
    private readonly Label _empty;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ui.Bg };

    private readonly Label _lblTitle = Ui.Label("", Ui.Text, Ui.H1);
    private readonly FlowLayoutPanel _chips = new()
    {
        Dock = DockStyle.Top, Height = 34, BackColor = Ui.Bg, Padding = new Padding(12, 4, 0, 0), WrapContents = false,
    };
    private readonly Button _lnkCrag = Ui.Button("open crag", 150);
    private readonly Button _lnkUkc = Ui.Button("on UKC \u2197", 92);
    private readonly UiList _lvComments = new();
    private readonly TextBox _txtDescription = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Ui.Panel, ForeColor = Ui.Text,
        Font = Ui.Body,
    };
    private readonly FlowLayoutPanel _photos = NewPhotoStrip();
    private readonly Label _lblPhotoNote = Ui.Label("", Ui.Muted, Ui.Tiny);
    private readonly Label _lblStatus = Ui.Label("", Ui.Amber, Ui.Small);

    private RockfaxClient? _api;
    private ImageFetcher? _images;

    public event Action<int, string>? CragRequested;

    /// <summary>Single-row gallery: wheel scrolls sideways, like a filmstrip.</summary>
    internal static FlowLayoutPanel NewPhotoStrip()
    {
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 146, AutoScroll = true, BackColor = Ui.BgDeep,
            Padding = new Padding(8), WrapContents = false,
        };
        DarkScroll.Apply(strip);
        strip.MouseWheel += (_, e) =>
        {
            if (!strip.HorizontalScroll.Visible) return;
            int delta = e.Delta > 0 ? -120 : 120;
            int target = Math.Clamp(strip.HorizontalScroll.Value + delta,
                strip.HorizontalScroll.Minimum, strip.HorizontalScroll.Maximum);
            strip.HorizontalScroll.Value = target;
            strip.PerformLayout();
            ((HandledMouseEventArgs)e).Handled = true;
        };
        return strip;
    }

    public RouteView()
    {
        BackColor = Ui.Bg;

        _empty = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Search a route on the left — try “Stanage” — and it opens here.\n\nDescription · first ascent · comments · photos",
            ForeColor = Ui.Muted,
            Font = Ui.H2,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Ui.Bg,
        };

        _lvComments.Columns.Add("Date", 86);
        _lvComments.Columns.Add("Who", 140);
        _lvComments.Columns.Add("Comment", 900);
        _lvComments.ColumnInk[1] = Ui.Accent;

        var descHost = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Border, Padding = new Padding(1) };
        descHost.Controls.Add(_txtDescription);

        var topSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Ui.Bg, SplitterWidth = 5 };
        topSplit.Panel1.BackColor = Ui.Bg;
        topSplit.Panel2.BackColor = Ui.Bg;
        topSplit.Panel1.Controls.Add(descHost);
        topSplit.Panel1.Controls.Add(HeaderBlock());
        topSplit.Panel2.Controls.Add(_lvComments);
        topSplit.Panel2.Controls.Add(_commentsHeader);

        _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Ui.Bg, SplitterWidth = 5 };
        _split.Panel1.BackColor = Ui.Bg;
        _split.Panel2.BackColor = Ui.Bg;
        _split.Panel1.Controls.Add(topSplit);
        _split.Panel2.Controls.Add(_photos);
        _split.Panel2.Controls.Add(_lblPhotoNote);
        _split.SplitterDistance = 250;

        _lblStatus.Dock = DockStyle.Bottom;
        _lblStatus.AutoSize = false;
        _lblStatus.Height = 22;
        _lblStatus.BackColor = Ui.BgDeep;
        _lblStatus.Padding = new Padding(10, 2, 0, 0);
        _content.Controls.Add(_split);
        _content.Controls.Add(_lblStatus);

        Controls.Add(_empty);
        Controls.Add(_content);
        _content.Visible = false;

        _lnkCrag.Click += (_, _) =>
        {
            if (Tag is RouteSummary r && r.CragUkcId > 0) CragRequested?.Invoke(r.CragUkcId, r.CragName);
        };
        _lnkUkc.Click += (_, _) =>
        {
            if (Tag is RouteSummary r && r.CragUkcId > 0)
                Jx.OpenBrowser($"https://www.ukclimbing.com/logbook/crag.php?id={r.CragUkcId}");
        };
    }

    private Panel HeaderBlock()
    {
        var head = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Ui.Bg, Padding = Padding.Empty };
        var linkRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, BackColor = Ui.Bg, WrapContents = false, Padding = new Padding(10, 0, 0, 0) };
        linkRow.Controls.Add(_lnkCrag);
        linkRow.Controls.Add(_lnkUkc);
        var titleRow = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Ui.Bg, Padding = new Padding(10, 4, 0, 0) };
        titleRow.Controls.Add(_lblTitle);
        head.Controls.Add(linkRow);
        head.Controls.Add(titleRow);
        head.Controls.Add(_chips);
        return head;
    }

    private readonly Label _commentsHeader = Ui.SectionHeader("COMMENTS", 26);

    public void Bind(RockfaxClient api, ImageFetcher images)
    {
        _api = api;
        _images = images;
    }

    public async Task ShowRouteAsync(RouteSummary route)
    {
        if (_api is null || _images is null) return;
        Tag = route;
        _empty.Visible = false;
        _content.Visible = true;

        _lblTitle.Text = route.Name;
        BuildChips(route);
        _lnkCrag.Text = route.CragName.Length > 0 ? $"▸ {route.CragName}" : "crag n/a";
        _txtDescription.Clear();
        _lvComments.Items.Clear();
        ClearPhotos();
        _lblPhotoNote.Text = "";
        _lblStatus.Text = "loading route…";

        try
        {
            RouteInfo info = await _api.GetRouteInfoAsync(route.UkcId);
            var meta = new List<string>();
            if (info.FirstAscent.Length > 0)
                meta.Add($"First ascent: {info.FirstAscent} {info.FirstAscentDate}".TrimEnd());
            if (info.Height > 0) meta.Add($"Height: {info.Height} m");
            if (info.Pitches > 0) meta.Add($"Pitches: {info.Pitches}");
            _txtDescription.Text =
                (meta.Count > 0 ? string.Join("    ", meta) + "\r\n\r\n" : "") +
                (info.Description.Length > 0 ? info.Description + "\r\n" : "(no description on UKC)\r\n") +
                (info.RockfaxDescription.Length > 0 ? "\r\n— Rockfax —\r\n" + info.RockfaxDescription : "");
        }
        catch (Exception ex)
        {
            _txtDescription.Text = "description unavailable: " + ex.Message;
        }

        _lblStatus.Text = "loading comments…";
        try
        {
            using JsonDocument doc = await _api.GetRouteCommentsAsync(new[] { route.UkcId });
            var comments = ParseComments(doc).ToList();
            foreach ((string who, string date, string text) in comments)
            {
                var item = new ListViewItem(date) { UseItemStyleForSubItems = false };
                item.SubItems.Add(who);
                item.SubItems.Add(text);
                _lvComments.Items.Add(item);
            }
            if (comments.Count == 0)
                _lvComments.Items.Add(new ListViewItem("—") { SubItems = { "", "no comments yet" } });
            _commentsHeader.Text = $"▍ COMMENTS ({comments.Count})";
            _lvComments.StretchLastColumn();
        }
        catch (Exception ex)
        {
            _lvComments.Items.Add(new ListViewItem("—") { SubItems = { "", "comments unavailable: " + ex.Message } });
        }

        _lblStatus.Text = "loading photos…";
        int shown = 0;
        try
        {
            using JsonDocument doc = await _api.GetRoutePhotosAsync(new[] { route.UkcId });
            foreach ((int id, string title, string author) in ParsePhotos(doc))
            {
                Image? thumb = await _images.GetThumbAsync(id);
                if (thumb is null) continue;
                _photos.Controls.Add(PhotoCard(_images, id, title, author, thumb));
                shown++;
            }
        }
        catch (Exception ex)
        {
            _lblPhotoNote.Text = "photos unavailable: " + ex.Message;
        }
        if (_lblPhotoNote.Text.Length == 0)
            _lblPhotoNote.Text = shown > 0 ? $"  {shown} photos — click to view full size" : "  no photos for this route";
        _lblStatus.Text = "";
    }

    private void BuildChips(RouteSummary route)
    {
        _chips.Controls.Clear();
        if (route.Grade.Length > 0) _chips.Controls.Add(Ui.Chip(route.Grade, Ui.Amber));
        if (route.TechGrade.Length > 0) _chips.Controls.Add(Ui.Chip(route.TechGrade, Ui.Muted));
        if (route.Stars > 0) _chips.Controls.Add(Ui.Chip(Jx.Stars(route.Stars), Ui.Green));
        _chips.Controls.Add(Ui.Chip($"UKC #{route.UkcId}", Ui.Muted));
    }

    internal static Panel PhotoCard(ImageFetcher images, int id, string title, string author, Image thumb)
    {
        var card = new Panel
        {
            Size = new Size(126, 126), Margin = new Padding(4), BackColor = Ui.Card,
            Padding = new Padding(3), Cursor = Cursors.Hand, Tag = (id, title, author),
        };
        var box = new PictureBox { Image = thumb, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.BgDeep };
        card.Controls.Add(box);
        int photoId = id;
        card.Click += async (_, _) => await PhotoDialog.ShowFromStripAsync(images, card.Parent!, photoId);
        box.Click += async (_, _) => await PhotoDialog.ShowFromStripAsync(images, card.Parent!, photoId);
        card.MouseEnter += (_, _) => card.BackColor = Ui.AccentDim;
        card.MouseLeave += (_, _) => card.BackColor = Ui.Card;
        return card;
    }

    private void ClearPhotos()
    {
        foreach (Control c in _photos.Controls)
            if (c is Panel { Controls.Count: > 0 } && c.Controls[0] is PictureBox pb)
                pb.Image?.Dispose();
        _photos.Controls.Clear();
    }

    internal static List<(string Who, string Date, string Text)> ParseComments(JsonDocument doc)
    {
        var result = new List<(string, string, string)>();
        foreach (JsonElement group in doc.RootElement.EnumerateArrayOrObjectValues())
        {
            foreach (JsonElement c in group.EnumerateArrayOrObjectValues())
            {
                if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("comment", out _)) continue;
                result.Add((c.Str("name"), Jx.DateFromUnix(c.Long("date")), c.Str("comment")));
            }
        }
        return result;
    }

    internal static List<(int Id, string Title, string Author)> ParsePhotos(JsonDocument doc)
    {
        var result = new List<(int, string, string)>();
        foreach (JsonElement group in doc.RootElement.EnumerateArrayOrObjectValues())
        {
            JsonElement photos = group.ValueKind == JsonValueKind.Object && group.TryGetProperty("photos", out JsonElement p)
                ? p
                : group;
            if (photos.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement photo in photos.EnumerateArray())
            {
                int id = photo.Int("id");
                if (id > 0) result.Add((id, photo.Str("title"), photo.Str("author")));
            }
        }
        return result;
    }
}
