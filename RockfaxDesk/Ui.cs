using System.ComponentModel;
using System.Drawing;

namespace RockfaxDesk;

/// <summary>
/// Design system for RockfaxDesk: one dark palette, one type ramp, factories for
/// every control so spacing/colors stay consistent everywhere.
/// </summary>
internal static class Ui
{
    // ---- palette ----------------------------------------------------------
    public static readonly Color Bg        = Color.FromArgb(13, 20, 36);    // window
    public static readonly Color BgDeep    = Color.FromArgb(8, 13, 25);     // wells, header bands
    public static readonly Color Panel     = Color.FromArgb(20, 30, 50);    // list rows, panels
    public static readonly Color RowAlt    = Color.FromArgb(24, 35, 57);    // alternating rows
    public static readonly Color Card      = Color.FromArgb(27, 39, 62);    // cards, chips
    public static readonly Color Border    = Color.FromArgb(45, 61, 90);
    // Near-flat fills: close enough to the page that a shape's edge (and its
    // antialiasing halo) stops reading as a hairline border. Color lives in the ink.
    public static readonly Color SurfaceSoft = Color.FromArgb(18, 27, 44);  // chips at rest
    public static readonly Color BtnFace    = Color.FromArgb(16, 24, 41);   // buttons at rest
    public static readonly Color CardSoft   = Color.FromArgb(22, 32, 51);   // weather tiles
    public static readonly Color Text      = Color.FromArgb(226, 232, 240);
    public static readonly Color Muted     = Color.FromArgb(133, 152, 180);
    public static readonly Color Accent    = Color.FromArgb(56, 189, 248);  // sky
    public static readonly Color AccentDim = Color.FromArgb(16, 66, 96);
    public static readonly Color Selection = Color.FromArgb(13, 78, 114);
    public static readonly Color Amber     = Color.FromArgb(245, 158, 11);
    public static readonly Color AmberDim  = Color.FromArgb(88, 62, 14);
    public static readonly Color Green     = Color.FromArgb(52, 211, 153);
    public static readonly Color Red       = Color.FromArgb(248, 113, 113);

    // ---- type ramp ----------------------------------------------------------
    // Segoe UI Variable (Win11) with plain Segoe UI as fallback; Cascadia Mono for
    // numbers, falling back to Consolas. One place to retune the whole app.
    private static readonly string BodyFamily = FamilyOr("Segoe UI Variable Text", "Segoe UI");
    private static readonly string DisplayFamily = FamilyOr("Segoe UI Variable Display", BodyFamily);
    private static readonly string MonoFamily = FamilyOr("Cascadia Mono", "Consolas");

    private static string FamilyOr(string preferred, string fallback)
        => FontFamily.Families.Any(f => f.Name == preferred) ? preferred : fallback;

    public static Font H1 { get; } = new(DisplayFamily, 17f, FontStyle.Bold);
    public static Font H2 { get; } = new(DisplayFamily, 11.5f, FontStyle.Bold);
    public static Font Body { get; } = new(BodyFamily, 10f);
    public static Font BodyBold { get; } = new(BodyFamily, 10f, FontStyle.Bold);
    public static Font Small { get; } = new(BodyFamily, 9.25f);
    public static Font SmallBold { get; } = new(BodyFamily, 9.25f, FontStyle.Bold);
    public static Font Tiny { get; } = new(BodyFamily, 8.5f);
    public static Font Mono { get; } = new(MonoFamily, 9.75f);
    public static Font MonoBig { get; } = new(MonoFamily, 11.5f, FontStyle.Bold);

    // ---- factories ----------------------------------------------------------

    public static Button Button(string text, int width = 96, bool primary = false, bool danger = false)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel,
            ForeColor = danger ? Red : primary ? Accent : Text,
            Font = BodyBold,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
            TabStop = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Lighten(Panel, 10);
        b.FlatAppearance.MouseDownBackColor = Selection;
        return b;
    }

    /// <summary>Borderless dark textbox wrapped in a 1px accent-on-focus border. Dock the returned panel.</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);

    /// <summary>Sets a gray hint inside the textbox (EM_SETCUEBANNER) — works with borderless boxes.</summary>
    public static TextBox Cue(TextBox tb, string hint)
    {
        SendMessage(tb.Handle, 0x1501, 1, hint);
        return tb;
    }

    public static Panel Box(TextBox tb, int width, int height = 30, string? cue = null)
    {
        tb.BorderStyle = BorderStyle.None;
        tb.BackColor = Panel;
        tb.ForeColor = Text;
        tb.Font = Body;
        tb.Margin = Padding.Empty;
        Panel wrapper = new()
        {
            Width = width,
            Height = height,
            BackColor = Panel, // a flat input strip like the list rows
            Margin = new Padding(0, 0, 10, 0),
        };
        tb.Dock = DockStyle.Fill;
        wrapper.Padding = new Padding(8, Math.Max(0, (height - tb.Height) / 2), 8, 0);
        wrapper.Controls.Add(tb);
        if (cue is not null) SendMessage(tb.Handle, 0x1501, 1, cue); // after handle (re)creation
        return wrapper;
    }

    public static Label Label(string text, Color? color = null, Font? font = null, bool auto = true)
        => new()
        {
            Text = text,
            AutoSize = auto,
            ForeColor = color ?? Text,
            Font = font ?? Body,
            Margin = new Padding(0, 0, 10, 0),
        };


    /// <summary>Channels clamped upward — used for hover tints on painted controls.</summary>
    public static Color Lighten(Color c, int amount) => Color.FromArgb(
        Math.Min(c.R + amount, 255), Math.Min(c.G + amount, 255), Math.Min(c.B + amount, 255));

    /// <summary>Flat text chip: colored bold text on a plain fill, no custom painting.</summary>
    public static Label Chip(string text, Color ink, Color? fill = null)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = ink,
            Font = BodyBold,
            BackColor = fill ?? Panel,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(8, 3, 8, 4),
        };

    /// <summary>Sunken section header: small caps feel, accent tick before the text.</summary>
    public static Label SectionHeader(string text, int height = 30)
        => new()
        {
            Text = "▍ " + text,
            Dock = DockStyle.Top,
            Height = height,
            ForeColor = Accent,
            BackColor = BgDeep,
            Font = H2,
            Padding = new Padding(8, 4, 0, 0),
            Margin = Padding.Empty,
        };

    /// <summary>Dark right-click menu. Items are (label, action) pairs; pass one per line.</summary>
    public static ContextMenuStrip Menu(params (string Label, Action Click)[] items)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Panel,
            ForeColor = Text,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Font = Body,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false },
        };
        foreach ((string label, Action click) in items)
        {
            var item = new ToolStripMenuItem(label) { ForeColor = Text };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
        }
        return menu;
    }
}

/// <summary>Palette for Ui.Menu so right-click menus match the app instead of system light.</summary>
internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Ui.Panel;
    public override Color ImageMarginGradientBegin => Ui.Panel;
    public override Color ImageMarginGradientMiddle => Ui.Panel;
    public override Color ImageMarginGradientEnd => Ui.Panel;
    public override Color MenuBorder => Ui.Border;
    public override Color MenuItemBorder => Ui.Border;
    public override Color MenuItemSelected => Ui.Card;
    public override Color MenuItemSelectedGradientBegin => Ui.Card;
    public override Color MenuItemSelectedGradientEnd => Ui.Card;
    public override Color MenuItemPressedGradientBegin => Ui.Panel;
    public override Color MenuItemPressedGradientEnd => Ui.Panel;
    public override Color SeparatorDark => Ui.Border;
    public override Color SeparatorLight => Ui.Border;
}


/// <summary>
/// Dark owner-drawn ListView: themed header band, alternating rows, accent selection,
/// optional per-column ink colors. All lists in the app use this.
/// </summary>
internal static class DarkScroll
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? win);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int LvmSetExtendedStyle = 0x1036;   // LVM_SETEXTENDEDLISTVIEWSTYLE
    private const int LvsExDoubleBuffer = 0x00010000; // LVS_EX_DOUBLEBUFFER

    /// <summary>Routes native listview painting through a memory buffer. Without it
    /// every hover/selection change repaints the surface unbuffered and flickers.
    /// Must run after base.OnHandleCreated — WinForms re-applies its own extended
    /// styles there and clears bits it doesn't manage.</summary>
    internal static void EnableDoubleBuffer(IntPtr handle)
    {
        try
        {
            SendMessage(handle, LvmSetExtendedStyle, (IntPtr)LvsExDoubleBuffer, (IntPtr)LvsExDoubleBuffer);
        }
        catch { /* best effort */ }
    }

    /// <summary>Applies the DarkMode_Explorer scrollbar theme once a handle exists.</summary>
    public static void Apply(Control control)
    {
        control.HandleCreated += (_, _) => ApplyNow(control.Handle);
        if (control.IsHandleCreated) ApplyNow(control.Handle);
    }

    /// <summary>Darks the native header child of a ListView — its beyond-last-section
    /// corner otherwise paints light and shows as a white box next to the scrollbar.
    /// Deferred a loop turn: the SysHeader32 child doesn't exist inside HandleCreated.</summary>
    public static void ApplyHeader(Control list)
    {
        void go()
        {
            if (!list.IsHandleCreated) return;
            try
            {
                IntPtr header = FindWindowEx(list.Handle, IntPtr.Zero, "SysHeader32", null);
                if (header != IntPtr.Zero) ApplyNow(header);
            }
            catch { /* best effort */ }
        }
        list.HandleCreated += (_, _) => list.BeginInvoke(go);
        if (list.IsHandleCreated) list.BeginInvoke(go);
    }

    private static void ApplyNow(IntPtr handle)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            try { SetWindowTheme(handle, "DarkMode_Explorer", null); } catch { /* best effort */ }
        }
    }
}

internal sealed class UiList : ListView
{
    /// <summary>Column index → text ink for that column (empty = default).</summary>
    public readonly Dictionary<int, Color> ColumnInk = new();

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AltRows { get; set; } = true;

    /// <summary>Click a column header to sort by it (non-virtual lists only).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ColumnSorting { get; set; } = true;
    private int _sortColumn = -1;
    private bool _sortDesc;

    public UiList()
    {
        Dock = DockStyle.Fill; // every list in the app fills its host
        BackColor = Ui.Panel;
        ForeColor = Ui.Text;
        BorderStyle = BorderStyle.None;
        Font = Ui.Body;
        FullRowSelect = true;
        HideSelection = false;
        View = View.Details;
        OwnerDraw = true;
        DrawColumnHeader += DrawHeader;
        DrawSubItem += DrawSubItemRow;
        Resize += (_, _) => StretchLastColumn();
        DarkScroll.Apply(this);
        DarkScroll.ApplyHeader(this);
        ColumnClick += OnColumnClicked;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e); // ListView re-applies its extended styles here, clearing ours
        DarkScroll.EnableDoubleBuffer(Handle);
    }

    private void OnColumnClicked(object? sender, ColumnClickEventArgs e)
    {
        if (VirtualMode || !ColumnSorting || Items.Count == 0) return;
        if (e.Column == _sortColumn) _sortDesc = !_sortDesc;
        else { _sortColumn = e.Column; _sortDesc = false; }
        SortByColumn(_sortColumn, _sortDesc);
    }

    /// <summary>Re-orders items by a subitem's text (numeric-aware). Groups are preserved.</summary>
    public void SortByColumn(int column, bool descending)
    {
        _sortColumn = column;
        _sortDesc = descending;
        if (VirtualMode || column < 0) return;
        BeginUpdate();
        var items = Items.Cast<ListViewItem>().ToList();
        items.Sort((a, b) =>
        {
            string at = column < a.SubItems.Count ? a.SubItems[column].Text : "";
            string bt = column < b.SubItems.Count ? b.SubItems[column].Text : "";
            int c;
            if (double.TryParse(at, out double an) && double.TryParse(bt, out double bn)) c = an.CompareTo(bn);
            else c = string.Compare(at, bt, StringComparison.OrdinalIgnoreCase);
            return descending ? -c : c;
        });
        Items.Clear();
        Items.AddRange(items.ToArray());
        EndUpdate();
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        int row = hit.Item is not null ? hit.Item.Index : -1;
        if (row != _hoverRow)
        {
            int previous = _hoverRow;
            _hoverRow = row;
            // repaint just the two rows that changed, not the whole surface
            InvalidateRow(previous);
            InvalidateRow(row);
            HoverRowChanged?.Invoke(row);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverRow != -1)
        {
            InvalidateRow(_hoverRow);
            _hoverRow = -1;
        }
    }

    private void InvalidateRow(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        Rectangle bounds = Items[index].Bounds;
        if (bounds.Width > 0 && bounds.Height > 0) Invalidate(bounds);
    }

    /// <summary>Sizes the last column to exactly the remaining client width. Call after
    /// populating: ClientSize then already excludes the vertical scrollbar, so sections
    /// meet it exactly — no unpainted header corner, no horizontal scrollbar.
    /// (Native -2 autosize runs past the client edge on owner-drawn grouped lists.)</summary>
    public void StretchLastColumn()
    {
        if (Columns.Count == 0) return;
        int others = 0;
        for (int i = 0; i < Columns.Count - 1; i++) others += Columns[i].Width;
        Columns[^1].Width = Math.Max(60, ClientSize.Width - others);
    }

    private static readonly SolidBrush HeaderBrush = new(Ui.BgDeep);
    private static readonly Pen HeaderUnderline = new(Ui.AccentDim);
    private static readonly Pen HeaderSeparator = new(Ui.Border);

    private static void DrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        Graphics g = e.Graphics;
        g.FillRectangle(HeaderBrush, e.Bounds);
        g.DrawLine(HeaderUnderline, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        string label = e.Header?.Text ?? "";
        if (sender is not UiList list) return;
        if (list.ColumnSorting && e.ColumnIndex == list._sortColumn)
            label += list._sortDesc ? " \u25bc" : " \u25b2";
        TextRenderer.DrawText(g, label, Ui.BodyBold,
            new Point(e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height - Ui.BodyBold.Height) / 2), Ui.Muted);
        g.DrawLine(HeaderSeparator, e.Bounds.Right - 1, 6, e.Bounds.Right - 1, e.Bounds.Bottom - 6);
    }

    private int _hoverRow = -1;

    /// <summary>Raised after the hovered row changes (view refreshes itself).</summary>
    public event Action<int>? HoverRowChanged;

    public int HoverRow => _hoverRow;

    // Cached row paints: owner-draw runs these for every visible cell on every
    // hover/selection/scroll repaint, so allocating brushes there shows up as churn.
    private static readonly SolidBrush RowBrush = new(Ui.Panel);
    private static readonly SolidBrush RowAltBrush = new(Ui.RowAlt);
    private static readonly SolidBrush RowHoverBrush = new(Color.FromArgb(34, 48, 74));
    private static readonly SolidBrush RowSelectedBrush = new(Ui.Selection);
    private static readonly Pen RowSelectedAccent = new(Ui.Accent, 2f);

    private void DrawSubItemRow(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null) return;
        bool selected = e.Item.Selected;
        SolidBrush bg = selected ? RowSelectedBrush
            : e.ItemIndex == _hoverRow ? RowHoverBrush
            : AltRows && e.ItemIndex % 2 == 1 ? RowAltBrush
            : RowBrush;
        e.Graphics.FillRectangle(bg, e.Bounds);

        Color ink = e.ColumnIndex == 0 ? Ui.Text
            : ColumnInk.TryGetValue(e.ColumnIndex, out Color c) ? c
            : Ui.Muted;
        if (e.SubItem is not null && !e.Item.UseItemStyleForSubItems && e.ColumnIndex > 0)
            ink = e.SubItem.ForeColor;

        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", Ui.Body,
            new Point(e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height - Ui.Body.Height) / 2), ink);

        if (selected)
            e.Graphics.DrawLine(RowSelectedAccent, e.Bounds.Left, e.Bounds.Top, e.Bounds.Left, e.Bounds.Bottom);
    }
}


/// <summary>
/// Custom tab strip + host: flat dark headers with an accent underline for the active
/// tab (no owner-drawn TabControl edge artifacts). Content panels are switched by index.
/// </summary>
internal sealed class TabStrip : Panel
{
    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Ui.Bg };
    private readonly FlowLayoutPanel _strip = new()
    {
        Dock = DockStyle.Top, Height = 44, BackColor = Ui.BgDeep, Padding = new Padding(10, 0, 0, 0), WrapContents = false,
    };
    private readonly List<(Button Header, Control Content)> _tabs = new();
    private int _selected = -1;

    public event Action<int>? Selected;

    /// <summary>Raised only for user-initiated selections (tab click / hotkey), so
    /// navigation history can trace tab switches without recording programmatic ones.</summary>
    public event Action<int>? UserSelected;

    public TabStrip()
    {
        Dock = DockStyle.Fill;
        BackColor = Ui.BgDeep;
        Controls.Add(_host);
        Controls.Add(_strip);
    }

    public void AddTab(string title, Control content)
    {
        content.Dock = DockStyle.Fill;
        content.Visible = false;
        _host.Controls.Add(content);
        int index = _tabs.Count;
        var b = new Button
        {
            Text = title,
            AutoSize = true,
            MinimumSize = new Size(68, 44),
            Padding = new Padding(20, 0, 20, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Ui.BgDeep,
            ForeColor = Ui.Muted,
            Font = Ui.BodyBold,
            Cursor = Cursors.Hand,
            Margin = Padding.Empty,
            Tag = index,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Ui.Panel;
        b.Click += (_, _) => Select(index, user: true);
        _strip.Controls.Add(b);
        _tabs.Add((b, content));
        if (_selected < 0) Select(0);
    }

    public int SelectedIndex => _selected;

    public void Select(int index, bool user = false)
    {
        if (index is < 0 || index >= _tabs.Count || index == _selected) return;
        if (user) UserSelected?.Invoke(index); // before state changes: same-tab clicks never reach here
        _selected = index;
        for (int i = 0; i < _tabs.Count; i++)
        {
            (Button header, Control content) = _tabs[i];
            content.Visible = i == index;
            header.BackColor = i == index ? Ui.Bg : Ui.BgDeep;
            header.ForeColor = i == index ? Ui.Accent : Ui.Muted;
            header.Paint -= PaintUnderline;
            if (i == index) header.Paint += PaintUnderline;
            header.Invalidate();
        }
        Selected?.Invoke(index);
    }

    private static void PaintUnderline(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(Ui.Accent, 2.5f);
        Rectangle r = ((Control)sender!).ClientRectangle;
        e.Graphics.DrawLine(pen, r.Left + 6, r.Bottom - 2, r.Right - 6, r.Bottom - 2);
    }
}
