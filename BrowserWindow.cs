using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SportSplitter;

public class BrowserWindow : Form
{
    private readonly WebView2 _webView = new();
    private bool _initialized = false;

    public int SlotIndex { get; }
    public string CurrentUrl { get; private set; }

    // Hide from taskbar and Alt+Tab — managed via Sport Splitter panel
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_APPWINDOW  = 0x00040000;
            var cp = base.CreateParams;
            cp.ExStyle = (cp.ExStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
            return cp;
        }
    }

    public BrowserWindow(int slotIndex, string url)
    {
        SlotIndex = slotIndex;
        CurrentUrl = url;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Text = $"Sport Splitter - Window {slotIndex + 1}";

        _webView.Dock = DockStyle.Fill;
        _webView.DefaultBackgroundColor = Color.Black;
        Controls.Add(_webView);
    }

    public async Task InitializeAsync(CoreWebView2Environment env)
    {
        if (_initialized) return;
        _initialized = true;

        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

        // Hide scrollbars after every navigation (content still scrolls via mouse wheel)
        _webView.CoreWebView2.DOMContentLoaded += async (_, _) =>
        {
            await _webView.ExecuteScriptAsync(
                "var s=document.createElement('style');" +
                "s.textContent='::-webkit-scrollbar{display:none!important}';" +
                "document.head?.appendChild(s);");
        };

        if (!string.IsNullOrWhiteSpace(CurrentUrl))
            _webView.CoreWebView2.Navigate(NormalizeUrl(CurrentUrl));
    }

    public void NavigateTo(string url)
    {
        CurrentUrl = url;
        if (_initialized && _webView.CoreWebView2 != null && !string.IsNullOrWhiteSpace(url))
            _webView.CoreWebView2.Navigate(NormalizeUrl(url));
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (!url.Contains("://"))
            url = "https://" + url;
        return url;
    }

    public async Task SetMutedAsync(bool muted)
    {
        if (!_initialized || _webView.CoreWebView2 == null) return;
        try
        {
            string js = muted
                ? "document.querySelectorAll('video,audio').forEach(e=>e.muted=true);"
                : "document.querySelectorAll('video,audio').forEach(e=>e.muted=false);";
            await _webView.ExecuteScriptAsync(js);
        }
        catch { }
    }

    // Prevent Alt+F4 from closing individual windows accidentally
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
