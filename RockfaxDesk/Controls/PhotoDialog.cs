using System.Drawing;

namespace RockfaxDesk.Controls;

/// <summary>Full-size photo popup with title/author caption.</summary>
internal sealed class PhotoDialog : Form
{
    private PhotoDialog()
    {
        Text = "Photo";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 720);
        MinimizeBox = false;
        BackColor = Color.FromArgb(12, 16, 24);
    }

    public static async Task ShowAsync(ImageFetcher images, int photoId, string title, string author)
    {
        Image? full = await images.GetFullAsync(photoId);
        if (full is null)
        {
            MessageBox.Show($"Could not load photo {photoId}.", "Photo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new PhotoDialog();
        dialog.Text = $"Photo {photoId} — {title}".TrimEnd('—', ' ');
        var box = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = full };
        var caption = new Label
        {
            Dock = DockStyle.Bottom, Height = 40, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 10f), Padding = new Padding(10, 0, 0, 0),
            Text = $"{title}   —   © {author}",
        };
        dialog.Controls.Add(box);
        dialog.Controls.Add(caption);
        dialog.KeyPreview = true;
        dialog.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) dialog.Close(); };
        box.Click += (_, _) => dialog.Close();
        caption.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
        full.Dispose();
    }
}
