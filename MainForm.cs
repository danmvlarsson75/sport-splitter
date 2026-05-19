using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;

namespace SportSplitter;

public enum LayoutType { LeftRight, TopBottom, ThreeLeft, ThreeTop, Quad, SixGrid, NineGrid }

public class MainForm : Form
{
    // ── Win32 for global hotkey ───────────────────────────────────────────────
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const int HK_TOGGLE = 1;
    const uint MOD_CTRL = 0x0002, MOD_ALT = 0x0001;
    const uint VK_W = 0x57;
    const int WM_HOTKEY = 0x0312;

    // ── Theme ─────────────────────────────────────────────────────────────────
    static readonly Color C_BG      = Color.FromArgb(0x1a, 0x1a, 0x2e);
    static readonly Color C_SURFACE = Color.FromArgb(0x16, 0x21, 0x3e);
    static readonly Color C_HOVER   = Color.FromArgb(0x0f, 0x34, 0x60);
    static readonly Color C_ACCENT  = Color.FromArgb(0xe9, 0x45, 0x60);
    static readonly Color C_PANE    = Color.FromArgb(0x2a, 0x4a, 0x7f);
    static readonly Color C_TEXT    = Color.FromArgb(0xea, 0xea, 0xea);
    static readonly Color C_MUTED   = Color.FromArgb(0x88, 0x88, 0x88);

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly Config _config = Config.Load();
    private readonly List<BrowserWindow> _windows = new();
    private CoreWebView2Environment? _env;
    private readonly AudioManager _audio;
    private readonly TextBox[] _urlBoxes = new TextBox[9];
    private NotifyIcon _tray = null!;
    private Label _status = null!;
    private bool _coverTaskbar = false;

    public MainForm()
    {
        _audio = new AudioManager(() => _windows.AsReadOnly());
        _audio.Enabled = _config.AudioFollowsMouse;

        SuspendLayout();
        BuildUI();
        BuildTray();
        ResumeLayout(true);

        Load += (_, _) =>
        {
            RegisterHotKey(Handle, HK_TOGGLE, MOD_CTRL | MOD_ALT, VK_W);
            Hide(); // start hidden; user opens via tray icon or Ctrl+Alt+W
        };
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

        const int W   = 440;
        const int pad = 20;
        int y = 0;

        // Scrollable content panel — fills the form, scrolls when content exceeds form height
        var scroll = new Panel
        {
            Location  = Point.Empty,
            BackColor = C_BG,
            AutoScroll = true
        };
        void Add(Control c) => scroll.Controls.Add(c);

        // Header
        Add(new Label
        {
            Text = "Sport Splitter", Font = new Font("Segoe UI Semibold", 12f),
            ForeColor = C_TEXT, BackColor = C_BG,
            Location = new Point(0, 0), Size = new Size(W, 42),
            TextAlign = ContentAlignment.MiddleCenter
        });
        y = 42;
        Add(HRule(y, W)); y += 1;

        // URLs
        Add(SectionLabel("URLs", y + 5, pad)); y += 24;
        for (int i = 0; i < 9; i++)
        {
            int idx = i;
            Add(new Label
            {
                Text = $"{i + 1}", ForeColor = C_MUTED, BackColor = C_BG,
                Location = new Point(pad, y + 5), Size = new Size(16, 20),
                TextAlign = ContentAlignment.MiddleRight
            });
            var box = new TextBox
            {
                Text = _config.Urls[i],
                Location = new Point(pad + 20, y),
                Size = new Size(W - pad * 2 - 20, 24),
                BackColor = C_SURFACE, ForeColor = C_TEXT,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
            box.TextChanged += (_, _) => { _config.Urls[idx] = box.Text.Trim(); _config.Save(); };
            _urlBoxes[i] = box;
            Add(box);
            y += 30;
        }
        y += 4;

        // Layout sections
        int btnW2 = (W - pad * 2 - 8) / 2;

        void LayoutSection(string title, Action addBtns, int btnH)
        {
            Add(HRule(y, W)); y += 1;
            Add(SectionLabel(title, y + 5, pad)); y += 24;
            addBtns();
            y += btnH + 10;
        }

        LayoutSection("2 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.LeftRight, "Left / Right",   pad,             y, btnW2, 80);
            AddLayoutBtn(scroll, LayoutType.TopBottom, "Top / Bottom",   pad + btnW2 + 8, y, btnW2, 80);
        }, 80);

        LayoutSection("3 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.ThreeLeft, "Main Left", pad,             y, btnW2, 80);
            AddLayoutBtn(scroll, LayoutType.ThreeTop,  "Main Top",  pad + btnW2 + 8, y, btnW2, 80);
        }, 80);

        LayoutSection("4 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.Quad,    "Quad Grid",  pad, y, W - pad * 2, 80);
        }, 80);

        LayoutSection("6 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.SixGrid, "2 × 3 Grid", pad, y, W - pad * 2, 100);
        }, 100);

        LayoutSection("9 Windows", () => {
            AddLayoutBtn(scroll, LayoutType.NineGrid, "3 × 3 Grid", pad, y, W - pad * 2, 110);
        }, 110);

        // Options
        Add(HRule(y, W)); y += 1;
        Add(SectionLabel("Options", y + 5, pad)); y += 24;
        AddToggleRow(scroll, "Audio follows mouse", _config.AudioFollowsMouse, pad, y, W, out var audioToggle);
        audioToggle.Toggled += on =>
        {
            _audio.Enabled = on;
            _config.AudioFollowsMouse = on;
            _config.Save();
            if (!on) _audio.UnmuteAll();
        };
        y += 32;
        AddToggleRow(scroll, "Cover taskbar (fullscreen)", false, pad, y, W, out var taskbarToggle);
        taskbarToggle.Toggled += on => _coverTaskbar = on;
        y += 32;

        // Actions
        Add(HRule(y, W)); y += 10;
        var closeBtn = MakeButton("Close All Windows", pad, y, W - pad * 2, C_ACCENT);
        closeBtn.Click += (_, _) => CloseAllWindows();
        Add(closeBtn);
        y += 38;

        // Version label
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string verText = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "";
        Add(new Label
        {
            Text = verText, ForeColor = C_MUTED, BackColor = C_BG,
            Location = new Point(pad, y), Size = new Size(W - pad * 2, 18),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 7.5f)
        });
        y += 22;

        _status = new Label
        {
            Text = "", ForeColor = C_MUTED, BackColor = C_BG,
            Location = new Point(pad, y), Size = new Size(W - pad * 2, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8f)
        };
        Add(_status);
        y += 28;

        // Size form to fit all content, capped at screen working area
        int sbW   = SystemInformation.VerticalScrollBarWidth;
        int formW = W + sbW;
        var screen = Screen.PrimaryScreen!.WorkingArea;

        // Set ClientSize once to let WinForms establish the non-client area (title bar + borders),
        // then use the actual non-client height to compute the correct maximum client height.
        ClientSize = new Size(formW, y + 8);
        int nonClientH  = Height - ClientSize.Height;
        int maxClientH  = screen.Height - nonClientH - 16;
        if (ClientSize.Height > maxClientH)
            ClientSize = new Size(formW, maxClientH);

        // Allow free resize in both directions
        MinimumSize = new Size(320, 300);
        MaximumSize = Size.Empty;

        scroll.Dock = DockStyle.Fill;
        scroll.AutoScrollMinSize = new Size(0, y);
        Controls.Add(scroll);

        // Position top-right; clamp so form never starts off the bottom of the screen
        int left = screen.Right - Width - 16;
        int top  = Math.Min(screen.Top + 16, screen.Bottom - Height);
        Location = new Point(left, top);
    }

    private void AddLayoutBtn(Panel parent, LayoutType layout, string label, int x, int y, int w, int h)
    {
        var btn = new LayoutButton(layout, label) { Location = new Point(x, y), Size = new Size(w, h) };
        btn.Clicked += async () => await ApplyLayoutAsync(layout);
        parent.Controls.Add(btn);
    }

    private void AddToggleRow(Panel parent, string text, bool on, int pad, int y, int W, out ToggleButton toggle)
    {
        parent.Controls.Add(new Label
        {
            Text = text, ForeColor = C_TEXT, BackColor = C_BG,
            Location = new Point(pad, y), Size = new Size(W - pad * 2 - 56, 26),
            TextAlign = ContentAlignment.MiddleLeft
        });
        toggle = new ToggleButton { Location = new Point(W - pad - 50, y + 2), Size = new Size(50, 22) };
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
        {
            w.FormClosed -= null;  // allow real close
            w.Dispose();
        }
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
        Show();
        WindowState = FormWindowState.Normal;
        TopMost = true;   // re-assert so it rises above browser windows
        Activate();
        BringToFront();
    }

    private void SetStatus(string msg)
    {
        _status.Text = msg;
        var t = new System.Windows.Forms.Timer { Interval = 3000 };
        t.Tick += (_, _) => { _status.Text = ""; t.Dispose(); };
        t.Start();
    }

    private static Label SectionLabel(string text, int y, int pad) => new()
    {
        Text = text.ToUpperInvariant(), ForeColor = Color.FromArgb(0x88, 0x99, 0xbb),
        BackColor = Color.FromArgb(0x1a, 0x1a, 0x2e),
        Location = new Point(pad, y), Size = new Size(400, 18),
        Font = new Font("Segoe UI", 7.5f, FontStyle.Bold)
    };

    private static Panel HRule(int y, int W) => new()
    {
        Location = new Point(0, y), Size = new Size(W, 1),
        BackColor = Color.FromArgb(0x2a, 0x2a, 0x4a)
    };

    private Button MakeButton(string text, int x, int y, int w, Color fg) => new()
    {
        Text = text, Location = new Point(x, y), Size = new Size(w, 28),
        FlatStyle = FlatStyle.Flat, BackColor = C_SURFACE, ForeColor = fg,
        Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        FlatAppearance = { BorderColor = Color.FromArgb(0x2a, 0x2a, 0x4a), BorderSize = 1 }
    };

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
        static readonly Color C_BG     = Color.FromArgb(0x16, 0x21, 0x3e);
        static readonly Color C_HOVER  = Color.FromArgb(0x0f, 0x34, 0x60);
        static readonly Color C_PANE   = Color.FromArgb(0x2a, 0x4a, 0x7f);
        static readonly Color C_TEXT   = Color.FromArgb(0xea, 0xea, 0xea);
        static readonly Color C_BORDER = Color.FromArgb(0x2a, 0x2a, 0x5a);

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
            g.Clear(_hover ? C_HOVER : C_BG);

            using var borderPen = new Pen(C_BORDER);
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            // Draw layout preview using 16:9 aspect ratio, centered in the top portion
            int labelH  = 22;
            int margin  = 8;
            int availW  = Width  - margin * 2;
            int availH  = Height - labelH - margin * 2;
            // Fit a 16:9 rectangle inside available area
            int pw = availW;
            int ph = pw * 9 / 16;
            if (ph > availH) { ph = availH; pw = ph * 16 / 9; }
            int px = margin + (availW - pw) / 2;
            int py = margin + (availH - ph) / 2;
            var previewRect = new Rectangle(px, py, pw, ph);

            using var paneBrush = new SolidBrush(C_PANE);
            using var panePen   = new Pen(Color.FromArgb(0x1a, 0x1a, 0x2e), 2);
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
        static readonly Color C_ON  = Color.FromArgb(0xe9, 0x45, 0x60);
        static readonly Color C_OFF = Color.FromArgb(0x44, 0x44, 0x66);

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
            using var bg = new SolidBrush(_on ? C_ON : C_OFF);
            g.FillEllipse(bg, 0, 0, Height, Height);
            g.FillEllipse(bg, Width - Height, 0, Height, Height);
            g.FillRectangle(bg, Height / 2, 0, Width - Height, Height);
            int knobX = _on ? Width - Height : 0;
            using var knob = new SolidBrush(Color.White);
            g.FillEllipse(knob, knobX + 2, 2, Height - 4, Height - 4);
        }
    }
}
