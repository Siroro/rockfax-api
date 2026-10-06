using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>The weekly top-ten photos as a clickable card grid.</summary>
internal sealed class Top10View : UserControl
{
    private readonly FlowLayoutPanel _grid = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg, Padding = new Padding(12),
    };
    private readonly Label _lblStatus = Ui.Label("", Ui.Amber, Ui.Small);

    private RockfaxClient? _api;
    private ImageFetcher? _images;

    public Top10View()
    {
        BackColor = Ui.Bg;
        var titleRow = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Ui.Bg, Padding = new Padding(10, 4, 0, 0) };
        titleRow.Controls.Add(Ui.Label("WEEKLY TOP 10 PHOTOS", Ui.Text, Ui.H2));
        _lblStatus.Dock = DockStyle.Top;
        _lblStatus.Height = 20;
        _lblStatus.Padding = new Padding(12, 0, 0, 0);
        Controls.Add(_grid);
        Controls.Add(_lblStatus);
        Controls.Add(titleRow);
    }

    public void Bind(RockfaxClient api, ImageFetcher images)
    {
        _api = api;
        _images = images;
    }

    public async Task LoadAsync()
    {
        if (_api is null || _images is null) return;
        _lblStatus.Text = "loading…";
        foreach (Control c in _grid.Controls)
            if (c is Panel { Controls.Count: > 0 } && c.Controls[0] is PictureBox pb)
                pb.Image?.Dispose();
        _grid.Controls.Clear();

        try
        {
            using JsonDocument doc = await _api.GetWeeklyTopTenPhotosAsync();
            int rank = 0;
            foreach (JsonElement photo in doc.RootElement.GetProperty("photos").EnumerateArray())
            {
                int id = photo.Int("id");
                string title = photo.Str("title"), author = photo.Str("author");
                Image? thumb = await _images.GetThumbAsync(id);
                if (thumb is null) continue;
                rank++;
                _grid.Controls.Add(Card(_images, rank, id, title, author, thumb, photo.Int("rating")));
            }
            _lblStatus.Text = $"{_grid.Controls.Count} photos — click for full size";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"unavailable: {ex.GetType().Name} — {ex.Message}";
        }
    }

    private static Panel Card(ImageFetcher images, int rank, int id, string title, string author, Image thumb, double rating)
    {
        var card = new Panel
        {
            Size = new Size(224, 236), Margin = new Padding(8), BackColor = Ui.Card,
            Padding = new Padding(6, 6, 6, 0), Cursor = Cursors.Hand, Tag = id,
        };
        var box = new PictureBox { Image = thumb, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.BgDeep };
        var caption = new Label
        {
            Dock = DockStyle.Bottom, Height = 56, ForeColor = Ui.Text,
            Font = Ui.Tiny, Padding = new Padding(2, 4, 0, 0),
            Text = $"#{rank}  {title}\n© {author}   ·   {rating:0.0}★",
        };
        var rankBadge = new Label
        {
            Text = $" {rank} ", BackColor = Ui.AmberDim, ForeColor = Ui.Amber,
            Font = Ui.BodyBold, AutoSize = true, Location = new Point(10, 10),
            Padding = new Padding(4, 2, 4, 3),
        };
        card.Controls.Add(box);
        card.Controls.Add(caption);
        card.Controls.Add(rankBadge);
        int photoId = id;
        card.Click += async (_, _) => await PhotoDialog.ShowAsync(images, photoId, title, author);
        box.Click += async (_, _) => await PhotoDialog.ShowAsync(images, photoId, title, author);
        caption.Click += async (_, _) => await PhotoDialog.ShowAsync(images, photoId, title, author);
        card.MouseEnter += (_, _) => card.BackColor = Ui.AccentDim;
        card.MouseLeave += (_, _) => card.BackColor = Ui.Card;
        return card;
    }
}
