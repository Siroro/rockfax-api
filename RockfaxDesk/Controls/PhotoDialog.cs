using System.Drawing;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>
/// Full-size photo popup with title/author caption. Navigable: arrow keys or the
/// on-screen buttons step through the strip the photo came from.
/// </summary>
internal sealed class PhotoDialog : Form
{
    private readonly ImageFetcher _images;
    private readonly IReadOnlyList<(int Id, string Title, string Author)> _photos;
    private int _index;
    private readonly PictureBox _box = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.BgDeep };
    private readonly Label _caption = new()
    {
        Dock = DockStyle.Bottom, Height = 44, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 10f), Padding = new Padding(10, 0, 0, 0),
    };
    private Image? _current;

    private PhotoDialog(ImageFetcher images, IReadOnlyList<(int Id, string Title, string Author)> photos, int index)
    {
        _images = images;
        _photos = photos;
        _index = index;

        Text = "Photo";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(940, 760);
        MinimizeBox = false;
        KeyPreview = true;
        BackColor = Ui.BgDeep;

        var prev = Ui.Button("\u2039", 40);
        var next = Ui.Button("\u203A", 40);
        var nav = new Panel { Dock = DockStyle.Right, Width = 100, BackColor = Ui.BgDeep };
        prev.Parent = nav; next.Parent = nav;
        prev.Location = new Point(2, 7);
        next.Location = new Point(50, 7);
        prev.Click += (_, _) => Step(-1);
        next.Click += (_, _) => Step(1);

        _caption.Controls.Add(nav);
        Controls.Add(_box);
        Controls.Add(_caption);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Left) Step(-1);
            if (e.KeyCode == Keys.Right) Step(1);
            if (e.KeyCode == Keys.Escape) Close();
        };
        _box.Click += (_, _) => Close();

        Load += async (_, _) => await LoadPhoto();
    }

    private void Step(int delta)
    {
        int next = Math.Clamp(_index + delta, 0, _photos.Count - 1);
        if (next == _index) return;
        _index = next;
        _ = LoadPhoto();
    }

    private async Task LoadPhoto()
    {
        (int id, string title, string author) = _photos[_index];
        Text = $"Photo {id}  ({_index + 1}/{_photos.Count})";
        _caption.Text = $"{title}   \u2014   \u00a9 {author}";
        Image? full = await _images.GetFullAsync(id);
        if (full is null)
        {
            _caption.Text = $"could not load photo {id}";
            return;
        }
        Image? old = _current;
        _current = full;
        _box.Image = full;
        old?.Dispose();
    }

    public static async Task ShowAsync(ImageFetcher images, IReadOnlyList<(int Id, string Title, string Author)> photos, int index)
    {
        if (photos.Count == 0) return;
        using var dialog = new PhotoDialog(images, photos, Math.Clamp(index, 0, photos.Count - 1));
        dialog.ShowDialog();
    }

    /// <summary>Opens the photo from a strip of PhotoCards, wired for arrow-key navigation.</summary>
    public static Task ShowFromStripAsync(ImageFetcher images, Control strip, int photoId)
    {
        var photos = Collect(strip);
        if (photos.Count == 0) return Task.CompletedTask;
        int index = Math.Max(0, photos.FindIndex(p => p.Id == photoId));
        return ShowAsync(images, photos, index);
    }

    internal static List<(int Id, string Title, string Author)> Collect(Control strip)
        => strip.Controls.OfType<Control>()
            .Where(c => c.Tag is ValueTuple<int, string, string>)
            .Select(c => ((int Id, string Title, string Author))c.Tag!)
            .ToList();
}
