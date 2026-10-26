using Akki.ImageConverter.Core;
using Velopack;

namespace Akki.ImageConverter;

public sealed class UpdateService
{
    public const string FeedUrl = "https://update.akki.lu/akki-image-converter/win-x64/stable/";
    private readonly UpdateManager _manager = new(FeedUrl);
    private UpdateInfo? _downloaded;
    public bool IsInstalled => _manager.IsInstalled;
    public bool HasDownload => _downloaded is not null;
    public string Status { get; private set; } = "Not checked yet.";
    public string? ReleaseNotes { get; private set; }
    public string Version => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    public bool Busy { get; private set; }

    public async Task<bool> CheckAsync(Preferences preferences, bool download, Action<int>? progress = null)
    {
        if (Busy) return false;
        if (!IsInstalled) { Status = "Updates are available after installing a release build."; return false; }
        Busy = true;
        try
        {
            Status = "Checking for updates…";
            var update = await _manager.CheckForUpdatesAsync();
            preferences.LastUpdateCheck = DateTimeOffset.Now;
            preferences.Save();
            if (update is null) { Status = "You're up to date."; return false; }
            Status = $"Version {update.TargetFullRelease.Version} is available.";
            ReleaseNotes = update.TargetFullRelease.NotesMarkdown;
            if (download)
            {
                Status = "Downloading update…";
                await _manager.DownloadUpdatesAsync(update, p => progress?.Invoke(p));
                _downloaded = update;
                Status = "Update ready. It will install when you close the app.";
            }
            return true;
        }
        catch (Exception e)
        {
            Status = "Couldn't reach the update service. You can retry later. " + e.Message;
            return false;
        }
        finally { Busy = false; }
    }

    public void InstallOnExit() { if (_downloaded is not null) _manager.WaitExitThenApplyUpdates(_downloaded); }
    public void Restart() { if (_downloaded is not null) _manager.ApplyUpdatesAndRestart(_downloaded); }
}
