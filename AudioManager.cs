namespace SportSplitter;

public class AudioManager : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private readonly Func<IReadOnlyList<BrowserWindow>> _getWindows;
    private BrowserWindow? _lastActive;

    public bool Enabled { get; set; }

    public AudioManager(Func<IReadOnlyList<BrowserWindow>> getWindows)
    {
        _getWindows = getWindows;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!Enabled) return;

        var cursor = Cursor.Position;
        var windows = _getWindows();

        BrowserWindow? active = null;
        foreach (var w in windows)
        {
            if (w.Visible && w.Bounds.Contains(cursor))
            {
                active = w;
                break;
            }
        }

        if (active == _lastActive) return;
        _lastActive = active;

        foreach (var w in windows)
            _ = w.SetMutedAsync(w != active);
    }

    public void UnmuteAll()
    {
        foreach (var w in _getWindows())
            _ = w.SetMutedAsync(false);
        _lastActive = null;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
