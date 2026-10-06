using System.Drawing;
using System.Globalization;
using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk.Controls;

/// <summary>A crag page: grade distribution chips, weather forecast cards, full route list, photos.</summary>
internal sealed class CragView : UserControl
{
    private readonly Label _empty;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ui.Bg };

    private readonly Label _lblTitle = Ui.Label("", Ui.Text, Ui.H1);
    private readonly FlowLayoutPanel _metaChips = new()
    {
        Dock = DockStyle.Right, BackColor = Ui.Bg, WrapContents = false, AutoSize = true,
        Padding = new Padding(0, 8, 10, 0),
    };
    private readonly FlowLayoutPanel _grades = new()
    {
        Dock = DockStyle.Top, Height = 34, BackColor = Ui.Bg, Padding = new Padding(12, 4, 0, 0), WrapContents = false,
    };
    private readonly FlowLayoutPanel _weather = new()
    {
        Dock = DockStyle.Top, Height = 92, BackColor = Ui.Bg, Padding = new Padding(12, 4, 0, 0), WrapContents = false,
    };
    private readonly UiList _lvRoutes = new();
    private readonly List<(RouteSummary Summary, string Buttress, int Band)> _allRoutes = new();
    private readonly List<int> _groupFirstItems = new();
    private ComboBox? _buttressJump;
    private int _bandFilter = -1; // -1 = all
    private int _sortColumn = -1; // -1 = grouped by buttress; 0-4 = flat list sorted by that column
    private bool _sortDesc;
    private static readonly string[] RouteColumnNames = { "Route", "Grade", "Tech", "Stars", "UKC id" };
    private readonly FlowLayoutPanel _photos = RouteView.NewPhotoStrip();
    private readonly Label _lblStatus = Ui.Label("", Ui.Amber, Ui.Small);

    // ---- crag info (description / access / guidebooks / parking / comments) -----------
    private readonly Label _infoHeader = Ui.SectionHeader("CRAG INFO", 30);
    private readonly Panel _infoExpander = new() { Dock = DockStyle.Top, Height = 250, BackColor = Ui.Bg, Visible = false };
    private readonly FlowLayoutPanel _infoChips = new()
    {
        Dock = DockStyle.Top, Height = 28, BackColor = Ui.Bg, Padding = new Padding(12, 2, 0, 0), WrapContents = false,
    };
    private readonly TextBox _txtFeatures = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Ui.Panel, ForeColor = Ui.Text, Font = Ui.Body,
    };
    private readonly TextBox _txtAccess = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Ui.Panel, ForeColor = Ui.Text, Font = Ui.Small,
    };
    private readonly Label _lblGuides = new()
    {
        Dock = DockStyle.Top, Height = 36, ForeColor = Ui.Text,
        Font = Ui.Small, BackColor = Ui.Bg, Padding = new Padding(12, 2, 0, 0),
    };
    private readonly Label _lblParking = new()
    {
        Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, ForeColor = Ui.Muted,
        Font = Ui.Small, BackColor = Ui.Bg, Padding = new Padding(12, 2, 0, 0),
    };
    private readonly TextBox _txtCragComments = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Ui.Panel, ForeColor = Ui.Text, Font = Ui.Small,
    };
    private readonly Label _cragCommentsHeader = Ui.SectionHeader("CRAG COMMENTS", 24);
    private readonly ToolTip _tips = new()
    {
        OwnerDraw = true, BackColor = Ui.BgDeep, ForeColor = Ui.Text, ShowAlways = true,
    };
    private bool _infoExpanded = true;

    private RockfaxClient? _api;
    private ImageFetcher? _images;

    public event Action<RouteSummary>? RouteRequested;
    public event Action<int>? MapRequested;

    public CragView()
    {
        DoubleBuffered = true;
        BackColor = Ui.Bg;

        _empty = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Open a crag from the map, from All crags, or via the ▸ link on a route page.",
            ForeColor = Ui.Muted,
            Font = Ui.H2,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Ui.Bg,
        };

        _lvRoutes.Columns.Add("Route", 320);
        _lvRoutes.Columns.Add("Grade", 70);
        _lvRoutes.Columns.Add("Tech", 60);
        _lvRoutes.Columns.Add("Stars", 56);
        _lvRoutes.Columns.Add("UKC id", 76);
        _lvRoutes.ColumnInk[1] = Ui.Amber;
        _lvRoutes.ColumnInk[3] = Ui.Green;
        _lvRoutes.ColumnClick += (_, e) => CycleSort(e.Column);
        _lvRoutes.DoubleClick += async (_, _) =>
        {
            if (_lvRoutes.SelectedItems.Count > 0 && _lvRoutes.SelectedItems[0].Tag is RouteSummary r)
                RouteRequested?.Invoke(r);
        };
        _lvRoutes.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem? hit = _lvRoutes.HitTest(e.Location).Item;
            if (hit?.Tag is not RouteSummary route) return;
            hit.Selected = true;
            string url = $"https://www.ukclimbing.com/logbook/route.php?id={route.UkcId}";
            Ui.Menu(
                ("Open route", () => RouteRequested?.Invoke(route)),
                ("Open on UKC ↗", () => Jx.OpenBrowser(url)),
                ("Copy UKC link", () =>
                {
                    try { Clipboard.SetText(url); _lblStatus.Text = "link copied to the clipboard"; }
                    catch { _lblStatus.Text = "clipboard is busy — try again"; }
                })
            ).Show(_lvRoutes, e.Location);
        };

        var titleRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Ui.Bg, Padding = new Padding(10, 2, 0, 0) };
        titleRow.Controls.Add(_lblTitle);
        titleRow.Controls.Add(_metaChips);
        _buttressJump = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Ui.BgDeep,
            ForeColor = Ui.Text,
            Font = Ui.Small,
            Width = 200,
            Visible = false,
        };
        _buttressJump.SelectedIndexChanged += (_, _) =>
        {
            if (_buttressJump.SelectedIndex is int idx && idx > 0 && idx - 1 < _groupFirstItems.Count)
            {
                _lvRoutes.EnsureVisible(_groupFirstItems[idx - 1]);
            }
        };
        var jumpHost = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Ui.Bg, Padding = new Padding(10, 4, 0, 0) };
        jumpHost.Controls.Add(_buttressJump);
        var routesHeader = Ui.SectionHeader("ROUTES — double-click for details", 26);
        var weatherHeader = Ui.SectionHeader("WEATHER", 24);
        var accessHeader = Ui.SectionHeader("ACCESS & CONSERVATION", 24);
        var guidesHeader = Ui.SectionHeader("GUIDEBOOKS", 24);

        // Info expander: two columns so the routes table keeps room below.
        // Left: chips + description (fills) + access. Right: guides, parking, comments (fills).
        var featuresHost = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Panel, Padding = new Padding(8, 6, 8, 4) };
        featuresHost.Controls.Add(_txtFeatures);
        var accessHost = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Ui.Panel, Padding = new Padding(8, 4, 8, 4) };
        accessHost.Controls.Add(_txtAccess);
        var commentsHost = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Panel, Padding = new Padding(8, 4, 8, 4) };
        commentsHost.Controls.Add(_txtCragComments);
        _lblGuides.Height = 36;

        var left = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(0, 0, 6, 0) };
        left.Controls.Add(featuresHost);
        left.Controls.Add(accessHost);
        left.Controls.Add(accessHeader);
        left.Controls.Add(_infoChips);

        var right = new Panel { Dock = DockStyle.Right, Width = 430, BackColor = Ui.Bg, Padding = new Padding(6, 0, 0, 0) };
        right.Controls.Add(commentsHost);
        right.Controls.Add(_cragCommentsHeader);
        right.Controls.Add(_lblParking);
        right.Controls.Add(_lblGuides);
        right.Controls.Add(guidesHeader);

        _infoExpander.Controls.Add(left);
        _infoExpander.Controls.Add(right);
        _infoHeader.Cursor = Cursors.Hand;
        _infoHeader.Click += (_, _) => ToggleInfo();
        _tips.Draw += (_, e) =>
        {
            e.DrawBackground();
            using var border = new Pen(Ui.Border);
            e.Graphics.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            e.DrawText();
        };

        _content.Controls.Add(_lvRoutes);
        _content.Controls.Add(_photos);
        _content.Controls.Add(jumpHost);
        _content.Controls.Add(routesHeader);
        _content.Controls.Add(_infoExpander);
        _content.Controls.Add(_infoHeader);
        _content.Controls.Add(weatherHeader);
        _content.Controls.Add(_weather);
        _content.Controls.Add(_grades);
        _content.Controls.Add(titleRow);
        _lblStatus.Dock = DockStyle.Bottom;
        _lblStatus.AutoSize = false;
        _lblStatus.Height = 22;
        _lblStatus.BackColor = Ui.BgDeep;
        _lblStatus.Padding = new Padding(10, 2, 0, 0);
        _content.Controls.Add(_lblStatus);

        Controls.Add(_empty);
        Controls.Add(_content);
        _content.Visible = false;
    }

    public void Bind(RockfaxClient api, ImageFetcher images)
    {
        _api = api;
        _images = images;
    }

    public async Task ShowCragAsync(int ukcCragId, string knownTitle)
    {
        if (_api is null || _images is null) return;
        _empty.Visible = false;
        _content.Visible = true;

        _lblTitle.Text = knownTitle.Length > 0 ? knownTitle : $"Crag {ukcCragId}";
        _grades.Controls.Clear();
        _allRoutes.Clear();
        _bandFilter = -1;
        _weather.Controls.Clear();
        _lvRoutes.Items.Clear();
        _infoHeader.Text = "▍ CRAG INFO";
        _infoHeader.ForeColor = Ui.Accent;
        _infoChips.Controls.Clear();
        _txtFeatures.Clear();
        _txtAccess.Clear();
        _lblGuides.Text = "";
        _lblParking.Text = "";
        _txtCragComments.Clear();
        _cragCommentsHeader.Text = "▍ CRAG COMMENTS";
        _infoExpander.Visible = false;
        foreach (Control c in _photos.Controls)
            if (c is Panel { Controls.Count: > 0 } && c.Controls[0] is PictureBox pb)
                pb.Image?.Dispose();
        _photos.Controls.Clear();
        _lblStatus.Text = "loading crag…";

        string cragName = knownTitle;
        string area = "";

        try
        {
            using JsonDocument doc = await _api.GetCragRoutesAsync(ukcCragId);
            JsonElement root = doc.RootElement;

            // Response shape: { "routes": [ {name, grade, ukcID, buttress, ...} ],
            //                   "crags":  { "<id>": { name, gradeColors: [4], areaName } } }
            if (root.TryGetProperty("crags", out JsonElement crags))
            {
                foreach (JsonProperty cragEntry in crags.EnumerateObject())
                {
                    JsonElement crag = cragEntry.Value;
                    if (crag.ValueKind != JsonValueKind.Object) continue;
                    if (cragName.Length == 0) cragName = crag.Str("name");
                    if (area.Length == 0) area = crag.Str("areaName");

                    if (crag.TryGetProperty("gradeColors", out JsonElement grades) && grades.ValueKind == JsonValueKind.Array)
                    {
                        int[] g = grades.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                        if (g.Length >= 4)
                        {
                            _gradeCounts = g;
                            BuildGradeChips();
                        }
                    }
                }
            }
            _lblTitle.Text = cragName.Length > 0 ? cragName : $"Crag {ukcCragId}";
            _metaChips.Controls.Clear();
            var mapBtn = Ui.Button("show on map", 118);
            mapBtn.Click += (_, _) => MapRequested?.Invoke(ukcCragId);
            var ukcLink = Ui.Button("on UKC \u2197", 92);
            ukcLink.Click += (_, _) => Jx.OpenBrowser($"https://www.ukclimbing.com/logbook/crag.php?id={ukcCragId}");
            if (area.Length > 0) _metaChips.Controls.Add(Ui.Chip(area, Ui.Accent));
            _metaChips.Controls.Add(mapBtn);
            _metaChips.Controls.Add(ukcLink);
            _metaChips.Controls.Add(Ui.Chip($"UKC #{ukcCragId}", Ui.Muted));

            _allRoutes.Clear();
            if (root.TryGetProperty("routes", out JsonElement routes) && routes.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement route in routes.EnumerateArray())
                {
                    var summary = new RouteSummary(
                        route.Str("name"), route.Str("grade"), route.Str("techGrade"), route.Int("stars"),
                        cragName, route.Int("ukcID"), route.Int("rockfaxID"), ukcCragId);
                    if (summary.UkcId == 0 && summary.RockfaxId == 0) continue;
                    _allRoutes.Add((summary, route.Str("buttress"), GradeBand(summary.Grade)));
                }
            }
            ApplyRouteFilter();
            _lvRoutes.StretchLastColumn();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "routes unavailable: " + ex.Message;
        }

        try
        {
            using JsonDocument doc = await _api.GetWeatherAsync(new[] { ukcCragId }, RockfaxApi.Site.UkClimbing);
            foreach (WeatherChip chip in FormatWeather(doc))
            {
                if (chip.Hourly is { Length: > 0 } hours) _tips.SetToolTip(chip, hours);
                _weather.Controls.Add(chip);
            }
            if (_weather.Controls.Count == 0) _weather.Controls.Add(Ui.Label("no forecast data", Ui.Muted, Ui.Small));
        }
        catch
        {
            _weather.Controls.Add(Ui.Label("weather unavailable", Ui.Muted, Ui.Small));
        }

        // Crag description / access / guidebooks / parking / crag comments.
        try
        {
            using JsonDocument doc = await _api.GetCragDetailsAsync(new[] { ukcCragId }, RockfaxApi.Site.UkClimbing);
            ShowInfo(ParseCragInfo(doc));
        }
        catch (Exception ex)
        {
            _infoHeader.Text = "▍ CRAG INFO — unavailable";
            _infoHeader.ForeColor = Ui.Muted;
            _infoExpander.Visible = false;
            _lblStatus.Text = "crag info unavailable: " + ex.Message;
        }

        _lblStatus.Text = "loading photos…";
        int shown = 0;
        try
        {
            using JsonDocument doc = await _api.GetCragPhotosAsync(new[] { ukcCragId }, RockfaxApi.Site.UkClimbing);
            foreach ((int id, string title, string author) in RouteView.ParsePhotos(doc))
            {
                Image? thumb = await _images.GetThumbAsync(id);
                if (thumb is null) continue;
                _photos.Controls.Add(RouteView.PhotoCard(_images, id, title, author, thumb));
                shown++;
            }
        }
        catch
        {
            // photos are decoration; ignore failures
        }
        _lblStatus.Text = _lvRoutes.Items.Count > 0
            ? $"{_lvRoutes.Items.Count} routes — double-click for details · click a column to sort" + (shown > 0 ? $"  ·  {shown} photos" : "")
            : "no routes listed";
    }

    private int[] _gradeCounts = Array.Empty<int>();

    internal static int GradeBand(string grade)
    {
        if (grade.StartsWith("M") || grade.StartsWith("D") || grade.StartsWith("VD")
            || grade.StartsWith("V Diff") || grade.StartsWith("HVD")) return 0;
        if (grade.StartsWith("S") || grade.StartsWith("HS")) return 1;
        if (grade.StartsWith("VS") || grade.StartsWith("HVS")) return 2;
        if (grade.StartsWith("E")) return 3;
        return -1; // sport, boulder, winter, unknown
    }

    private void BuildGradeChips()
    {
        _grades.Controls.Clear();
        string[] labels = { "Mod-VD", "S-HS", "VS-HVS", "E1+" };
        Color[] inks = { Ui.Green, Ui.Amber, Color.FromArgb(251, 146, 60), Ui.Red };
        for (int band = 0; band < 4; band++)
        {
            if (_gradeCounts.Length > band && _gradeCounts[band] == 0) continue; // no noise chips for empty bands
            Color ink = inks[band];
            var chip = Ui.Chip($"{_gradeCounts[band]}  {labels[band]}",
                               _bandFilter == band ? Ui.Bg : ink,
                               _bandFilter == band ? ink : Ui.Card);
            int captured = band;
            chip.Cursor = Cursors.Hand;
            chip.Click += (_, _) =>
            {
                _bandFilter = _bandFilter == captured ? -1 : captured;
                BuildGradeChips();
                ApplyRouteFilter();
            };
            _grades.Controls.Add(chip);
        }
    }

    /// <summary>Column-header click: ascending → descending → back to buttress grouping.</summary>
    internal void CycleSort(int column)
    {
        if (column < 0 || column >= RouteColumnNames.Length) return;
        if (_sortColumn != column) { _sortColumn = column; _sortDesc = false; }
        else if (!_sortDesc) _sortDesc = true;
        else { _sortColumn = -1; _sortDesc = false; } // third click restores the default view
        UpdateColumnHeaders();
        ApplyRouteFilter();
    }

    private void UpdateColumnHeaders()
    {
        for (int i = 0; i < _lvRoutes.Columns.Count && i < RouteColumnNames.Length; i++)
            _lvRoutes.Columns[i].Text = i == _sortColumn
                ? RouteColumnNames[i] + (_sortDesc ? " \u25bc" : " \u25b2")
                : RouteColumnNames[i];
    }

    private void ApplyRouteFilter()
    {
        _lvRoutes.BeginUpdate();
        _lvRoutes.Groups.Clear();
        _lvRoutes.Items.Clear();
        _groupFirstItems.Clear();
        if (_buttressJump is not null) _buttressJump.Items.Clear();

        var visible = new List<(RouteSummary Summary, string Buttress)>();
        foreach ((RouteSummary summary, string buttress, int band) in _allRoutes)
        {
            if (_bandFilter >= 0 && band != _bandFilter) continue;
            visible.Add((summary, buttress));
        }

        if (_sortColumn >= 0)
        {
            _lvRoutes.ShowGroups = false;
            visible.Sort((x, y) =>
            {
                int c = CompareRoutes(x.Summary, y.Summary, _sortColumn);
                return _sortDesc ? -c : c;
            });
            foreach ((RouteSummary summary, _) in visible)
                _lvRoutes.Items.Add(RouteItem(summary, null));
        }
        else
        {
            _lvRoutes.ShowGroups = true;
            if (_buttressJump is not null) _buttressJump.Items.Add("jump to buttress\u2026");
            var groups = new Dictionary<string, ListViewGroup>();
            foreach ((RouteSummary summary, string buttress) in visible)
            {
                string b = buttress.Length > 0 ? buttress : "General";
                if (!groups.TryGetValue(b, out ListViewGroup? group))
                {
                    group = new ListViewGroup(b);
                    groups[b] = group;
                    _lvRoutes.Groups.Add(group);
                    _groupFirstItems.Add(_lvRoutes.Items.Count); // index this group's first item gets
                    if (_buttressJump is not null) _buttressJump.Items.Add(b);
                }
                _lvRoutes.Items.Add(RouteItem(summary, group));
            }
            if (_groupFirstItems.Count > 1 && _buttressJump is not null) _buttressJump.SelectedIndex = 0;
        }
        if (_buttressJump is not null) _buttressJump.Visible = _groupFirstItems.Count > 1;
        _lvRoutes.EndUpdate();
        _lvRoutes.StretchLastColumn();
        if (_bandFilter >= 0 && _allRoutes.Count > 0)
        {
            string[] labels = { "Mod-VD", "S-HS", "VS-HVS", "E1+" };
            _lblStatus.Text = $"{_lvRoutes.Items.Count} of {_allRoutes.Count} routes ({labels[_bandFilter]} band) — double-click for details";
        }
    }

    private static ListViewItem RouteItem(RouteSummary summary, ListViewGroup? group)
    {
        var item = group is null ? new ListViewItem(summary.Name) : new ListViewItem(summary.Name, group);
        item.SubItems.Add(summary.Grade);
        item.SubItems.Add(summary.TechGrade);
        item.SubItems.Add(Jx.Stars(summary.Stars));
        item.SubItems.Add(summary.UkcId.ToString());
        item.Tag = summary;
        return item;
    }

    /// <summary>Routes-table column order: name, grade (grade-aware), tech grade, stars, UKC id.</summary>
    internal static int CompareRoutes(RouteSummary a, RouteSummary b, int column)
    {
        int c;
        if (column == 0)
            c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        else if (column == 1)
        {
            c = GradeKey(a.Grade).CompareTo(GradeKey(b.Grade));
            if (c == 0) c = string.Compare(a.Grade, b.Grade, StringComparison.OrdinalIgnoreCase);
        }
        else if (column == 2)
            c = string.Compare(a.TechGrade, b.TechGrade, StringComparison.OrdinalIgnoreCase);
        else if (column == 3)
            c = a.Stars.CompareTo(b.Stars);
        else
            c = a.UkcId.CompareTo(b.UkcId);
        if (c == 0 && column != 0)
            c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); // stable tie-break
        return c;
    }

    /// <summary>Grade sort key: adjectival and E-grades rank below sport numbers; unparseable grades last.</summary>
    internal static int GradeKey(string grade)
    {
        int rank = LogbookView.GradeRank(grade);
        return rank < 0 ? int.MaxValue : rank;
    }

    /// <summary>Fills the info expander from a crag-details response; hides it when there is nothing to show.</summary>
    private void ShowInfo(CragInfo? info)
    {
        if (info is null)
        {
            _infoHeader.Text = "▍ CRAG INFO — none for this crag";
            _infoHeader.ForeColor = Ui.Muted;
            _infoExpander.Visible = false;
            return;
        }

        if (info.RockType.Length > 0) _infoChips.Controls.Add(Ui.Chip(info.RockType, Ui.Accent));
        if (info.EditDate > 0) _infoChips.Controls.Add(Ui.Chip($"updated {Jx.DateFromUnix(info.EditDate)}", Ui.Muted));
        if (info.Parking.Count > 0)
            _infoChips.Controls.Add(Ui.Chip($"{info.Parking.Count} parking", Ui.Muted));

        string features = Jx.StripHtml(info.Features);
        _txtFeatures.Text = features.Length > 0 ? features : "(no crag description available)";

        string access = Jx.StripHtml(info.Access);
        _txtAccess.Text = access.Length > 0 ? access : "(no access notes)";

        var inPrint = info.Guidebooks.Where(g => g.InPrint).ToList();
        var older = info.Guidebooks.Where(g => !g.InPrint).ToList();
        if (info.Guidebooks.Count > 0)
        {
            string join(IEnumerable<CragGuidebook> books)
                => string.Join(" · ", books.Select(g => g.Year > 0 ? $"{g.Name} ({g.Year})" : g.Name));
            _lblGuides.Text = (inPrint.Count > 0
                    ? "In print: " + join(inPrint)
                    : "") + (older.Count > 0
                    ? (inPrint.Count > 0 ? "  —  " : "") + $"{older.Count} out-of-print guides"
                    : "");
            _tips.SetToolTip(_lblGuides, string.Join("\r\n", info.Guidebooks.Select(g =>
                $"{g.Name} ({g.Year}){(g.InPrint ? " — in print" : "")}")));
        }
        else
        {
            _lblGuides.Text = "no guidebooks listed";
        }

        if (info.Parking.Count > 0)
            _lblParking.Text = "Parking — " + string.Join(" · ", info.Parking.Select(p => $"{p.Name} ({(p.Pay ? "pay" : "free")})"));

        _txtCragComments.Text = info.Comments.Count > 0
            ? string.Join("\r\n\r\n", info.Comments.Select(c => $"{c.Date} — {c.Who}: {c.Text}"))
            : "no crag comments yet";
        _cragCommentsHeader.Text = $"▍ CRAG COMMENTS ({info.Comments.Count})";

        bool hasContent = features.Length > 0 || access.Length > 0 || info.Guidebooks.Count > 0
                          || info.Comments.Count > 0 || info.Parking.Count > 0;
        _infoHeader.Text = hasContent ? "▍ CRAG INFO — click to collapse" : "▍ CRAG INFO — none for this crag";
        _infoHeader.ForeColor = hasContent ? Ui.Accent : Ui.Muted;
        _infoExpanded = hasContent;
        _infoExpander.Visible = _infoExpanded;
    }

    private void ToggleInfo()
    {
        if (!_infoExpander.Visible && _txtFeatures.Text.Length == 0) return; // nothing to expand
        _infoExpanded = !_infoExpanded;
        _infoExpander.Visible = _infoExpanded;
        _infoHeader.Text = _infoExpanded ? "▍ CRAG INFO — click to collapse" : "▍ CRAG INFO — click to expand";
    }

    /// <summary>First named object in a crag-details response; null when the response has none.</summary>
    internal static CragInfo? ParseCragInfo(JsonDocument doc)
    {
        foreach (JsonElement e in doc.RootElement.EnumerateArrayOrObjectValues())
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("name", out _)) continue;

            var guides = new List<CragGuidebook>();
            if (e.TryGetProperty("guidebooks", out JsonElement gb) && gb.ValueKind == JsonValueKind.Array)
                foreach (JsonElement g in gb.EnumerateArray())
                    guides.Add(new CragGuidebook(g.Str("name"), g.Int("year"), g.Int("inprint") == 1));

            var comments = new List<(string, string, string)>();
            if (e.TryGetProperty("comments", out JsonElement cs) && cs.ValueKind == JsonValueKind.Array)
                foreach (JsonElement c in cs.EnumerateArray())
                    comments.Add((c.Str("name"), Jx.DateFromUnix(c.Long("date")), c.Str("comment")));

            var parking = new List<(string, bool)>();
            if (e.TryGetProperty("parking", out JsonElement pk) && pk.ValueKind == JsonValueKind.Array)
                foreach (JsonElement p in pk.EnumerateArray())
                    parking.Add((p.Str("name"), p.Int("pay") == 1));

            var buttresses = new List<string>();
            if (e.TryGetProperty("buttressdata", out JsonElement bd) && bd.ValueKind == JsonValueKind.Array)
                foreach (JsonElement b in bd.EnumerateArray())
                    buttresses.Add(b.Str("name"));

            return new CragInfo(e.Str("name"), e.Str("rocktype"), e.Str("features"), e.Str("access"),
                e.Int("nroutes"), e.Long("editDate"), e.Int("bmc"), guides, comments, parking, buttresses);
        }
        return null;
    }

    internal static List<WeatherChip> FormatWeather(JsonDocument doc)
    {
        var chips = new List<WeatherChip>();
        foreach (JsonElement area in doc.RootElement.EnumerateArrayOrObjectValues())
        {
            if (area.ValueKind != JsonValueKind.Object) continue;
            foreach (JsonProperty day in area.EnumerateObject())
            {
                if (day.Value.ValueKind != JsonValueKind.Object || !day.Value.TryGetProperty("dly", out JsonElement dly)) continue;
                string raw = day.Name.Length > 2 ? day.Name[2..] : day.Name; // e.g. "Sa26-10-03" -> "26-10-03"
                var chip = new WeatherChip
                {
                    Day = FormatDay(raw),
                    Temp = dly.Int("t"),
                    RainPct = dly.Int("cor"),
                    Wind = dly.Int("ws"),
                    WindDeg = dly.Int("wd"),
                    Code = dly.Int("wc"),
                };
                if (day.Value.TryGetProperty("ast", out JsonElement ast))
                {
                    chip.Sunrise = ast.Str("sr");
                    chip.Sunset = ast.Str("ss");
                }
                chip.Hourly = HourlyText(day.Value);
                chips.Add(chip);
            }
            break; // one forecast area is enough for the strip
        }
        return chips;
    }

    /// <summary>Two-hourly daytime summary for the tooltip: "09:00  14°  rain 13%  wind 21 W".</summary>
    internal static string? HourlyText(JsonElement day)
    {
        if (!day.TryGetProperty("hry", out JsonElement hry) || hry.ValueKind != JsonValueKind.Object)
            return null;
        var lines = new List<string>();
        foreach (JsonProperty hour in hry.EnumerateObject())
        {
            if (!int.TryParse(hour.Name.Split(':')[0], out int h) || h is < 6 or > 20 || h % 2 != 0) continue;
            JsonElement v = hour.Value;
            lines.Add($"{h:00}:00  {v.Int("t"),2}°  rain {v.Int("cor"),2}%  wind {v.Int("ws")} {Jx.Compass(v.Int("wd"))}");
        }
        return lines.Count > 0 ? string.Join("\r\n", lines) : null;
    }

    /// <summary>"26-10-03" -> "Sat 3 Oct" (or "Today"); falls back to the raw string.</summary>
    private static string FormatDay(string raw)
        => DateTime.TryParseExact(raw, "yy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt)
            ? dt.Date == DateTime.Today ? "Today" : dt.ToString("ddd d MMM", CultureInfo.InvariantCulture)
            : raw;
}

/// <summary>Typed view of one crag-details entry (logbook/v1/crag_ukc/{id}).</summary>
internal sealed record CragGuidebook(string Name, int Year, bool InPrint);

internal sealed record CragInfo(
    string Name, string RockType, string Features, string Access, int NRoutes, long EditDate, int BmcId,
    IReadOnlyList<CragGuidebook> Guidebooks,
    IReadOnlyList<(string Who, string Date, string Text)> Comments,
    IReadOnlyList<(string Name, bool Pay)> Parking,
    IReadOnlyList<string> Buttresses);

/// <summary>One painted weather-day card.</summary>
internal sealed class WeatherChip : Control
{
    private static readonly SolidBrush CardFill = new(Ui.CardSoft);

    public string Day = "";
    public int Temp, RainPct, Wind, WindDeg, Code;
    public string Sunrise = "", Sunset = "";
    public string? Hourly;

    public WeatherChip()
    {
        Size = new Size(104, 80);
        BackColor = Ui.Bg; // corners outside the flat tile show the page
        Margin = new Padding(0, 0, 6, 0);
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    private static string CodeText(int wc)
    {
        if (wc == 0) return "clear";
        if (wc <= 2) return "p.sun";
        if (wc == 3) return "cloud";
        if (wc <= 48) return "fog";
        if (wc <= 57) return "drizzle";
        if (wc <= 67) return "rain";
        if (wc <= 77) return "snow";
        if (wc <= 82) return "showers";
        if (wc >= 95) return "storm";
        return "w" + wc;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.FillRectangle(CardFill, ClientRectangle); // plain flat tile — no border, no rounding

        TextRenderer.DrawText(g, Day, Ui.BodyBold, new Point(10, 5), Ui.Accent);
        Color tempInk = Temp >= 18 ? Ui.Amber : Temp <= 4 ? Ui.Accent : Ui.Text;
        TextRenderer.DrawText(g, $"{Temp}\u00b0", Ui.MonoBig, new Point(8, 19), tempInk);
        TextRenderer.DrawText(g, $"{RainPct}% rain", Ui.Tiny, new Point(10, 45),
            RainPct >= 60 ? Ui.Accent : Ui.Muted, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, $"{CodeText(Code)} \u00b7 wind {Wind} {Jx.Compass(WindDeg)}", Ui.Tiny, new Point(10, 56),
            Ui.Muted, TextFormatFlags.EndEllipsis);
        if (Sunrise.Length > 0 || Sunset.Length > 0)
            TextRenderer.DrawText(g, $"\u2191{Sunrise}  \u2193{Sunset}", Ui.Tiny, new Point(10, 67),
                Ui.Muted, TextFormatFlags.EndEllipsis);
    }
}
