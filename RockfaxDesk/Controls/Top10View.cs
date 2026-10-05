using System.Drawing;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>The weekly top-ten photos as a clickable grid.</summary>
internal sealed class Top10View : UserControl
{
    private readonly FlowLayoutPanel _grid = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(18, 26, 38), Padding = new Padding(10),
    };
    private readonly Label _lblTitle = new()
    {
        Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI Semibold", 13f),
        ForeColor = Color.FromArgb(235, 245, 255), Padding = new Padding(8, 4, 0, 0), Text = "Weekly top 10 photos",
    };
    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Top, Height = 20, ForeColor = Color.FromArgb(255, 170, 60),
        Font = new Font("Segoe UI", 9f), Padding = new Padding(8, 0, 0, 0),
    };

    private RockfaxClient? _api;
    private ImageFetcher? _images;

    public Top10View()
    {
        BackColor = Color.FromArgb(24, 32, 44);
        Controls.Add(_grid);
        Controls.Add(_lblTitle);
        Controls.Add(_lblStatus);
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
        foreach (Control c in _grid.Controls) if (c is PictureBox pb) pb.Image?.Dispose();
        _grid.Controls.Clear();

        try
        {
            using JsonDocument doc = await _api.GetWeeklyTopTenPhotosAsync();
            foreach (JsonElement photo in doc.RootElement.GetProperty("photos").EnumerateArray())
            {
                int id = photo.Int("id");
                string title = photo.Str("title"), author = photo.Str("author");
                Image? thumb = await _images.GetThumbAsync(id);
                if (thumb is null) continue;

                var card = new Panel
                {
                    Size = new Size(210, 210), Margin = new Padding(8), BackColor = Color.FromArgb(30, 40, 54),
                    Padding = new Padding(4), Cursor = Cursors.Hand, Tag = id,
                };
                var box = new PictureBox { Image = thumb, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
                var caption = new Label
                {
                    Dock = DockStyle.Bottom, Height = 42, ForeColor = Color.Gainsboro,
                    Font = new Font("Segoe UI", 8.5f), Padding = new Padding(4, 2, 0, 0),
                    Text = $"{title}\n© {author}", TextAlign = ContentAlignment.MiddleLeft,
                };
                card.Controls.Add(box);
                card.Controls.Add(caption);
                card.Click += async (_, _) => await PhotoDialog.ShowAsync(_images, id, title, author);
                box.Click += async (_, _) => await PhotoDialog.ShowAsync(_images, id, title, author);
                _grid.Controls.Add(card);
            }
            _lblStatus.Text = $"{_grid.Controls.Count} photos — click for full size";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "unavailable: " + ex.Message;
        }
    }
}
