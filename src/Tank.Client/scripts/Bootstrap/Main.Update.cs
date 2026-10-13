using Godot;
using Tank.Client.Update;
using Tank.Update;

namespace Tank.Client.Bootstrap;

/// <summary>Online update: check and download in the background, offer "restart to update" on the main menu.</summary>
public partial class Main
{
    private const double UpdatePollSeconds = 0.25;
    private const double JustUpdatedNoticeSeconds = 10;

    private UpdateService? _update;
    private double _nextUpdatePoll;
    private string? _updateError;
    private bool _installing;

    private void InitUpdates()
    {
        _menu.SetVersion(UpdateService.CurrentVersionText());
        _update = UpdateService.TryCreate(_options.NoUpdate);
        if (_update is null) return;
        if (_update.JustUpdatedFrom is { } from) GD.Print($"[update] updated from v{from} to v{_update.Current}");
        _update.Start();
    }

    /// <summary>Called every frame while on the main menu.</summary>
    private void PollUpdates()
    {
        if (_update is null || _installing || _time < _nextUpdatePoll) return;
        _nextUpdatePoll = _time + UpdatePollSeconds;

        var u = _update.Updater;
        string version = u.Available?.Version.ToString() ?? "";
        string notes = u.Available is { Notes.Length: > 0 } m ? $"：{m.Notes}" : "";
        switch (u.Status)
        {
            case UpdateStatus.Downloading:
                _menu.SetUpdate($"正在下载新版本 v{version}…… {u.Progress:P0}");
                break;
            case UpdateStatus.Ready:
                if (_options.UpdateNow)
                {
                    InstallUpdate();
                    return;
                }
                _menu.SetUpdate(_updateError ?? $"新版本 v{version} 已下载好{notes}", "重启并更新", InstallUpdate);
                break;
            case UpdateStatus.ManualRequired:
                _menu.SetUpdate($"有新版本 v{version}，但游戏所在的位置不能自动更新，请手动下载安装。", "打开下载页",
                    () => OS.ShellOpen(UpdateDefaults.ReleasesUrl));
                break;
            case UpdateStatus.Failed:
                if (_updateError != u.Error)
                {
                    _updateError = u.Error;
                    GD.Print($"[update] check failed: {u.Error}"); // not worth a banner: retried next launch
                }
                _menu.SetUpdate(JustUpdatedText());
                break;
            default:
                _menu.SetUpdate(JustUpdatedText());
                break;
        }
    }

    private string? JustUpdatedText() =>
        _update?.JustUpdatedFrom is not null && _time < JustUpdatedNoticeSeconds ? $"已更新到 v{_update.Current}。" : null;

    private void InstallUpdate()
    {
        if (_update is null || _installing) return;
        if (_update.LaunchInstaller(out string error))
        {
            _installing = true;
            _menu.SetUpdate("正在安装更新，游戏马上会重新启动……");
            GD.Print("[update] installer launched, quitting");
            GetTree().Quit();
            return;
        }
        _updateError = $"自动更新失败：{error}";
        GD.PushWarning($"[update] {error}");
    }
}
