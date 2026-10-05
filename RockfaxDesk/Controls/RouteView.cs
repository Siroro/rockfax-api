using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>Everything about one route: header, description/FA, comments, photo strip.</summary>
internal sealed class RouteView : UserControl
{
    private readonly Label _lblTitle = new()
    {
        Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI Semibold", 15f),
        ForeColor = Color.FromArgb(235, 245, 255), Padding = new Padding(8, 4, 0, 0),
    };
    private readonly Label _lblSub = new()
    {
        Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI", 9.75f),
        ForeColor = Color.FromArgb(150, 200, 240), Padding = new Padding(10, 0, 0, 0),
    };
    private readonly LinkLabel _lnkCrag = new()
    {
        Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9.75f),
        ForeColor = Color.FromArgb(120, 180, 255), Padding = new Padding(10, 0, 0, 0),
    };

    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 220 };
    private readonly TextBox _txtDescription = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(24, 32, 44), ForeColor = Color.Gainsboro,
        Font = new Font("Segoe UI", 10f), Padding = new Padding(8), Margin = new Padding(8),
    };
    private readonly ListView _lvComments = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(24, 32, 44), ForeColor = Color.Gainsboro,
    };
    private readonly FlowLayoutPanel _photos = new()
    {
        Dock = DockStyle.Bottom, Height = 128, AutoScroll = true, BackColor = Color.FromArgb(18, 26, 38),
        Padding = new Padding(6), WrapContents = false,
    };
    private readonly Label _lblPhotoNote = new()
    {
        Dock = DockStyle.Bottom, Height = 18, ForeColor = Color.DimGray,
        Font = new Font("Segoe UI", 8.5f), Padding = new Padding(8, 0, 0, 0), Text = "",
    };
    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Bottom, Height = 18, ForeColor = Color.FromArgb(255, 170, 60),
        Font = new Font("Segoe UI", 8.5f), Padding = new Padding(8, 0, 0, 0),
    };

    private RockfaxClient? _api;
    private ImageFetcher? _images;

    public event Action<int, string>? CragRequested;

    public RouteView()
    {
        BackColor = Color.FromArgb(24, 32, 44);
        _lvComments.Columns.Add("Date", 80);
        _lvComments.Columns.Add("Who", 130);
        _lvComments.Columns.Add("Comment", 650);

        var topSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 160 };
        topSplit.Panel1.Controls.Add(_txtDescription);
        topSplit.Panel2.Controls.Add(_lvComments);
        _split.Panel1.Controls.Add(topSplit);
        _split.Panel2.Controls.Add(_photos);
        _split.Panel2.Controls.Add(_lblPhotoNote);

        Controls.Add(_split);
        Controls.Add(_lblStatus);
        Controls.Add(_lnkCrag);
        Controls.Add(_lblSub);
        Controls.Add(_lblTitle);

        _lnkCrag.LinkClicked += (_, _) =>
        {
            if (Tag is RouteSummary r && r.CragUkcId > 0) CragRequested?.Invoke(r.CragUkcId, r.CragName);
        };
    }

    public void Bind(RockfaxClient api, ImageFetcher images)
    {
        _api = api;
        _images = images;
    }

    public async Task ShowRouteAsync(RouteSummary route)
    {
        if (_api is null || _images is null) return;
        Tag = route;
        _lblTitle.Text = $"{route.Name}   {route.Grade}{(route.TechGrade.Length > 0 ? " " + route.TechGrade : "")}   {Jx.Stars(route.Stars)}";
        _lblSub.Text = $"UKC route {route.UkcId} · crag {route.CragUkcId}";
        _lnkCrag.Text = route.CragName.Length > 0 ? $"▸ open crag: {route.CragName}" : "";
        _txtDescription.Clear();
        _lvComments.Items.Clear();
        ClearPhotos();
        _lblStatus.Text = "loading route…";

        try
        {
            RouteInfo info = await _api.GetRouteInfoAsync(route.UkcId);
            _txtDescription.Text =
                $"First ascent: {info.FirstAscent} {info.FirstAscentDate}\r\n" +
                $"Height: {info.Height} m    Pitches: {info.Pitches}\r\n\r\n" +
                (info.Description.Length > 0 ? info.Description + "\r\n" : "") +
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
            foreach ((string who, string date, string text) in ParseComments(doc))
            {
                var item = new ListViewItem(date) { UseItemStyleForSubItems = false };
                item.SubItems.Add(who);
                item.SubItems.Add(text);
                _lvComments.Items.Add(item);
            }
            if (_lvComments.Items.Count == 0) _lvComments.Items.Add(new ListViewItem("") { SubItems = { "", "no comments yet" } });
        }
        catch (Exception ex)
        {
            _lvComments.Items.Add(new ListViewItem("") { SubItems = { "", "comments unavailable: " + ex.Message } });
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
                var box = new PictureBox
                {
                    Image = thumb, Size = new Size(118, 118), SizeMode = PictureBoxSizeMode.Zoom,
                    Tag = (id, title, author), Padding = new Padding(2), Cursor = Cursors.Hand,
                };
                box.Click += async (_, _) => await PhotoDialog.ShowAsync(_images, id, title, author);
                _photos.Controls.Add(box);
                shown++;
            }
        }
        catch (Exception ex)
        {
            _lblPhotoNote.Text = "photos unavailable: " + ex.Message;
        }
        _lblPhotoNote.Text = shown > 0 ? $"{shown} photos — click to view full size" : _lblPhotoNote.Text;
        _lblStatus.Text = "";
    }

    private void ClearPhotos()
    {
        foreach (Control c in _photos.Controls) if (c is PictureBox pb) pb.Image?.Dispose();
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
