using Velopack;
using Velopack.Sources;

namespace SportSplitter;

/// <summary>
/// Checks GitHub Releases for new versions, downloads them in the background,
/// and applies them on demand or when the app exits.
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/danmvlarsson75/sport-splitter";

    private readonly UpdateManager _mgr = new(new GithubSource(RepoUrl, null, prerelease: false));
    private UpdateInfo? _pending;

    /// <summary>Fires (possibly on a worker thread) when an update has been
    /// downloaded and is ready to apply. Argument is the new version.</summary>
    public event Action<string>? UpdateReady;

    public bool UpdatePending => _pending != null;

    public async Task CheckAndDownloadAsync()
    {
        // Not installed via Velopack (e.g. running from the IDE or a loose exe)
        if (!_mgr.IsInstalled) return;

        try
        {
            var info = await _mgr.CheckForUpdatesAsync();
            if (info == null) return;

            await _mgr.DownloadUpdatesAsync(info);
            _pending = info;
            UpdateReady?.Invoke(info.TargetFullRelease.Version.ToString());
        }
        catch
        {
            // Network or GitHub hiccups are non-fatal; we try again next launch.
        }
    }

    /// <summary>Applies the downloaded update immediately and restarts the app.</summary>
    public void ApplyAndRestart()
    {
        if (_pending != null)
            _mgr.ApplyUpdatesAndRestart(_pending);
    }

    /// <summary>Queues the downloaded update to install after the process exits.</summary>
    public void ApplyOnExit()
    {
        if (_pending != null)
            _mgr.WaitExitThenApplyUpdates(_pending);
    }
}
