using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SportSplitter;

public class BrowserWindow : Form
{
    private readonly WebView2 _webView = new();
    private bool _initialized = false;

    public int SlotIndex { get; }
    public string CurrentUrl { get; private set; }

    public BrowserWindow(int slotIndex, string url)
    {
        SlotIndex = slotIndex;
        CurrentUrl = url;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
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
            _webView.CoreWebView2.Navigate(CurrentUrl);
    }

    public void NavigateTo(string url)
    {
        CurrentUrl = url;
        if (_initialized && _webView.CoreWebView2 != null && !string.IsNullOrWhiteSpace(url))
            _webView.CoreWebView2.Navigate(url);
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
