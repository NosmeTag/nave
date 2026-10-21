using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NaveBrowser;

public partial class Form1 : Form
{
    private static readonly Color WindowColor = Color.FromArgb(18, 20, 27);
    private static readonly Color SideColor = Color.FromArgb(28, 30, 39);
    private static readonly Color FieldColor = Color.FromArgb(25, 27, 36);
    private static readonly Color TextColor = Color.FromArgb(235, 233, 243);
    private static readonly Color MutedColor = Color.FromArgb(156, 154, 168);
    private static readonly Color AccentColor = Color.FromArgb(172, 150, 255);
    private static readonly Color LineColor = Color.FromArgb(48, 50, 61);
    private static readonly Color TabActiveColor = Color.FromArgb(53, 48, 68);
    private static readonly Color TabHoverColor = Color.FromArgb(41, 39, 52);

    private readonly Panel _page = new();
    private readonly Panel _toolbar = new();
    private readonly Panel _toolbarTop = new();
    private readonly Panel _homePanel = new();
    private readonly Panel _homeCard = new();
    private readonly FlowLayoutPanel _favorites = new();
    private readonly FlowLayoutPanel _homeShortcuts = new();
    private readonly Panel _sidebar = new();
    private readonly TableLayoutPanel _sidebarLayout = new();
    private readonly FlowLayoutPanel _tabRows = new();
    private readonly TextBox _address = new();
    private readonly TextBox _homeSearch = new();
    private readonly Label _stateLabel = new();
    private readonly Label _sidebarTabCount = new();
    private readonly Button _sidebarPinButton = new();
    private Panel? _toolbarAddressFrame;
    private FlowLayoutPanel? _toolbarNavFlow;
    private FlowLayoutPanel? _toolbarChromeButtons;
    private readonly ToolTip _toolTips = new();
    private readonly System.Windows.Forms.Timer _edgeTimer = new();
    private readonly System.Windows.Forms.Timer _sidebarAnimation = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _toolbarAnimation = new() { Interval = 15 };
    private readonly List<BrowserTab> _tabs = [];
    private readonly List<FavoriteItem> _bookmarks = [];
    private readonly Stack<TabSnapshot> _closedTabs = new();
    private readonly HashSet<string> _allowedSites = new(StringComparer.OrdinalIgnoreCase);
    private CoreWebView2Environment? _environment;
    private BrowserTab? _activeTab;
    private Button? _blockerButton;
    private ToolStripMenuItem? _blockerMenuEnabled;
    private ToolStripMenuItem? _blockerMenuSiteException;
    private ToolStripMenuItem? _blockerMenuStatus;
    private HashSet<string> _blockedHosts = new(BuiltInBlockedHosts, StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _blocklistUpdatedAt;
    private bool _sidebarPinned;
    private bool _toolbarPinned;
    private bool _blockerEnabled = true;
    private bool _isShuttingDown;
    private int _blockedRequests;
    private int _tabNumber;
    private int _sidebarTarget;
    private int _toolbarTarget;
    private string? _pendingHomeSearch;

    private const int SidebarWidth = 224;
    private const int SidebarInsetLeft = 12;
    private const int SidebarInsetTop = 56;
    private const int SidebarInsetBottom = 20;
    private const int SidebarChromeHeight = 194;
    private const int SidebarTabRowHeight = 38;
    private const int SidebarMaxHeight = 720;
    private const int ToolbarHeight = 88;
    private const int ToolbarTopInset = 0;
    private const int EdgeSize = 6;

    private static readonly HashSet<string> BuiltInBlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "adservice.google.com",
        "adnxs.com", "criteo.com", "criteo.net", "taboola.com", "outbrain.com", "adsrvr.org",
        "adform.net", "amazon-adsystem.com", "adroll.com", "rubiconproject.com", "pubmatic.com",
        "openx.net", "scorecardresearch.com", "zedo.com", "moatads.com", "serving-sys.com",
        "casalemedia.com", "smartadserver.com", "sharethrough.com", "contextweb.com"
    };
    private static readonly Uri HostsListUri = new("https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts");
    private static readonly HttpClient BlocklistHttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly TimeSpan BlocklistRefreshInterval = TimeSpan.FromDays(7);
    private const int MaxBlocklistBytes = 12 * 1024 * 1024;
    private const int MaxBlocklistDomains = 250_000;

    public Form1()
    {
        InitializeComponent();
        Text = "Nave";
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? System.Drawing.SystemIcons.Application; }
        catch { Icon = System.Drawing.SystemIcons.Application; }
        BackColor = WindowColor;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        KeyPreview = true;
        LoadBlockerState();
        BuildBrowserShell();

        Shown += async (_, _) =>
        {
            _edgeTimer.Start();
            await NewTabAsync();
            _ = RefreshBlocklistAsync(force: false);
        };
        FormClosing += (_, _) =>
        {
            _isShuttingDown = true;
            _edgeTimer.Stop();
            _sidebarAnimation.Stop();
            _toolbarAnimation.Stop();
            foreach (var tab in _tabs) tab.SiteIcon.Image?.Dispose();
        };
        Resize += (_, _) => LayoutOverlays();
        KeyDown += HandleShortcuts;
    }

    private void BuildBrowserShell()
    {
        _page.Dock = DockStyle.Fill;
        _page.BackColor = WindowColor;
        Controls.Add(_page);

        BuildToolbar();
        BuildSidebar();
        BuildHomePage();

        _edgeTimer.Interval = 45;
        _edgeTimer.Tick += (_, _) => CheckEdgeHover();
        _edgeTimer.Start();
        _sidebarAnimation.Tick += (_, _) => AnimateSidebarStep();
        _toolbarAnimation.Tick += (_, _) => AnimateToolbarStep();

        _page.Resize += (_, _) => LayoutOverlays();
    }

    private void BuildHomePage()
    {
        _homePanel.Dock = DockStyle.Fill;
        _homePanel.BackColor = WindowColor;
        _homePanel.Visible = true;

        _homeCard.Size = new Size(620, 320);
        _homeCard.BackColor = WindowColor;
        _homeCard.Anchor = AnchorStyles.None;

        var title = new Label
        {
            Text = "Nave",
            Location = new Point(0, 20),
            Size = new Size(620, 50),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = TextColor,
            Font = new Font("Segoe UI Semibold", 34F)
        };
        var subtitle = new Label
        {
            Text = "O que vamos descobrir hoje?",
            Location = new Point(0, 72),
            Size = new Size(620, 28),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI", 11F)
        };
        var searchFrame = new Panel
        {
            Location = new Point(30, 122),
            Size = new Size(560, 52),
            Padding = new Padding(13, 0, 8, 0),
            BackColor = FieldColor
        };
        var searchIcon = new Label
        {
            Text = "⌕",
            Dock = DockStyle.Left,
            Width = 32,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AccentColor,
            Font = new Font("Segoe UI", 20F),
            BackColor = FieldColor
        };
        _homeSearch.Dock = DockStyle.Fill;
        _homeSearch.BorderStyle = BorderStyle.None;
        _homeSearch.BackColor = FieldColor;
        _homeSearch.ForeColor = TextColor;
        _homeSearch.Font = new Font("Segoe UI", 11F);
        _homeSearch.PlaceholderText = "Pesquisar na web ou digitar um endereço";
        _homeSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SubmitHomeSearch();
        };
        var searchButton = new Button
        {
            Text = "↵",
            Dock = DockStyle.Right,
            Width = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = FieldColor,
            ForeColor = AccentColor,
            Font = new Font("Segoe UI Semibold", 14F),
            Cursor = Cursors.Hand
        };
        searchButton.FlatAppearance.BorderSize = 0;
        searchButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 42, 58);
        searchButton.Click += (_, _) =>
        {
            SubmitHomeSearch();
        };
        searchFrame.Controls.Add(_homeSearch);
        searchFrame.Controls.Add(searchButton);
        searchFrame.Controls.Add(searchIcon);
        searchFrame.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(_homeSearch.ContainsFocus ? AccentColor : LineColor);
            using var path = CreateRoundedPath(new Rectangle(0, 0, searchFrame.Width - 1, searchFrame.Height - 1), 12);
            e.Graphics.DrawPath(pen, path);
        };
        searchFrame.Resize += (_, _) => ApplyRoundedRegion(searchFrame, 12);
        ApplyRoundedRegion(searchFrame, 12);
        _homeSearch.GotFocus += (_, _) => searchFrame.Invalidate();
        _homeSearch.LostFocus += (_, _) => searchFrame.Invalidate();

        var shortcutLabel = new Label
        {
            Text = "ATALHOS RÁPIDOS",
            Location = new Point(30, 197),
            Size = new Size(560, 20),
            ForeColor = MutedColor,
            Font = new Font("Segoe UI Semibold", 8.5F),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _homeShortcuts.Location = new Point(20, 222);
        _homeShortcuts.Size = new Size(580, 86);
        _homeShortcuts.FlowDirection = FlowDirection.LeftToRight;
        _homeShortcuts.WrapContents = false;
        _homeShortcuts.AutoScroll = true;
        _homeShortcuts.BackColor = WindowColor;

        _homeCard.Controls.Add(title);
        _homeCard.Controls.Add(subtitle);
        _homeCard.Controls.Add(searchFrame);
        _homeCard.Controls.Add(shortcutLabel);
        _homeCard.Controls.Add(_homeShortcuts);
        _homePanel.Controls.Add(_homeCard);
        _page.Controls.Add(_homePanel);
        PositionHomeCard();
    }

    private void PositionHomeCard()
    {
        _homeCard.Left = Math.Max(16, (_page.ClientSize.Width - _homeCard.Width) / 2);
        _homeCard.Top = Math.Max(20, (_page.ClientSize.Height - _homeCard.Height) / 2 - 20);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        using var path = CreateRoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius);
        control.Region?.Dispose();
        control.Region = new Region(path);
    }

    private void RefreshHomeShortcuts()
    {
        foreach (Control old in _homeShortcuts.Controls.Cast<Control>().ToArray())
        {
            if (old is Button { Image: { } image }) image.Dispose();
            old.Dispose();
        }
        _homeShortcuts.Controls.Clear();
        foreach (var bookmark in _bookmarks)
        {
            var shortcut = MakeHomeShortcut(bookmark);
            _homeShortcuts.Controls.Add(shortcut);
        }
        var add = new Button
        {
            Text = "+\nAdicionar",
            Width = 88,
            Height = 82,
            Margin = new Padding(5, 2, 5, 2),
            FlatStyle = FlatStyle.Flat,
            BackColor = WindowColor,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI", 9F),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        add.FlatAppearance.BorderSize = 0;
        add.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 36, 46);
        add.Click += (_, _) => ShowAddShortcutDialog();
        _homeShortcuts.Controls.Add(add);
    }

    private void SubmitHomeSearch()
    {
        string query = _homeSearch.Text.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;
        if (_activeTab?.View?.CoreWebView2 is null)
        {
            _pendingHomeSearch = query;
            _stateLabel.Text = "Preparando a pesquisa…";
            return;
        }
        _homeSearch.Clear();
        if (TryGetWebUri(query, out var uri)) NavigateTo(uri.ToString());
        else if (query.Contains(' ') || !query.Contains('.')) NavigateSearch(query);
        else NavigateTo("https://" + query);
    }

    private void ShowAddShortcutDialog()
    {
        using var dialog = new Form
        {
            Text = "Adicionar atalho rápido",
            ClientSize = new Size(430, 220),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = SideColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9.5F)
        };
        var titleLabel = new Label { Text = "Nome do atalho", Location = new Point(22, 18), Size = new Size(380, 22) };
        var titleInput = new TextBox { Location = new Point(22, 42), Size = new Size(386, 28), BackColor = FieldColor, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        var urlLabel = new Label { Text = "Endereço do site", Location = new Point(22, 80), Size = new Size(380, 22) };
        var urlInput = new TextBox { Location = new Point(22, 104), Size = new Size(386, 28), BackColor = FieldColor, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "https://exemplo.com" };
        var cancel = new Button { Text = "Cancelar", Location = new Point(226, 162), Size = new Size(86, 34), FlatStyle = FlatStyle.Flat, BackColor = SideColor, ForeColor = TextColor, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Adicionar", Location = new Point(322, 162), Size = new Size(86, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(74, 62, 103), ForeColor = TextColor };
        cancel.FlatAppearance.BorderColor = LineColor;
        save.FlatAppearance.BorderSize = 0;
        save.Click += (_, _) =>
        {
            string address = urlInput.Text.Trim();
            if (!TryGetWebUri(address, out _) && !TryGetWebUri("https://" + address, out _))
            {
                MessageBox.Show(dialog, "Digite um endereço válido, como exemplo.com", "Endereço inválido", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AddBookmark(titleInput.Text.Trim(), address.Contains("://") ? address : "https://" + address);
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        dialog.Controls.AddRange([titleLabel, titleInput, urlLabel, urlInput, cancel, save]);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => titleInput.Focus();
        dialog.ShowDialog(this);
    }

    private Button MakeHomeShortcut(FavoriteItem bookmark)
    {
        var shortcut = new Button
        {
            Text = bookmark.Title,
            Width = 88,
            Height = 82,
            Margin = new Padding(5, 2, 5, 2),
            FlatStyle = FlatStyle.Flat,
            BackColor = WindowColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 8.5F),
            TextImageRelation = TextImageRelation.ImageAboveText,
            ImageAlign = ContentAlignment.MiddleCenter,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Image = GetBookmarkIcon(bookmark)
        };
        shortcut.FlatAppearance.BorderSize = 0;
        shortcut.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 36, 46);
        shortcut.FlatAppearance.MouseDownBackColor = Color.FromArgb(49, 44, 65);
        _toolTips.SetToolTip(shortcut, bookmark.Url + "\nClique com o botão direito para remover");
        shortcut.Click += (_, _) => NavigateTo(bookmark.Url);
        shortcut.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) RemoveBookmark(bookmark);
        };
        return shortcut;
    }

    private static Image GetBookmarkIcon(FavoriteItem bookmark)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(bookmark.IconData))
            {
                using var stream = new MemoryStream(Convert.FromBase64String(bookmark.IconData));
                using var source = Image.FromStream(stream);
                return new Bitmap(source, new Size(32, 32));
            }
        }
        catch { }

        var image = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Color.FromArgb(57, 48, 78));
        graphics.FillEllipse(background, 1, 1, 30, 30);
        string initial = string.IsNullOrWhiteSpace(bookmark.Title) ? "•" : bookmark.Title[..1].ToUpperInvariant();
        using var font = new Font("Segoe UI Semibold", 15F);
        TextRenderer.DrawText(graphics, initial, font, new Rectangle(1, 1, 30, 30), TextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return image;
    }

    private void BuildToolbar()
    {
        _toolbar.BackColor = SideColor;
        _toolbar.Size = new Size(ClientSize.Width, ToolbarHeight);
        _toolbar.Location = new Point(0, ToolbarTopInset);
        _toolbar.Visible = false;
        _toolbar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _toolbar.Padding = new Padding(12, 7, 6, 4);

        _toolbarTop.Dock = DockStyle.Top;
        _toolbarTop.Height = 42;
        _toolbarTop.BackColor = SideColor;

        var navFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 148,
            Height = 38,
            WrapContents = false,
            BackColor = SideColor,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 5, 0)
        };
        navFlow.Controls.Add(MakeToolButton("‹", "Voltar", (_, _) => _activeTab?.View?.CoreWebView2?.GoBack()));
        navFlow.Controls.Add(MakeToolButton("›", "Avançar", (_, _) => _activeTab?.View?.CoreWebView2?.GoForward()));
        navFlow.Controls.Add(MakeToolButton("↻", "Atualizar", (_, _) => _activeTab?.View?.CoreWebView2?.Reload()));
        navFlow.Controls.Add(MakeToolButton("☆", "Fixar ou recolher a barra de favoritos", (_, _) => ToggleToolbar(!_toolbarPinned)));

        var addressFrame = new Panel
        {
            Size = new Size(720, 30),
            Margin = Padding.Empty,
            Padding = new Padding(9, 0, 8, 0),
            BackColor = FieldColor
        };
        var searchIcon = new Label
        {
            Text = "⌕",
            Dock = DockStyle.Left,
            Width = 23,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI", 14F),
            BackColor = FieldColor
        };
        _address.Dock = DockStyle.Fill;
        _address.BorderStyle = BorderStyle.None;
        _address.BackColor = FieldColor;
        _address.ForeColor = TextColor;
        _address.Font = new Font("Segoe UI", 10.5F);
        _address.PlaceholderText = "Pesquisar ou inserir endereço";
        addressFrame.Controls.Add(_address);
        addressFrame.Controls.Add(searchIcon);
        addressFrame.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(_address.ContainsFocus ? AccentColor : LineColor);
            using var path = CreateRoundedPath(new Rectangle(0, 0, addressFrame.Width - 1, addressFrame.Height - 1), 9);
            e.Graphics.DrawPath(pen, path);
        };
        addressFrame.Resize += (_, _) => ApplyRoundedRegion(addressFrame, 9);
        ApplyRoundedRegion(addressFrame, 9);
        _address.GotFocus += (_, _) => addressFrame.Invalidate();
        _address.LostFocus += (_, _) => addressFrame.Invalidate();
        _address.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            NavigateFromAddress();
        };

        var chromeButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 72,
            Height = 38,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = SideColor,
            Margin = Padding.Empty
        };
        chromeButtons.Controls.Add(MakeWindowButton(Color.FromArgb(255, 95, 87), "×", "Fechar", (_, _) => Close()));
        chromeButtons.Controls.Add(MakeWindowButton(Color.FromArgb(254, 188, 46), "−", "Minimizar", (_, _) => WindowState = FormWindowState.Minimized));
        chromeButtons.Controls.Add(MakeWindowButton(Color.FromArgb(40, 200, 64), "□", "Maximizar ou restaurar", (_, _) => ToggleWindowState()));

        _toolbarTop.Controls.Add(addressFrame);
        _toolbarTop.Controls.Add(chromeButtons);
        _toolbarTop.Controls.Add(navFlow);
        _toolbarAddressFrame = addressFrame;
        _toolbarNavFlow = navFlow;
        _toolbarChromeButtons = chromeButtons;
        _toolbarTop.Resize += (_, _) => LayoutToolbarTop();
        LayoutToolbarTop();

        _favorites.Location = new Point(14, 49);
        _favorites.Width = Math.Max(220, ClientSize.Width - 215);
        _favorites.Height = 34;
        _favorites.FlowDirection = FlowDirection.LeftToRight;
        _favorites.WrapContents = false;
        _favorites.BackColor = SideColor;
        _favorites.Padding = new Padding(8, 2, 0, 0);
        LoadBookmarks();
        RefreshFavoritesBar();

        _stateLabel.Location = new Point(ClientSize.Width - 195, 51);
        _stateLabel.Width = 170;
        _stateLabel.Height = 28;
        _stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _stateLabel.TextAlign = ContentAlignment.MiddleRight;
        _stateLabel.ForeColor = MutedColor;
        _stateLabel.Font = new Font("Segoe UI", 8.5F);
        _stateLabel.Text = "Bloqueador ligado";
        _toolbar.Controls.Add(_favorites);
        _toolbar.Controls.Add(_stateLabel);
        _toolbar.Controls.Add(_toolbarTop);
        _toolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(LineColor);
            e.Graphics.DrawLine(pen, 12, 47, Math.Max(12, _toolbar.Width - 12), 47);
        };

        Controls.Add(_toolbar);
        LayoutToolbarTop();
        _toolbar.BringToFront();
    }

    private void BuildSidebar()
    {
        _sidebar.BackColor = SideColor;
        _sidebar.Size = new Size(0, GetSidebarHeight());
        _sidebar.Location = new Point(SidebarInsetLeft, GetSidebarTop(_sidebar.Height));
        _sidebar.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _sidebar.Visible = false;
        _sidebar.Resize += (_, _) =>
        {
            if (_sidebar.Width >= 32 && _sidebar.Height >= 32)
                ApplyRoundedRegion(_sidebar, 16);
            else
            {
                _sidebar.Region?.Dispose();
                _sidebar.Region = null;
            }
        };
        _sidebar.Paint += (_, e) =>
        {
            if (_sidebar.Width < 32 || _sidebar.Height < 32) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = CreateRoundedPath(new Rectangle(0, 0, _sidebar.Width - 1, _sidebar.Height - 1), 16);
            using var pen = new Pen(LineColor);
            e.Graphics.DrawPath(pen, outline);
        };

        _sidebarLayout.Dock = DockStyle.Fill;
        _sidebarLayout.ColumnCount = 1;
        _sidebarLayout.RowCount = 5;
        _sidebarLayout.BackColor = SideColor;
        _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = SideColor, Padding = new Padding(14, 0, 10, 0) };
        var brand = new Label
        {
            Text = "Nave",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextColor,
            Font = new Font("Segoe UI Semibold", 14F)
        };
        _sidebarPinButton.Text = "⌖";
        _sidebarPinButton.AccessibleName = "Fixar lateral";
        _sidebarPinButton.Size = new Size(30, 30);
        _sidebarPinButton.Location = new Point(SidebarWidth - 50, 12);
        _sidebarPinButton.FlatStyle = FlatStyle.Flat;
        _sidebarPinButton.BackColor = SideColor;
        _sidebarPinButton.ForeColor = MutedColor;
        _sidebarPinButton.Font = new Font("Segoe UI", 13F);
        _sidebarPinButton.Cursor = Cursors.Hand;
        _sidebarPinButton.FlatAppearance.BorderSize = 0;
        _sidebarPinButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 40, 55);
        _toolTips.SetToolTip(_sidebarPinButton, "Fixar lateral (Ctrl+B)");
        _sidebarPinButton.Click += (_, _) =>
        {
            _sidebarPinned = !_sidebarPinned;
            UpdateSidebarPinButton();
            ShowSidebar(_sidebarPinned);
        };
        header.Controls.Add(brand);
        header.Controls.Add(_sidebarPinButton);
        _sidebarPinButton.BringToFront();
        brand.Padding = new Padding(0, 0, 40, 0);
        _sidebarPinButton.Resize += (_, _) => ApplyRoundedRegion(_sidebarPinButton, 9);
        ApplyRoundedRegion(_sidebarPinButton, 9);

        var addTab = MakeSidebarButton("＋   Nova aba", async (_, _) => await NewTabAsync());
        addTab.Location = Point.Empty;
        addTab.Margin = new Padding(10, 4, 10, 4);
        addTab.Width = SidebarWidth - 20;
        addTab.Height = 36;
        addTab.BackColor = Color.FromArgb(43, 40, 55);
        addTab.Font = new Font("Segoe UI Semibold", 9.2F);
        addTab.FlatAppearance.MouseOverBackColor = Color.FromArgb(57, 50, 75);
        addTab.AccessibleName = "Abrir nova aba";
        addTab.Resize += (_, _) => ApplyRoundedRegion(addTab, 9);
        ApplyRoundedRegion(addTab, 9);

        var tabsHeader = new Panel { Dock = DockStyle.Fill, BackColor = SideColor, Padding = new Padding(16, 0, 14, 0) };
        var tabsLabel = new Label
        {
            Text = "Suas abas",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI Semibold", 8.5F)
        };
        _sidebarTabCount.Dock = DockStyle.Right;
        _sidebarTabCount.Width = 24;
        _sidebarTabCount.Height = 18;
        _sidebarTabCount.TextAlign = ContentAlignment.MiddleCenter;
        _sidebarTabCount.BackColor = TabActiveColor;
        _sidebarTabCount.ForeColor = MutedColor;
        _sidebarTabCount.Font = new Font("Segoe UI Semibold", 8F);
        _sidebarTabCount.Resize += (_, _) => ApplyRoundedRegion(_sidebarTabCount, 8);
        ApplyRoundedRegion(_sidebarTabCount, 8);
        tabsHeader.Controls.Add(tabsLabel);
        tabsHeader.Controls.Add(_sidebarTabCount);

        _tabRows.Dock = DockStyle.Fill;
        _tabRows.FlowDirection = FlowDirection.TopDown;
        _tabRows.WrapContents = false;
        _tabRows.AutoScroll = true;
        _tabRows.Padding = new Padding(6, 6, 6, 6);
        _tabRows.BackColor = SideColor;

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SideColor,
            Padding = new Padding(6, 5, 6, 0)
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(LineColor);
            e.Graphics.DrawLine(pen, 12, 0, Math.Max(12, footer.Width - 12), 0);
        };
        var blocker = MakeSidebarButton("◉   Bloqueador ligado", (_, _) => ToggleBlocker());
        blocker.Name = "blockerButton";
        blocker.Height = 26;
        blocker.Margin = new Padding(3, 2, 3, 0);
        blocker.AccessibleDescription = "Clique para ligar ou pausar. Clique com o botão direito para opções.";
        _toolTips.SetToolTip(blocker, "Clique para ligar/pausar · botão direito para opções");
        _blockerButton = blocker;
        BuildBlockerMenu(blocker);
        var settings = MakeSidebarButton("⚙   Configurações", (_, _) => ShowSettings());
        settings.Height = 26;
        settings.Margin = new Padding(3, 0, 3, 0);
        footer.Controls.Add(blocker);
        footer.Controls.Add(settings);

        _sidebarLayout.Controls.Add(header, 0, 0);
        _sidebarLayout.Controls.Add(addTab, 0, 1);
        _sidebarLayout.Controls.Add(tabsHeader, 0, 2);
        _sidebarLayout.Controls.Add(_tabRows, 0, 3);
        _sidebarLayout.Controls.Add(footer, 0, 4);
        _sidebar.Controls.Add(_sidebarLayout);
        Controls.Add(_sidebar);
        _sidebar.BringToFront();
    }

    private void BuildBlockerMenu(Button blockerButton)
    {
        var menu = new ContextMenuStrip();
        _blockerMenuEnabled = new ToolStripMenuItem("Bloqueador geral") { CheckOnClick = true };
        _blockerMenuEnabled.Click += (_, _) =>
        {
            _blockerEnabled = _blockerMenuEnabled.Checked;
            UpdateBlockerStatus();
        };

        _blockerMenuSiteException = new ToolStripMenuItem("Liberar neste site") { CheckOnClick = true };
        _blockerMenuSiteException.Click += (_, _) => ToggleCurrentSiteException(_blockerMenuSiteException.Checked);

        var updateItem = new ToolStripMenuItem("Atualizar lista agora");
        updateItem.Click += async (_, _) => await RefreshBlocklistAsync(force: true);

        _blockerMenuStatus = new ToolStripMenuItem { Enabled = false };
        menu.Items.Add(_blockerMenuEnabled);
        menu.Items.Add(_blockerMenuSiteException);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(updateItem);
        menu.Items.Add(_blockerMenuStatus);
        menu.Opening += (_, _) => RefreshBlockerMenu();
        blockerButton.ContextMenuStrip = menu;
    }

    private void RefreshBlockerMenu()
    {
        if (_blockerMenuEnabled is not null) _blockerMenuEnabled.Checked = _blockerEnabled;
        if (_blockerMenuSiteException is not null)
        {
            string? site = CurrentSiteHost();
            _blockerMenuSiteException.Enabled = site is not null;
            _blockerMenuSiteException.Checked = site is not null && _allowedSites.Contains(site);
            _blockerMenuSiteException.Text = site is null ? "Liberar neste site (abra uma página)" : $"Liberar neste site ({site})";
        }
        if (_blockerMenuStatus is not null)
        {
            string updated = _blocklistUpdatedAt is null ? "lista padrão" : $"atualizada {_blocklistUpdatedAt.Value.ToLocalTime():dd/MM/yyyy}";
            _blockerMenuStatus.Text = $"{_blockedHosts.Count:N0} domínios · {updated}";
        }
    }

    private string? CurrentSiteHost() =>
        _activeTab is not null && TryGetWebUri(_activeTab.Url, out var uri) ? uri.IdnHost.TrimEnd('.') : null;

    private void ToggleCurrentSiteException(bool allow)
    {
        string? site = CurrentSiteHost();
        if (site is null) return;
        if (allow) _allowedSites.Add(site);
        else _allowedSites.Remove(site);
        SaveAllowedSites();
        UpdateBlockerStatus();
    }

    private void UpdateBlockerStatus()
    {
        if (_blockerButton is not null)
            _blockerButton.Text = _blockerEnabled ? "◉   Bloqueador ligado" : "◯   Bloqueador pausado";
        if (_activeTab is not null)
            _stateLabel.Text = _blockerEnabled ? $"Bloqueador ligado · {_blockedRequests}" : "Bloqueador pausado";
    }

    private Button MakeToolButton(string text, string accessibleName, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            AccessibleName = accessibleName,
            Width = 30,
            Height = 30,
            Margin = new Padding(2, 2, 2, 2),
            FlatStyle = FlatStyle.Flat,
            BackColor = SideColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI Semibold", 12F),
            Cursor = Cursors.Hand,
            TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 44, 63);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(70, 58, 93);
        _toolTips.SetToolTip(button, accessibleName);
        button.Click += click;
        return button;
    }

    private Button MakeWindowButton(Color color, string hoverSymbol, string accessibleName, EventHandler click)
    {
        var button = new Button
        {
            Text = "",
            AccessibleName = accessibleName,
            Width = 18,
            Height = 18,
            Margin = new Padding(3, 6, 3, 0),
            Padding = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.FromArgb(72, 38, 36),
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = color;
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color, .12F);
        _toolTips.SetToolTip(button, accessibleName);
        ApplyCircleRegion(button, 14);
        button.MouseEnter += (_, _) =>
        {
            button.Text = hoverSymbol;
            ApplyCircleRegion(button, 18);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Text = "";
            ApplyCircleRegion(button, 14);
        };
        button.Click += click;
        return button;
    }

    private static void ApplyCircleRegion(Button button, int diameter)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int inset = Math.Max(0, (button.Width - diameter) / 2);
        path.AddEllipse(inset, inset, diameter - 1, diameter - 1);
        button.Region?.Dispose();
        button.Region = new Region(path);
    }

    private Button MakeSidebarButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Height = 34,
            Width = SidebarWidth - 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 4, 0),
            Margin = new Padding(3, 1, 3, 1),
            FlatStyle = FlatStyle.Flat,
            BackColor = SideColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9.2F),
            Cursor = Cursors.Hand,
            TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 40, 55);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(59, 52, 78);
        button.Click += click;
        button.AccessibleName = text;
        button.Resize += (_, _) => ApplyRoundedRegion(button, 8);
        ApplyRoundedRegion(button, 8);
        return button;
    }

    private Button MakeFavorite(string title, string? url)
    {
        var button = new Button
        {
            Text = title,
            AutoSize = true,
            Height = 28,
            Margin = new Padding(2, 0, 3, 0),
            Padding = new Padding(9, 0, 9, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = SideColor,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI", 8.5F),
            Cursor = Cursors.Hand,
            TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 40, 55);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(59, 52, 78);
        _toolTips.SetToolTip(button, url ?? "Salvar a página atual nos favoritos");
        button.Click += (_, _) =>
        {
            if (url is null)
            {
                AddCurrentFavorite();
                return;
            }
            NavigateTo(url);
        };
        return button;
    }

    private void LoadBookmarks()
    {
        try
        {
            string path = Path.Combine(UserDataFolder, "bookmarks.json");
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<List<FavoriteItem>>(File.ReadAllText(path));
                if (saved is not null)
                    _bookmarks.AddRange(saved.Where(item => !string.IsNullOrWhiteSpace(item.Title) && TryGetWebUri(item.Url, out _)));
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }

        if (_bookmarks.Count == 0)
        {
            _bookmarks.Add(new FavoriteItem("Google", "https://www.google.com/"));
            _bookmarks.Add(new FavoriteItem("YouTube", "https://www.youtube.com/"));
        }
    }

    private void RefreshFavoritesBar()
    {
        _favorites.SuspendLayout();
        foreach (Control old in _favorites.Controls.Cast<Control>().ToArray()) old.Dispose();
        _favorites.Controls.Clear();
        foreach (var bookmark in _bookmarks)
            _favorites.Controls.Add(MakeFavorite(bookmark.Title, bookmark.Url));
        _favorites.Controls.Add(MakeFavorite("＋ Favorito", null));
        _favorites.ResumeLayout();
        RefreshHomeShortcuts();
    }

    private void AddCurrentFavorite()
    {
        if (_activeTab is null || !TryGetWebUri(_activeTab.Url, out _))
        {
            _stateLabel.Text = "Abra uma página para salvar nos favoritos";
            return;
        }
        if (_bookmarks.Any(bookmark => string.Equals(bookmark.Url, _activeTab.Url, StringComparison.OrdinalIgnoreCase)))
        {
            _stateLabel.Text = "Essa página já está nos favoritos";
            return;
        }

        _bookmarks.Add(new FavoriteItem(_activeTab.Title, _activeTab.Url, CaptureTabIcon(_activeTab)));
        try
        {
            SaveBookmarks();
            RefreshFavoritesBar();
            _stateLabel.Text = "Favorito salvo";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _stateLabel.Text = "Não consegui salvar o favorito";
        }
    }

    private string UserDataFolder => Environment.GetEnvironmentVariable("NAVE_USER_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nave", "BrowserData");

    private string BlocklistFilePath => Path.Combine(UserDataFolder, "hosts-blocklist.txt");
    private string AllowedSitesFilePath => Path.Combine(UserDataFolder, "blocker-allowed-sites.json");

    private void LoadBlockerState()
    {
        try
        {
            if (File.Exists(AllowedSitesFilePath))
            {
                var savedSites = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(AllowedSitesFilePath));
                if (savedSites is not null)
                    foreach (string site in savedSites)
                        if (IsValidDomain(site)) _allowedSites.Add(site);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load blocker exceptions: {ex}");
        }

        try
        {
            if (!File.Exists(BlocklistFilePath)) return;
            string contents = File.ReadAllText(BlocklistFilePath);
            HashSet<string> downloadedHosts = ParseHostsList(contents);
            if (downloadedHosts.Count < 1_000) return;

            _blockedHosts = new HashSet<string>(BuiltInBlockedHosts, StringComparer.OrdinalIgnoreCase);
            _blockedHosts.UnionWith(downloadedHosts);
            _blocklistUpdatedAt = File.GetLastWriteTimeUtc(BlocklistFilePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load cached blocklist: {ex}");
        }
    }

    private void SaveAllowedSites()
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder);
            string tempPath = AllowedSitesFilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_allowedSites.OrderBy(site => site)), new UTF8Encoding(false));
            File.Move(tempPath, AllowedSitesFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not save blocker exceptions: {ex}");
            _stateLabel.Text = "Não consegui salvar a exceção deste site";
        }
    }

    private async Task RefreshBlocklistAsync(bool force)
    {
        try
        {
            if (!force && _blocklistUpdatedAt is not null && DateTimeOffset.UtcNow - _blocklistUpdatedAt < BlocklistRefreshInterval)
                return;

            using var request = new HttpRequestMessage(HttpMethod.Get, HostsListUri);
            request.Headers.UserAgent.ParseAdd("NaveBrowser/0.1");
            using HttpResponseMessage response = await BlocklistHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxBlocklistBytes)
                throw new InvalidDataException("The blocklist is larger than the allowed size.");

            await using Stream source = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81_920];
            int total = 0;
            while (true)
            {
                int read = await source.ReadAsync(chunk);
                if (read == 0) break;
                total += read;
                if (total > MaxBlocklistBytes) throw new InvalidDataException("The blocklist is larger than the allowed size.");
                await buffer.WriteAsync(chunk.AsMemory(0, read));
            }

            string contents = Encoding.UTF8.GetString(buffer.ToArray());
            HashSet<string> downloadedHosts = ParseHostsList(contents);
            if (downloadedHosts.Count < 1_000 || downloadedHosts.Count > MaxBlocklistDomains)
                throw new InvalidDataException("The downloaded blocklist did not pass validation.");

            Directory.CreateDirectory(UserDataFolder);
            string tempPath = BlocklistFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, contents, new UTF8Encoding(false));
            File.Move(tempPath, BlocklistFilePath, overwrite: true);

            var refreshedHosts = new HashSet<string>(BuiltInBlockedHosts, StringComparer.OrdinalIgnoreCase);
            refreshedHosts.UnionWith(downloadedHosts);
            _blockedHosts = refreshedHosts;
            _blocklistUpdatedAt = DateTimeOffset.UtcNow;
            RefreshBlockerMenu();
            if (force) _stateLabel.Text = $"Lista atualizada · {_blockedHosts.Count:N0} domínios";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not refresh blocker list: {ex}");
            if (force) _stateLabel.Text = "Falha ao atualizar a lista; mantendo a lista salva";
        }
        finally
        {
            try
            {
                string tempPath = BlocklistFilePath + ".tmp";
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
    }

    private static HashSet<string> ParseHostsList(string contents)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in contents.Split('\n'))
        {
            string line = rawLine;
            int commentStart = line.IndexOf('#');
            if (commentStart >= 0) line = line[..commentStart];
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !IPAddress.TryParse(fields[0], out _)) continue;

            foreach (string candidate in fields.Skip(1))
            {
                string host = candidate.Trim().TrimEnd('.').ToLowerInvariant();
                if (!IsValidDomain(host)) continue;
                hosts.Add(host);
                if (hosts.Count > MaxBlocklistDomains)
                    throw new InvalidDataException("The blocklist contains too many domains.");
            }
        }
        return hosts;
    }

    private static bool IsValidDomain(string host) =>
        !string.IsNullOrWhiteSpace(host) && host.Length <= 253 && host.Contains('.') &&
        Uri.CheckHostName(host) == UriHostNameType.Dns && !IPAddress.TryParse(host, out _);

    private void SaveBookmarks()
    {
        Directory.CreateDirectory(UserDataFolder);
        File.WriteAllText(Path.Combine(UserDataFolder, "bookmarks.json"), JsonSerializer.Serialize(_bookmarks));
    }

    private static string? CaptureTabIcon(BrowserTab tab)
    {
        try
        {
            if (tab.SiteIcon.Image is null) return null;
            using var stream = new MemoryStream();
            tab.SiteIcon.Image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch { return null; }
    }

    private void AddBookmark(string title, string url)
    {
        if (!TryGetWebUri(url, out var uri))
        {
            _stateLabel.Text = "Digite um endereço http ou https válido";
            return;
        }
        string normalizedUrl = uri.ToString();
        if (_bookmarks.Any(bookmark => string.Equals(bookmark.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase)))
        {
            _stateLabel.Text = "Esse endereço já está nos atalhos";
            return;
        }
        _bookmarks.Add(new FavoriteItem(string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(), normalizedUrl,
            _activeTab is not null && string.Equals(_activeTab.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase)
                ? CaptureTabIcon(_activeTab)
                : null));
        try
        {
            SaveBookmarks();
            RefreshFavoritesBar();
            _stateLabel.Text = "Atalho salvo";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _stateLabel.Text = "Não consegui salvar o atalho";
        }
    }

    private void RemoveBookmark(FavoriteItem bookmark)
    {
        if (!_bookmarks.Remove(bookmark)) return;
        try
        {
            SaveBookmarks();
            RefreshFavoritesBar();
            _stateLabel.Text = "Atalho removido";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _stateLabel.Text = "Não consegui remover o atalho";
        }
    }

    private void LayoutOverlays()
    {
        if (_toolbar.IsDisposed || _sidebar.IsDisposed) return;
        _toolbar.Width = ClientSize.Width;
        _toolbar.Top = ToolbarTopInset;
        _sidebar.Height = GetSidebarHeight();
        _sidebar.Location = new Point(SidebarInsetLeft, GetSidebarTop(_sidebar.Height));
        _favorites.Width = Math.Max(220, ClientSize.Width - 215);
        _stateLabel.Left = ClientSize.Width - _stateLabel.Width - 18;
        LayoutToolbarTop();
        PositionHomeCard();
    }

    private int GetSidebarHeight()
    {
        int rowAndPaddingHeight = SidebarTabRowHeight + 12;
        int contentHeight = SidebarChromeHeight + (_tabs.Count * rowAndPaddingHeight);
        int availableHeight = Math.Max(220, ClientSize.Height - SidebarInsetTop - SidebarInsetBottom);
        return Math.Min(contentHeight * 3, Math.Min(SidebarMaxHeight, availableHeight));
    }

    private int GetSidebarTop(int height) => Math.Max(16, (ClientSize.Height - height) / 2);

    private void LayoutToolbarTop()
    {
        if (_toolbarAddressFrame is null || _toolbarNavFlow is null || _toolbarChromeButtons is null) return;
        _toolbarTop.PerformLayout();
        int minLeft = _toolbarNavFlow.Right + 12;
        int maxRight = _toolbarChromeButtons.Left - 12;
        int available = Math.Max(0, maxRight - minLeft);
        int width = Math.Min(720, available);
        int left = (_toolbarTop.ClientSize.Width - width) / 2;
        left = Math.Clamp(left, minLeft, Math.Max(minLeft, maxRight - width));
        _toolbarAddressFrame.SetBounds(left, Math.Max(0, (_toolbarTop.ClientSize.Height - 30) / 2), width, 30);
    }

    private void CheckEdgeHover()
    {
        if (_isShuttingDown || !IsHandleCreated || WindowState == FormWindowState.Minimized) return;
        var point = PointToClient(Cursor.Position);
        if (point.X < -2 || point.Y < -2 || point.X >= ClientSize.Width || point.Y >= ClientSize.Height) return;

        bool overSidebar = _sidebar.Visible && point.X >= _sidebar.Left && point.X < _sidebar.Right &&
                           point.Y >= _sidebar.Top && point.Y < _sidebar.Bottom;
        if (_sidebarPinned || point.X <= EdgeSize || overSidebar)
            ShowSidebar(true);
        else
            ShowSidebar(false);

        bool overToolbar = _toolbar.Visible && point.X >= _toolbar.Left && point.X < _toolbar.Right &&
                           point.Y >= _toolbar.Top && point.Y < _toolbar.Top + _toolbar.Height;
        bool addressFocused = _address.ContainsFocus;
        bool overToolbarRevealBand = point.Y >= 0 && point.Y <= EdgeSize;
        if (_toolbarPinned || overToolbarRevealBand || overToolbar || addressFocused)
            ShowToolbar(true);
        else
            ShowToolbar(false);
    }

    private void ShowSidebar(bool show)
    {
        int target = show ? SidebarWidth : 0;
        if (!show && _sidebarPinned) return;
        _sidebarTarget = target;

        if (show && !_sidebar.Visible)
        {
            _sidebar.Width = 0;
            _sidebar.Visible = true;
            _sidebar.BringToFront();
            if (_toolbar.Visible) _toolbar.BringToFront();
        }
        if (_sidebar.Width != _sidebarTarget && !_sidebarAnimation.Enabled)
        {
            _sidebarAnimation.Start();
        }
    }

    private void AnimateSidebarStep()
    {
        int difference = _sidebarTarget - _sidebar.Width;
        if (Math.Abs(difference) <= 7)
        {
            _sidebar.Width = _sidebarTarget;
            _sidebarAnimation.Stop();
            if (_sidebarTarget == 0) _sidebar.Visible = false;
            return;
        }
        _sidebar.Width += Math.Sign(difference) * Math.Max(8, Math.Abs(difference) / 3);
    }

    private void ShowToolbar(bool show)
    {
        int target = show ? ToolbarHeight : 0;
        if (!show && _toolbarPinned) return;
        _toolbarTarget = target;

        if (show && !_toolbar.Visible)
        {
            _toolbar.Height = 0;
            _toolbar.Visible = true;
            _toolbar.BringToFront();
            if (_sidebar.Visible) _sidebar.BringToFront();
        }
        if (_toolbar.Height != _toolbarTarget && !_toolbarAnimation.Enabled)
        {
            _toolbarAnimation.Start();
        }
    }

    private void AnimateToolbarStep()
    {
        int difference = _toolbarTarget - _toolbar.Height;
        if (Math.Abs(difference) <= 4)
        {
            _toolbar.Height = _toolbarTarget;
            _toolbarAnimation.Stop();
            if (_toolbarTarget == 0) _toolbar.Visible = false;
            return;
        }
        _toolbar.Height += Math.Sign(difference) * Math.Max(5, Math.Abs(difference) / 3);
    }

    private void ToggleToolbar(bool pin)
    {
        _toolbarPinned = pin;
        ShowToolbar(true);
        _stateLabel.Text = pin ? "Barra superior fixada" : "Barra ao passar o mouse";
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    private async Task NewTabAsync(string? url = null)
    {
        var tab = new BrowserTab(++_tabNumber, url ?? "nave://nova-guia");
        tab.IsHome = tab.Url == "nave://nova-guia";
        tab.Row = CreateTabRow(tab);
        _tabs.Add(tab);
        _tabRows.Controls.Add(tab.Row);
        UpdateSidebarTabCount();
        AnimateTabRowIn(tab.Row);
        await SelectTabAsync(tab);
    }

    private static void AnimateTabRowIn(Panel row)
    {
        const int fullHeight = 36;
        var timer = new System.Windows.Forms.Timer { Interval = 12 };
        timer.Tick += (_, _) =>
        {
            if (row.IsDisposed)
            {
                timer.Stop();
                timer.Dispose();
                return;
            }
            row.Height = Math.Min(fullHeight, row.Height + 6);
            if (row.Height >= fullHeight)
            {
                timer.Stop();
                timer.Dispose();
            }
        };
        timer.Start();
    }

    private Panel CreateTabRow(BrowserTab tab)
    {
        var row = new Panel
        {
            Width = SidebarWidth - 32,
            Height = 0,
            Margin = new Padding(1, 2, 1, 2),
            Padding = new Padding(2),
            BackColor = SideColor,
            Cursor = Cursors.Hand
        };
        row.Resize += (_, _) => ApplyRoundedRegion(row, 8);
        var siteIcon = new PictureBox
        {
            Dock = DockStyle.Left,
            Width = 30,
            Margin = new Padding(2, 0, 0, 0),
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = SideColor,
            Image = CreateFallbackSiteIcon()
        };
        var select = new Button
        {
            Text = tab.Title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 2, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = SideColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9.2F),
            Cursor = Cursors.Hand,
            Tag = tab
        };
        select.FlatAppearance.BorderSize = 0;
        select.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 38, 49);
        select.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 47, 72);
        select.Click += async (_, _) => await SelectTabAsync(tab);
        var close = new Button
        {
            Text = "×",
            Dock = DockStyle.Right,
            Width = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = SideColor,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI Semibold", 11F),
            Tag = tab
        };
        close.Cursor = Cursors.Hand;
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(82, 47, 62);
        close.FlatAppearance.MouseDownBackColor = Color.FromArgb(79, 45, 59);
        close.AccessibleName = "Fechar aba";
        close.Click += async (_, _) => await CloseTabAsync(tab);
        tab.CloseButton = close;
        row.Paint += (_, e) =>
        {
            if (_activeTab != tab) return;
            using var brush = new SolidBrush(AccentColor);
            e.Graphics.FillRectangle(brush, 1, 9, 3, Math.Max(1, row.Height - 18));
        };
        row.Controls.Add(select);
        row.Controls.Add(siteIcon);
        row.Controls.Add(close);
        tab.SelectButton = select;
        tab.SiteIcon = siteIcon;
        EventHandler mouseEnter = (_, _) => SetTabHovered(tab, true);
        EventHandler mouseLeave = (_, _) => QueueTabHoverCheck(tab, row);
        row.MouseEnter += mouseEnter;
        row.MouseLeave += mouseLeave;
        select.MouseEnter += mouseEnter;
        select.MouseLeave += mouseLeave;
        siteIcon.MouseEnter += mouseEnter;
        siteIcon.MouseLeave += mouseLeave;
        close.MouseEnter += mouseEnter;
        close.MouseLeave += mouseLeave;
        MouseEventHandler closeWithMiddleButton = (_, e) =>
        {
            if (e.Button == MouseButtons.Middle) _ = CloseTabAsync(tab);
        };
        row.MouseUp += closeWithMiddleButton;
        select.MouseUp += closeWithMiddleButton;
        siteIcon.MouseUp += closeWithMiddleButton;
        close.MouseUp += closeWithMiddleButton;
        return row;
    }

    private void SetTabHovered(BrowserTab tab, bool hovered)
    {
        if (tab.IsHovered == hovered) return;
        tab.IsHovered = hovered;
        UpdateTabRow(tab);
    }

    private void QueueTabHoverCheck(BrowserTab tab, Control row)
    {
        if (row.IsDisposed || !row.IsHandleCreated) return;
        try
        {
            row.BeginInvoke((Action)(() =>
            {
                if (row.IsDisposed) return;
                SetTabHovered(tab, row.RectangleToScreen(row.ClientRectangle).Contains(Cursor.Position));
            }));
        }
        catch (InvalidOperationException) { }
    }

    private static Bitmap CreateFallbackSiteIcon()
    {
        var bitmap = new Bitmap(20, 20);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(Color.FromArgb(57, 48, 78));
        using var pen = new Pen(Color.FromArgb(192, 177, 255), 1.4F);
        graphics.FillEllipse(background, 1, 1, 18, 18);
        graphics.DrawEllipse(pen, 4, 4, 12, 12);
        graphics.DrawArc(pen, 7, 4, 6, 12, 90, 180);
        graphics.DrawArc(pen, 7, 4, 6, 12, 270, 180);
        graphics.DrawLine(pen, 4, 10, 16, 10);
        return bitmap;
    }

    private async Task SelectTabAsync(BrowserTab tab)
    {
        var previousTab = _activeTab;
        _activeTab = tab;
        if (tab.IsHome)
        {
            _homePanel.Visible = true;
            _homePanel.BringToFront();
            _homeSearch.Focus();
        }
        if (previousTab is not null && previousTab.View is not null && previousTab != tab)
        {
            previousTab.View.Visible = false;
            try
            {
                if (previousTab.View.CoreWebView2 is { } oldCore) await oldCore.TrySuspendAsync();
            }
            catch { }
        }

        foreach (var current in _tabs)
            UpdateTabRow(current);
        _address.Text = tab.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? tab.Url : "";
        _stateLabel.Text = _blockerEnabled ? $"Bloqueador ligado · {_blockedRequests}" : "Bloqueador pausado";
        _homePanel.Visible = tab.IsHome;
        if (tab.IsHome)
        {
            RefreshHomeShortcuts();
            _homePanel.BringToFront();
        }
        await EnsureTabViewAsync(tab);
        if (tab.View is null) return;
        try { tab.View.CoreWebView2?.Resume(); } catch { }
        if (_activeTab == tab && !string.IsNullOrWhiteSpace(_pendingHomeSearch))
        {
            _homeSearch.Text = _pendingHomeSearch;
            _pendingHomeSearch = null;
            SubmitHomeSearch();
            return;
        }
        if (tab.IsHome)
        {
            tab.View.Visible = false;
            _homePanel.Visible = true;
            _homePanel.BringToFront();
            BeginInvoke(() => _homeSearch.Focus());
            return;
        }
        _homePanel.Visible = false;
        tab.View.Visible = true;
        tab.View.BringToFront();
        if (_toolbar.Visible) _toolbar.BringToFront();
        if (_sidebar.Visible) _sidebar.BringToFront();
    }

    private async Task EnsureTabViewAsync(BrowserTab tab)
    {
        if (tab.View is null)
        {
            tab.View = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = WindowColor,
                Visible = false,
                ZoomFactor = 1.0
            };
            tab.View.CoreWebView2InitializationCompleted += (_, e) =>
            {
                if (!e.IsSuccess || tab.View?.CoreWebView2 is null) return;
                ConfigureWebView(tab);
            };
            _page.Controls.Add(tab.View);
        }

        try
        {
            _environment ??= await CreateEnvironmentAsync();
            await tab.View.EnsureCoreWebView2Async(_environment);
            ConfigureWebView(tab);
            if (tab.Url == "nave://nova-guia")
            {
                tab.IsHome = true;
                tab.View.Visible = false;
            }
            else if (TryGetWebUri(tab.Url, out var uri))
            {
                tab.IsHome = false;
                tab.View.Source = uri;
            }
            else
            {
                tab.IsHome = true;
                tab.View.Visible = false;
            }
        }
        catch (Exception ex)
        {
            _stateLabel.Text = "Falha ao iniciar o motor do navegador";
            Debug.WriteLine(ex);
            MessageBox.Show(this, "Não consegui iniciar o motor do navegador. Confira se o Microsoft Edge WebView2 Runtime está instalado.", "Nave", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(UserDataFolder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
    }

    private void ConfigureWebView(BrowserTab tab)
    {
        if (tab.View?.CoreWebView2 is not { } core || tab.Configured) return;
        tab.Configured = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => FilterRequest(tab, core, e);
        core.NavigationStarting += (_, e) =>
        {
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                tab.IsHome = false;
                tab.Url = uri.ToString();
                SetSiteIcon(tab, CreateFallbackSiteIcon());
                if (_activeTab == tab) _address.Text = tab.Url;
            }
            else if (!string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                _stateLabel.Text = "Endereço bloqueado por segurança";
            }
        };
        core.FaviconChanged += async (_, _) => await UpdateSiteIconAsync(tab, core);
        core.NavigationCompleted += (_, _) =>
        {
            if (_activeTab == tab && !tab.Url.StartsWith("nave:", StringComparison.OrdinalIgnoreCase))
                _address.Text = tab.Url;
        };
        core.DocumentTitleChanged += (_, _) =>
        {
            string title = core.DocumentTitle;
            tab.Title = string.IsNullOrWhiteSpace(title) ? "Nova guia" : title;
            UpdateTabRow(tab);
        };
        core.NewWindowRequested += async (_, e) =>
        {
            e.Handled = true;
            if (TryGetWebUri(e.Uri, out var safeUri)) await NewTabAsync(safeUri.ToString());
        };
        core.WebMessageReceived += (_, e) => HandleHomeMessage(tab, e.WebMessageAsJson);
        core.DownloadStarting += (_, e) =>
        {
            e.Handled = false;
            _stateLabel.Text = "Download iniciado";
        };
    }

    private async Task UpdateSiteIconAsync(BrowserTab tab, CoreWebView2 core)
    {
        string faviconUri = core.FaviconUri;
        Image? image = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(faviconUri))
            {
                using Stream stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                using Image downloaded = Image.FromStream(stream);
                image = new Bitmap(downloaded);
            }
        }
        catch
        {
            // Sites without an available favicon keep the built-in globe icon.
        }

        if (tab.Row.IsDisposed || tab.View?.CoreWebView2 != core || core.FaviconUri != faviconUri)
        {
            image?.Dispose();
            return;
        }

        SetSiteIcon(tab, image ?? CreateFallbackSiteIcon());
        int bookmarkIndex = _bookmarks.FindIndex(bookmark => string.Equals(bookmark.Url, tab.Url, StringComparison.OrdinalIgnoreCase));
        if (bookmarkIndex >= 0)
        {
            string? iconData = CaptureTabIcon(tab);
            if (!string.IsNullOrWhiteSpace(iconData) && !string.Equals(_bookmarks[bookmarkIndex].IconData, iconData, StringComparison.Ordinal))
            {
                _bookmarks[bookmarkIndex] = _bookmarks[bookmarkIndex] with { IconData = iconData };
                try { SaveBookmarks(); } catch (Exception ex) { Debug.WriteLine(ex); }
                RefreshHomeShortcuts();
            }
        }
    }

    private static void SetSiteIcon(BrowserTab tab, Image image)
    {
        if (tab.Row.IsDisposed || tab.SiteIcon.IsDisposed)
        {
            image.Dispose();
            return;
        }
        var previous = tab.SiteIcon.Image;
        tab.SiteIcon.Image = image;
        previous?.Dispose();
    }

    private void FilterRequest(BrowserTab tab, CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!_blockerEnabled || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
        string requestHost = uri.IdnHost.TrimEnd('.');
        if (CurrentTabIsAllowed(tab) || !IsHostBlocked(requestHost)) return;
        e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Array.Empty<byte>()), 403, "Blocked", "Content-Type: text/plain");
        _blockedRequests++;
        if (_activeTab is not null) _stateLabel.Text = $"Bloqueador ligado · {_blockedRequests}";
    }

    private bool CurrentTabIsAllowed(BrowserTab tab)
    {
        if (!TryGetWebUri(tab.Url, out var pageUri)) return false;
        string pageHost = pageUri.IdnHost.TrimEnd('.');
        return _allowedSites.Any(allowedSite => HostMatchesDomain(pageHost, allowedSite));
    }

    private bool IsHostBlocked(string host)
    {
        if (!IsValidDomain(host)) return false;
        string candidate = host;
        while (true)
        {
            if (_blockedHosts.Contains(candidate)) return true;
            int dot = candidate.IndexOf('.');
            if (dot < 0 || dot == candidate.Length - 1) return false;
            candidate = candidate[(dot + 1)..];
        }
    }

    private static bool HostMatchesDomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private void HandleHomeMessage(BrowserTab sourceTab, string json)
    {
        if (!sourceTab.IsHome) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "search" && root.TryGetProperty("query", out var query))
                NavigateSearch(query.GetString() ?? "");
            else if (root.TryGetProperty("type", out type) && type.GetString() == "navigate" && root.TryGetProperty("url", out var url))
                NavigateTo(url.GetString() ?? "");
        }
        catch (JsonException) { }
    }

    private void NavigateFromAddress()
    {
        string text = _address.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            NavigateTo(absolute.ToString());
            return;
        }
        if (text.Contains(' ') || !text.Contains('.'))
        {
            NavigateSearch(text);
            return;
        }
        NavigateTo("https://" + text);
    }

    private void NavigateSearch(string query)
    {
        if (!string.IsNullOrWhiteSpace(query)) NavigateTo("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
    }

    private void NavigateTo(string url)
    {
        if (_activeTab?.View?.CoreWebView2 is null) return;
        if (!TryGetWebUri(url, out var uri)) return;
        _activeTab.IsHome = false;
        _activeTab.Url = uri.ToString();
        _homePanel.Visible = false;
        _activeTab.View.Visible = true;
        _activeTab.View.BringToFront();
        if (_toolbar.Visible) _toolbar.BringToFront();
        if (_sidebar.Visible) _sidebar.BringToFront();
        _activeTab.View.CoreWebView2.Navigate(_activeTab.Url);
        _address.Text = _activeTab.Url;
    }

    private static bool TryGetWebUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        if (!_tabs.Contains(tab)) return;
        _closedTabs.Push(new TabSnapshot(tab.Url, tab.Title));
        if (_tabs.Count <= 1)
        {
            tab.Url = "nave://nova-guia";
            tab.Title = "Nova guia";
            tab.IsHome = true;
            SetSiteIcon(tab, CreateFallbackSiteIcon());
            tab.View?.CoreWebView2?.Navigate("about:blank");
            tab.View!.Visible = false;
            _homePanel.Visible = true;
            _homePanel.BringToFront();
            _homeSearch.Clear();
            BeginInvoke(() => _homeSearch.Focus());
            UpdateTabRow(tab);
            return;
        }
        int index = _tabs.IndexOf(tab);
        bool wasActive = _activeTab == tab;
        _tabs.Remove(tab);
        _tabRows.Controls.Remove(tab.Row);
        UpdateSidebarTabCount();
        tab.SiteIcon.Image?.Dispose();
        tab.View?.Dispose();
        tab.Row.Dispose();
        if (wasActive) await SelectTabAsync(_tabs[Math.Max(0, index - 1)]);
    }

    private void UpdateTabRow(BrowserTab tab)
    {
        tab.SelectButton.Text = string.IsNullOrWhiteSpace(tab.Title) ? "Nova guia" : tab.Title;
        Color background = _activeTab == tab ? TabActiveColor : tab.IsHovered ? TabHoverColor : SideColor;
        tab.Row.BackColor = background;
        tab.SelectButton.BackColor = background;
        tab.SiteIcon.BackColor = background;
        tab.CloseButton.BackColor = background;
        tab.CloseButton.ForeColor = _activeTab == tab || tab.IsHovered ? TextColor : MutedColor;
        tab.Row.Invalidate();
    }

    private void UpdateSidebarTabCount()
    {
        _sidebarTabCount.Text = _tabs.Count.ToString();
        _sidebar.Height = GetSidebarHeight();
        _sidebar.Top = GetSidebarTop(_sidebar.Height);
    }

    private void UpdateSidebarPinButton()
    {
        _sidebarPinButton.ForeColor = _sidebarPinned ? AccentColor : MutedColor;
        _sidebarPinButton.BackColor = _sidebarPinned ? TabActiveColor : SideColor;
        _toolTips.SetToolTip(_sidebarPinButton, _sidebarPinned ? "Desafixar lateral" : "Fixar lateral (Ctrl+B)");
        _sidebarPinButton.AccessibleName = _sidebarPinned ? "Desafixar lateral" : "Fixar lateral";
    }

    private void ToggleBlocker()
    {
        _blockerEnabled = !_blockerEnabled;
        UpdateBlockerStatus();
    }

    private void ShowSettings()
    {
        MessageBox.Show(this,
            "Navegação usa o Microsoft Edge WebView2.\n\n" +
            "A barra e as abas aparecem ao passar o mouse pelas bordas superior e esquerda.\n" +
            "Clique com o botão direito em ‘Bloqueador’ para liberar um site ou atualizar a lista.\n" +
            "Atalhos: Ctrl+T nova aba, Ctrl+L/Ctrl+K pesquisar, Ctrl+Tab trocar abas, " +
            "Ctrl+Shift+T reabrir aba, Ctrl+D favorito, Ctrl+W fechar, Ctrl+R atualizar.",
            "Sobre o Nave", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void HandleShortcuts(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.T)
        {
            if (_closedTabs.TryPop(out var tab)) _ = NewTabAsync(tab.Url);
            else _ = NewTabAsync();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Tab)
        {
            CycleTabs(e.Shift ? -1 : 1);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && (e.KeyCode == Keys.L || e.KeyCode == Keys.K))
        {
            ShowToolbar(true);
            _address.Focus();
            _address.SelectAll();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.D)
        {
            AddCurrentFavorite();
            e.SuppressKeyPress = true;
        }
        else if ((e.Control && e.KeyCode == Keys.R) || e.KeyCode == Keys.F5)
        {
            _activeTab?.View?.CoreWebView2?.Reload();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.T)
        {
            _ = NewTabAsync();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.W)
        {
            if (_activeTab is not null) _ = CloseTabAsync(_activeTab);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.B)
        {
            _sidebarPinned = !_sidebarPinned;
            UpdateSidebarPinButton();
            ShowSidebar(_sidebarPinned);
            e.SuppressKeyPress = true;
        }
        else if (e.Alt && e.KeyCode == Keys.Left)
        {
            _activeTab?.View?.CoreWebView2?.GoBack();
            e.SuppressKeyPress = true;
        }
        else if (e.Alt && e.KeyCode == Keys.Right)
        {
            _activeTab?.View?.CoreWebView2?.GoForward();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape && _address.ContainsFocus)
        {
            _activeTab?.View?.Focus();
            e.SuppressKeyPress = true;
        }
    }

    private void CycleTabs(int direction)
    {
        if (_tabs.Count < 2 || _activeTab is null) return;
        int index = _tabs.IndexOf(_activeTab);
        int next = (index + direction + _tabs.Count) % _tabs.Count;
        _ = SelectTabAsync(_tabs[next]);
    }

    private const string HomePageHtml = """
        <!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <style>
        *{box-sizing:border-box}body{margin:0;background:#12141b;color:#eceaf3;font:14px 'Segoe UI',sans-serif;display:grid;place-items:center;min-height:100vh}
        main{width:min(590px,calc(100% - 48px));text-align:center;margin-top:-3vh}h1{font-size:23px;font-weight:500;letter-spacing:-.03em;margin:0 0 18px}
        form{height:48px;display:flex;align-items:center;gap:11px;padding:0 15px;border:1px solid #30323d;border-radius:13px;background:#191b24;box-shadow:0 5px 20px #0002}
        input{width:100%;border:0;outline:0;background:transparent;color:#eceaf3;font:13px 'Segoe UI',sans-serif}input::placeholder{color:#a6a4b2}
        .links{display:flex;justify-content:center;gap:8px;margin-top:18px}.links button{border:0;border-radius:8px;background:transparent;color:#a6a4b2;padding:7px 10px;font:12px 'Segoe UI',sans-serif;cursor:pointer}.links button:hover{background:#292b36;color:#eceaf3}
        </style><main><h1>Pesquisar na web</h1><form id="search"><span>⌕</span><input id="q" autofocus placeholder="Pesquise ou digite um endereço" autocomplete="off"><span>↵</span></form>
        <div class="links"><button data-url="https://www.google.com/">G&nbsp; Google</button><button data-url="https://www.youtube.com/">▶&nbsp; YouTube</button></div></main>
        <script>document.getElementById('search').addEventListener('submit',e=>{e.preventDefault();chrome.webview.postMessage({type:'search',query:document.getElementById('q').value})});document.querySelectorAll('[data-url]').forEach(b=>b.addEventListener('click',()=>chrome.webview.postMessage({type:'navigate',url:b.dataset.url})));</script>
        </html>
        """;

    private sealed class BrowserTab(int id, string url)
    {
        public int Id { get; } = id;
        public string Url { get; set; } = url;
        public string Title { get; set; } = "Nova guia";
        public bool IsHome { get; set; }
        public bool Configured { get; set; }
        public WebView2? View { get; set; }
        public Panel Row { get; set; } = new();
        public Button SelectButton { get; set; } = new();
        public Button CloseButton { get; set; } = new();
        public PictureBox SiteIcon { get; set; } = new();
        public bool IsHovered { get; set; }
    }

    private sealed record FavoriteItem(string Title, string Url, string? IconData = null);
    private sealed record TabSnapshot(string Url, string Title);
}
