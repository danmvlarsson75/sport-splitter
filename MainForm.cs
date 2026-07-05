using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;

namespace SportSplitter;

public enum LayoutType { LeftRight, TopBottom, ThreeLeft, ThreeTop, Quad, SixGrid, NineGrid }

public class MainForm : Form
{
    // ── Win32 for global hotkey ───────────────────────────────────────────────
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    const int HK_TOGGLE = 1;
    const uint MOD_CTRL = 0x0002, MOD_ALT = 0x0001;
    const uint VK_W = 0x57;
    const int WM_HOTKEY = 0x0312;

    // ── Theme ─────────────────────────────────────────────────────────────────
    static readonly Color C_BG      = Color.FromArgb(0x1a, 0x1a, 0x2e);
    static readonly Color C_SURFACE = Color.FromArgb(0x16, 0x21, 0x3e);
    static readonly Color C_ACCENT  = Color.FromArgb(0xe9, 0x45, 0x60);
    static readonly Color C_TEXT    = Color.FromArgb(0xea, 0xea, 0xea);
    static readonly Color C_MUTED   = Color.FromArgb(0x88, 0x88, 0x88);
    static readonly Color C_BORDER  = Color.FromArgb(0x2a, 0x2a, 0x4a);
    static readonly Color C_LABEL   = Color.FromArgb(0x88, 0x99, 0xbb);

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = Math.Max(1, radius) * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private float _dpiScale = 1f;
    private readonly Config _config = Config.Load();
    private readonly List<BrowserWindow> _windows = new();
    private CoreWebView2Environment? _env;
    private readonly AudioManager _audio;
    private readonly TextBox[] _urlBoxes = new TextBox[9];
    private NotifyIcon _tray = null!;
    private Label _status = null!;
    private bool _coverTaskbar = false;
    private bool _layoutBusy = false;
    private bool _allowShow = false;
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 3000 };

    public MainForm()
    {
        _audio = new AudioManager(() => _windows.AsReadOnly());
        _audio.Enabled = _config.AudioFollowsMouse;
        _statusTimer.Tick += (_, _) => { _status.Text = ""; _statusTimer.Stop(); };

        SuspendLayout();
        BuildUI();
        BuildTray();
        ResumeLayout(true);

    }

    // Start hidden: swallow the initial Show from Application.Run (Hide() in
    // the Load event doesn't stick — the show sequence re-asserts visibility
    // after Load returns). The handle is still created so the global hotkey
    // and WndProc work while hidden.
    protected override void SetVisibleCore(bool value)
    {
        if (!_allowShow)
        {
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!RegisterHotKey(Handle, HK_TOGGLE, MOD_CTRL | MOD_ALT, VK_W))
            _tray?.ShowBalloonTip(3000, "Sport Splitter",
                "Global hotkey Ctrl+Alt+W is unavailable (in use by another app). " +
                "Use the tray icon to open the panel.", ToolTipIcon.Warning);
    }

    // ── UI Construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        Text            = "Sport Splitter";
        var icoPath     = Path.Combine(AppContext.BaseDirectory, "icons", "app.ico");
        if (File.Exists(icoPath)) Icon = new Icon(icoPath);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = false;
        BackColor       = C_BG;
        ForeColor       = C_TEXT;
        Font            = new Font("Segoe UI", 9f);
        StartPosition   = FormStartPosition.Manual;
        TopMost         = true;
        ShowInTaskbar   = false;
        AutoScaleMode   = AutoScaleMode.None;

        // DeviceDpi can still be 96 before the form handle exists, so use the
        // process-aware system DPI for the initial layout.
        _dpiScale = GetInitialDpiScale();
        int S(int v) => (int)Math.Round(v * _dpiScale);

        int W   = S(440);
        int pad = S(20);
        int y   = 0;

        // Scrollable content panel — fills the form, scrolls when content exceeds form height
        var scroll = new Panel
        {
            Location  = Point.Empty,
            BackColor = C_BG,
            AutoScroll = true
        };
        void Add(Control c) => scroll.Controls.Add(c);

        // Header — two-tone title, subtitle, gradient accent rule
        int headerH = S(62);
        var header = new Panel { Location = Point.Empty, Size = new Size(W, headerH), BackColor = C_BG };
        header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var titleFont = new Font("Segoe UI Semibold", 13f);
            using var subFont   = new Font("Segoe UI", 8f);
            const string t1 = "Sport ", t2 = "Splitter";
            var s1 = g.MeasureString(t1, titleFont);
            var s2 = g.MeasureString(t2, titleFont);
            float tx = (W - s1.Width - s2.Width) / 2f;
            float ty = S(7);
            using (var accentBrush = new SolidBrush(C_ACCENT))
            using (var textBrush   = new SolidBrush(C_TEXT))
            {
                g.DrawString(t1, titleFont, accentBrush, tx, ty);
                g.DrawString(t2, titleFont, textBrush, tx + s1.Width, ty);
            }
            using (var mutedBrush = new SolidBrush(C_MUTED))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString("Tile streams across your screen", subFont, mutedBrush,
                    new RectangleF(0, ty + s1.Height + S(1), W, S(16)), sf);
            var ruleRect = new Rectangle(0, headerH - 2, W, 2);
            using var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                ruleRect, C_BG, C_BG, 0f)
            {
                InterpolationColors = new System.Drawing.Drawing2D.ColorBlend
                {
                    Colors    = new[] { C_BG, C_ACCENT, C_BG },
                    Positions = new[] { 0f, 0.5f, 1f }
                }
            };
            g.FillRectangle(grad, ruleRect);
        };
        Add(header);
        y = headerH;

        // URLs
        Add(SectionLabel("URLs", y + S(5), pad)); y += S(24);
        for (int i = 0; i < 9; i++)
        {
            int idx = i;
            Add(new NumberBadge(i + 1)
            {
                Location = new Point(pad, y + S(4)), Size = new Size(S(18), S(18)),
                BackColor = C_BG
            });
            var wrap = new UrlInputPanel
            {
                Location = new Point(pad + S(24), y),
                Size = new Size(W - pad * 2 - S(24), S(26)),
                BackColor = C_BG
            };
            var box = wrap.Box;
            box.Text = _config.Urls[i];
            box.TextChanged += (_, _) => { _config.Urls[idx] = box.Text.Trim(); _config.Save(); };
            box.Leave += (_, _) => NavigateSlot(idx);
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    NavigateSlot(idx);
                }
            };
            _urlBoxes[i] = box;
            Add(wrap);
            y += S(32);
        }
        y += S(4);

        // Layout sections
        int btnW2 = (W - pad * 2 - S(8)) / 2;

        void LayoutSection(string title, Action addBtns, int btnH)
        {
            Add(HRule(y, W)); y += 1;
            Add(SectionLabel(title, y + S(5), pad)); y += S(24);
            addBtns();
            y += btnH + S(10);
        }

        LayoutSection("2 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.LeftRight, "Left / Right",   pad,                 y, btnW2, S(80));
            AddLayoutBtn(scroll, LayoutType.TopBottom, "Top / Bottom",   pad + btnW2 + S(8),  y, btnW2, S(80));
        }, S(80));

        LayoutSection("3 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.ThreeLeft, "Main Left", pad,                 y, btnW2, S(80));
            AddLayoutBtn(scroll, LayoutType.ThreeTop,  "Main Top",  pad + btnW2 + S(8),  y, btnW2, S(80));
        }, S(80));

        LayoutSection("4 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.Quad,    "Quad Grid",  pad, y, W - pad * 2, S(80));
        }, S(80));

        LayoutSection("6 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.SixGrid, "2 × 3 Grid", pad, y, W - pad * 2, S(100));
        }, S(100));

        LayoutSection("9 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.NineGrid, "3 × 3 Grid", pad, y, W - pad * 2, S(110));
        }, S(110));

        // Options
        Add(HRule(y, W)); y += 1;
        Add(SectionLabel("Options", y + S(5), pad)); y += S(24);
        AddToggleRow(scroll, "Audio follows mouse", _config.AudioFollowsMouse, pad, y, W, out var audioToggle);
        audioToggle.Toggled += on =>
        {
            _audio.Enabled = on;
            _config.AudioFollowsMouse = on;
            _config.Save();
            if (!on) _audio.UnmuteAll();
        };
        y += S(32);
        AddToggleRow(scroll, "Cover taskbar (fullscreen)", false, pad, y, W, out var taskbarToggle);
        taskbarToggle.Toggled += on => _coverTaskbar = on;
        y += S(32);
        y += S(8);

        // ── Fixed footer (never scrolls away) ────────────────────────────────
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string verText = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "";

        int footerH = S(74);
        // Give the footer its final width up front: children anchored
        // Left|Right take their resize baseline from the parent's width at
        // add time, and the dock only stretches the panel later.
        var footer = new Panel
        {
            BackColor = C_BG,
            Size = new Size(W + SystemInformation.VerticalScrollBarWidth, footerH),
            Dock = DockStyle.Bottom
        };

        int fy = 0;
        footer.Controls.Add(new Panel { Location = Point.Empty, Size = new Size(W + SystemInformation.VerticalScrollBarWidth, 1), BackColor = Color.FromArgb(0x2a, 0x2a, 0x4a) });
        fy += S(10);

        var closeBtn = new PillButton("Close All Windows")
        {
            Location = new Point(pad, fy), Size = new Size(W - pad * 2, S(30)),
            BackColor = C_BG,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        closeBtn.Click += (_, _) => CloseAllWindows();
        footer.Controls.Add(closeBtn);
        fy += S(38);

        _status = new Label
        {
            Text = "", ForeColor = C_MUTED, BackColor = C_BG,
            Location = new Point(pad, fy), Size = new Size(W - pad * 2, S(18)),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8f), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        footer.Controls.Add(_status);

        footer.Controls.Add(new Label
        {
            Text = verText, ForeColor = C_MUTED, BackColor = C_BG,
            Location = new Point(pad, fy), Size = new Size(W - pad * 2, S(18)),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 7.5f), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        });

        // ── Size form ─────────────────────────────────────────────────────────
        int sbW        = SystemInformation.VerticalScrollBarWidth;
        int formW      = W + sbW;
        var screen     = Screen.PrimaryScreen!.WorkingArea;
        int nonClientH = SystemInformation.CaptionHeight
                       + SystemInformation.FrameBorderSize.Height * 2;
        int maxClientH = screen.Height - nonClientH - S(16);
        int formH      = Math.Min(y + footerH, maxClientH);
        ClientSize     = new Size(formW, formH);

        // Allow free resize in both directions
        MinimumSize = new Size(S(320), S(300));
        MaximumSize = Size.Empty;

        // Footer docked to bottom first so Fill scroll doesn't overlap it
        Controls.Add(footer);

        scroll.Dock = DockStyle.Fill;
        scroll.AutoScrollMinSize = new Size(0, y);
        Controls.Add(scroll);

        // Position top-right; clamp so form never starts off the bottom of the screen
        int margin = S(16);
        int left   = screen.Right - Width - margin;
        int top    = Math.Min(screen.Top + margin, screen.Bottom - Height);
        Location   = new Point(left, top);
    }

    private void AddLayoutBtn(Panel parent, LayoutType layout, string label, int x, int y, int w, int h)
    {
        var btn = new LayoutButton(layout, label) { Location = new Point(x, y), Size = new Size(w, h) };
        btn.Clicked += async () => await ApplyLayoutAsync(layout);
        parent.Controls.Add(btn);
    }

    private void AddToggleRow(Panel parent, string text, bool on, int pad, int y, int W, out ToggleButton toggle)
    {
        int s(int v) => (int)Math.Round(v * _dpiScale);
        parent.Controls.Add(new Label
        {
            Text = text, ForeColor = C_TEXT, BackColor = C_BG,
            Location = new Point(pad, y), Size = new Size(W - pad * 2 - s(56), s(26)),
            TextAlign = ContentAlignment.MiddleLeft
        });
        toggle = new ToggleButton { Location = new Point(W - pad - s(50), y + s(2)), Size = new Size(s(50), s(22)) };
        toggle.SetOn(on);
        parent.Controls.Add(toggle);
    }

    private void BuildTray()
    {
        var icoPath2 = Path.Combine(AppContext.BaseDirectory, "icons", "tray.ico");
        if (!File.Exists(icoPath2)) icoPath2 = Path.Combine(AppContext.BaseDirectory, "icons", "app.ico");
        _tray = new NotifyIcon
        {
            Text = "Sport Splitter",
            Icon = File.Exists(icoPath2) ? new Icon(icoPath2, 16, 16) : SystemIcons.Application,
            Visible = true
        };
        var menu = new ContextMenuStrip { BackColor = C_BG, ForeColor = C_TEXT };
        menu.Items.Add("Show  (Ctrl+Alt+W)", null, (_, _) => ShowPanel());
        menu.Items.Add("Bring Windows to Front", null, (_, _) => BringWindowsToFront());
        menu.Items.Add("Close All Windows", null, (_, _) => CloseAllWindows());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { CloseAllWindows(); Application.Exit(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowPanel();
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private async Task ApplyLayoutAsync(LayoutType layout)
    {
        // Awaits below yield to the message loop, so a second click could
        // interleave and create duplicate windows.
        if (_layoutBusy) return;
        _layoutBusy = true;
        try
        {
            await ApplyLayoutCoreAsync(layout);
        }
        finally
        {
            _layoutBusy = false;
        }
    }

    private async Task ApplyLayoutCoreAsync(LayoutType layout)
    {
        int count = layout switch
        {
            LayoutType.NineGrid => 9,
            LayoutType.SixGrid  => 6,
            LayoutType.Quad     => 4,
            LayoutType.ThreeLeft or LayoutType.ThreeTop => 3,
            _ => 2
        };

        SetStatus("Opening windows...");

        try
        {
            _env ??= await CreateEnvAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 Runtime not found.\n\n{ex.Message}\n\n" +
                "Please install the Microsoft Edge WebView2 Runtime:\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/",
                "WebView2 Required", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("WebView2 not found.");
            return;
        }

        // Create missing windows
        while (_windows.Count < count)
        {
            int idx = _windows.Count;
            string url = idx < _config.Urls.Length ? _config.Urls[idx] : "";
            var win = new BrowserWindow(idx, url);
            _windows.Add(win);
            win.Show();
            await win.InitializeAsync(_env);
        }

        // Calculate bounds
        var work = _coverTaskbar ? Screen.PrimaryScreen!.Bounds : Screen.PrimaryScreen!.WorkingArea;
        int l = work.Left, t = work.Top, w = work.Width, h = work.Height;
        int hw = w / 2, hh = h / 2;

        var bounds = layout switch
        {
            LayoutType.LeftRight => new[]
            {
                new Rectangle(l,      t, hw,      h),
                new Rectangle(l + hw, t, w - hw,  h)
            },
            LayoutType.TopBottom => new[]
            {
                new Rectangle(l, t,       w, hh),
                new Rectangle(l, t + hh,  w, h - hh)
            },
            LayoutType.ThreeLeft => new[]
            {
                new Rectangle(l,      t,      hw,     h),
                new Rectangle(l + hw, t,      w - hw, hh),
                new Rectangle(l + hw, t + hh, w - hw, h - hh)
            },
            LayoutType.ThreeTop => new[]
            {
                new Rectangle(l,      t,      w,      hh),
                new Rectangle(l,      t + hh, hw,     h - hh),
                new Rectangle(l + hw, t + hh, w - hw, h - hh)
            },
            LayoutType.Quad => new[]
            {
                new Rectangle(l,      t,      hw,     hh),
                new Rectangle(l + hw, t,      w - hw, hh),
                new Rectangle(l,      t + hh, hw,     h - hh),
                new Rectangle(l + hw, t + hh, w - hw, h - hh)
            },
            LayoutType.SixGrid => Grid(l, t, w, h, cols: 2, rows: 3),
            LayoutType.NineGrid => Grid(l, t, w, h, cols: 3, rows: 3),
            _ => Array.Empty<Rectangle>()
        };

        // Position and show required windows
        for (int i = 0; i < count; i++)
        {
            _windows[i].Bounds = bounds[i];
            _windows[i].Show();
        }

        // Hide windows beyond the needed count
        for (int i = count; i < _windows.Count; i++)
            _windows[i].Hide();

        SetStatus($"{count} windows arranged.");
    }

    private void CloseAllWindows()
    {
        _audio.UnmuteAll();
        foreach (var w in _windows)
            w.Dispose();
        _windows.Clear();
        SetStatus("All windows closed.");
    }

    private static Rectangle[] Grid(int l, int t, int w, int h, int cols, int rows)
    {
        var rects = new Rectangle[cols * rows];
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int x  = l + c * w / cols;
            int y  = t + r * h / rows;
            int rw = l + (c + 1) * w / cols - x;
            int rh = t + (r + 1) * h / rows - y;
            rects[r * cols + c] = new Rectangle(x, y, rw, rh);
        }
        return rects;
    }

    private static async Task<CoreWebView2Environment> CreateEnvAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebSplitter", "BrowserData");
        return await CoreWebView2Environment.CreateAsync(null, userDataFolder);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void BringWindowsToFront()
    {
        foreach (var w in _windows)
            if (w.Visible) { w.BringToFront(); w.Activate(); }
    }

    private void ShowPanel()
    {
        _allowShow = true;
        Show();
        WindowState = FormWindowState.Normal;
        TopMost = true;   // re-assert so it rises above browser windows
        Activate();
        BringToFront();
    }

    private void NavigateSlot(int idx)
    {
        if (idx >= _windows.Count) return;
        var win = _windows[idx];
        string url = _config.Urls[idx];
        if (!string.IsNullOrWhiteSpace(url) && url != win.CurrentUrl)
            win.NavigateTo(url);
    }

    private void SetStatus(string msg)
    {
        _status.Text = msg;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private Control SectionLabel(string text, int y, int pad)
    {
        int s(int v) => (int)Math.Round(v * _dpiScale);
        var p = new Panel
        {
            Location = new Point(pad, y),
            Size = new Size(s(400), s(18)),
            BackColor = C_BG
        };
        p.Paint += (_, e) =>
        {
            var g = e.Graphics;
            using (var bar = new SolidBrush(C_ACCENT))
                g.FillRectangle(bar, 0, s(4), s(3), p.Height - s(8));
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var font  = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var brush = new SolidBrush(C_LABEL);
            using var sf    = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(text.ToUpperInvariant(), font, brush,
                new RectangleF(s(9), 0, p.Width - s(9), p.Height), sf);
        };
        return p;
    }

    private static Panel HRule(int y, int W) => new()
    {
        Location = new Point(0, y), Size = new Size(W, 1),
        BackColor = C_BORDER
    };

    private static float GetInitialDpiScale()
    {
        try
        {
            uint dpi = GetDpiForSystem();
            if (dpi > 0)
                return dpi / 96f;
        }
        catch
        {
            // Fall back below if the API is unavailable for any reason.
        }

        using var screenGraphics = Graphics.FromHwnd(IntPtr.Zero);
        return screenGraphics.DpiX / 96f;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HK_TOGGLE)
        {
            if (Visible) Hide();
            else ShowPanel();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _config.Save();
        UnregisterHotKey(Handle, HK_TOGGLE);
        _audio.Dispose();
        _tray.Dispose();
        base.OnFormClosing(e);
    }

    // ── Nested Controls ───────────────────────────────────────────────────────

    private sealed class LayoutButton : Control
    {
        static readonly Color C_BG         = Color.FromArgb(0x16, 0x21, 0x3e);
        static readonly Color C_HOVER      = Color.FromArgb(0x0f, 0x34, 0x60);
        static readonly Color C_PANE       = Color.FromArgb(0x2a, 0x4a, 0x7f);
        static readonly Color C_PANE_HOVER = Color.FromArgb(0xb8, 0x37, 0x4d);
        static readonly Color C_TEXT       = Color.FromArgb(0xea, 0xea, 0xea);
        static readonly Color C_BORDER     = Color.FromArgb(0x2a, 0x2a, 0x5a);

        private bool _hover;
        public event Action? Clicked;
        private readonly LayoutType _layout;
        public string Label { get; }

        public LayoutButton(LayoutType layout, string label)
        {
            _layout = layout; Label = label;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Clicked?.Invoke(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float dpi = DeviceDpi / 96f;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(rect, (int)Math.Round(8 * dpi)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(_hover ? C_HOVER : C_BG))
                    g.FillPath(fill, path);
                using (var borderPen = new Pen(_hover ? C_ACCENT : C_BORDER))
                    g.DrawPath(borderPen, path);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            }

            // Draw layout preview using 16:9 aspect ratio, centered in the top portion
            int labelH  = (int)Math.Round(22 * dpi);
            int margin  = (int)Math.Round(8 * dpi);
            int availW  = Width  - margin * 2;
            int availH  = Height - labelH - margin * 2;
            // Fit a 16:9 rectangle inside available area
            int pw = availW;
            int ph = pw * 9 / 16;
            if (ph > availH) { ph = availH; pw = ph * 16 / 9; }
            int px = margin + (availW - pw) / 2;
            int py = margin + (availH - ph) / 2;
            var previewRect = new Rectangle(px, py, pw, ph);

            using var paneBrush = new SolidBrush(_hover ? C_PANE_HOVER : C_PANE);
            using var panePen   = new Pen(_hover ? C_HOVER : C_BG, 2);
            DrawLayoutPreview(g, paneBrush, panePen, previewRect, _layout);

            // Label
            using var sf   = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var font = new Font("Segoe UI", 8f);
            using var textBrush = new SolidBrush(C_TEXT);
            g.DrawString(Label, font, textBrush,
                new RectangleF(0, Height - labelH, Width, labelH), sf);
        }

        private static void DrawLayoutPreview(Graphics g, SolidBrush fill, Pen gap,
            Rectangle r, LayoutType layout)
        {
            int l = r.Left, t = r.Top, w = r.Width, h = r.Height;
            int hw = w / 2, hh = h / 2;

            void Pane(int x, int y, int pw, int ph)
            {
                g.FillRectangle(fill, x, y, pw, ph);
                g.DrawRectangle(gap, x, y, pw, ph);
            }

            switch (layout)
            {
                case LayoutType.LeftRight:
                    Pane(l, t, hw - 1, h); Pane(l + hw + 1, t, w - hw - 2, h); break;
                case LayoutType.TopBottom:
                    Pane(l, t, w, hh - 1); Pane(l, t + hh + 1, w, h - hh - 2); break;
                case LayoutType.ThreeLeft:
                    Pane(l, t, hw - 1, h);
                    Pane(l + hw + 1, t, w - hw - 2, hh - 1);
                    Pane(l + hw + 1, t + hh + 1, w - hw - 2, h - hh - 2); break;
                case LayoutType.ThreeTop:
                    Pane(l, t, w, hh - 1);
                    Pane(l, t + hh + 1, hw - 1, h - hh - 2);
                    Pane(l + hw + 1, t + hh + 1, w - hw - 2, h - hh - 2); break;
                case LayoutType.Quad:
                    Pane(l, t, hw - 1, hh - 1);
                    Pane(l + hw + 1, t, w - hw - 2, hh - 1);
                    Pane(l, t + hh + 1, hw - 1, h - hh - 2);
                    Pane(l + hw + 1, t + hh + 1, w - hw - 2, h - hh - 2); break;
                case LayoutType.SixGrid:
                    for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 2; col++)
                    {
                        int px = l + col * w / 2, py = t + row * h / 3;
                        int pw = w / 2 - 1,       ph = h / 3 - 1;
                        Pane(px + 1, py + 1, pw - 1, ph - 1);
                    }
                    break;
                case LayoutType.NineGrid:
                    for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        int px = l + col * w / 3, py = t + row * h / 3;
                        int pw = w / 3 - 1,       ph = h / 3 - 1;
                        Pane(px + 1, py + 1, pw - 1, ph - 1);
                    }
                    break;
            }
        }
    }

    private sealed class ToggleButton : Control
    {
        static readonly Color C_ON         = Color.FromArgb(0xe9, 0x45, 0x60);
        static readonly Color C_OFF        = Color.FromArgb(0x3a, 0x3a, 0x58);
        static readonly Color C_OFF_BORDER = Color.FromArgb(0x55, 0x55, 0x77);

        private bool _on;
        public event Action<bool>? Toggled;

        public ToggleButton() { Cursor = Cursors.Hand; DoubleBuffered = true; }

        public void SetOn(bool on) { _on = on; Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _on = !_on;
            Invalidate();
            Toggled?.Invoke(_on);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, (Height - 1) / 2);
            using var bg = new SolidBrush(_on ? C_ON : C_OFF);
            g.FillPath(bg, path);
            using var border = new Pen(_on ? C_ON : C_OFF_BORDER);
            g.DrawPath(border, path);
            int knobX = _on ? Width - Height : 0;
            using var knob = new SolidBrush(Color.White);
            g.FillEllipse(knob, knobX + 2, 2, Height - 4, Height - 4);
        }
    }

    private sealed class NumberBadge : Control
    {
        private readonly string _num;

        public NumberBadge(int n) { _num = n.ToString(); DoubleBuffered = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(C_SURFACE))
                g.FillEllipse(fill, 0, 0, Width - 1, Height - 1);
            using (var border = new Pen(C_BORDER))
                g.DrawEllipse(border, 0, 0, Width - 1, Height - 1);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var font  = new Font("Segoe UI", 7.5f);
            using var brush = new SolidBrush(C_LABEL);
            using var sf    = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(_num, font, brush, new RectangleF(0, 0, Width, Height), sf);
        }
    }

    private sealed class UrlInputPanel : Panel
    {
        public TextBox Box { get; } = new()
        {
            BorderStyle = BorderStyle.None,
            BackColor = C_SURFACE, ForeColor = C_TEXT,
            Font = new Font("Segoe UI", 9f)
        };

        private bool _focus;

        public UrlInputPanel()
        {
            DoubleBuffered = true;
            Controls.Add(Box);
            Box.Enter += (_, _) => { _focus = true; Invalidate(); };
            Box.Leave += (_, _) => { _focus = false; Invalidate(); };
            Resize += (_, _) =>
            {
                int padX = (int)Math.Round(9 * DeviceDpi / 96f);
                Box.SetBounds(padX, (Height - Box.PreferredHeight) / 2,
                    Width - padX * 2, Box.PreferredHeight);
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, (int)Math.Round(5 * DeviceDpi / 96f));
            using var fill = new SolidBrush(C_SURFACE);
            g.FillPath(fill, path);
            using var pen = new Pen(_focus ? C_ACCENT : C_BORDER);
            g.DrawPath(pen, path);
        }
    }

    private sealed class PillButton : Control
    {
        private bool _hover;

        public PillButton(string text) { Text = text; Cursor = Cursors.Hand; DoubleBuffered = true; }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, (Height - 1) / 2);
            using var fill = new SolidBrush(_hover ? C_ACCENT : C_SURFACE);
            g.FillPath(fill, path);
            using var pen = new Pen(C_ACCENT);
            g.DrawPath(pen, path);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var font  = new Font("Segoe UI Semibold", 9f);
            using var brush = new SolidBrush(_hover ? Color.White : C_ACCENT);
            using var sf    = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(Text, font, brush, new RectangleF(0, 0, Width, Height), sf);
        }
    }
}
