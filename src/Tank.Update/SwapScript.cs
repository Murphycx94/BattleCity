using System.Text;

namespace Tank.Update;

/// <summary>
/// Generates the small script that finishes an update after the game exits: wait for the process to end,
/// move the old items aside, move the new ones in, relaunch; on any failure put the old ones back and relaunch those.
/// </summary>
public static class SwapScript
{
    public const string BackupDirName = ".tank3d-update-backup";

    public static string FileName(UpdatePlatform p) => p == UpdatePlatform.Windows ? "apply-update.cmd" : "apply-update.sh";

    public static string Build(InstallTarget target, string stagedDir, int pid, string logPath)
    {
        var items = target.Items(stagedDir);
        if (items.Count == 0) throw new UpdateException("Nothing to install.");
        return target.Platform == UpdatePlatform.Windows
            ? BuildCmd(target, stagedDir, items, pid, logPath)
            : BuildSh(target, stagedDir, items, pid, logPath);
    }

    private static string BuildSh(InstallTarget t, string staged, IReadOnlyList<SwapItem> items, int pid, string log)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/sh");
        sb.AppendLine("# Tank 3D updater: waits for the game to exit, swaps in the new version, relaunches it.");
        sb.AppendLine($"PID={pid}");
        sb.AppendLine($"STAGE={Q(staged)}");
        sb.AppendLine($"TARGET={Q(t.Directory)}");
        sb.AppendLine($"BACKUP=\"$TARGET\"/{BackupDirName}");
        sb.AppendLine($"exec >>{Q(log)} 2>&1");
        sb.AppendLine("echo \"$(date) updating in $TARGET\"");
        sb.AppendLine("n=0");
        sb.AppendLine("while kill -0 \"$PID\" 2>/dev/null; do n=$((n+1)); [ \"$n\" -gt 600 ] && { echo 'game did not exit'; exit 1; }; sleep 0.1; done");
        sb.AppendLine(t.Platform == UpdatePlatform.MacOS
            ? $"launch() {{ open {QT(t, t.RelaunchPath)}; }}"
            : $"launch() {{ ( cd \"$TARGET\" && nohup {QT(t, t.RelaunchPath)} >/dev/null 2>&1 & ); }}");
        sb.Append("restore() { echo 'update failed, restoring previous version';");
        foreach (var it in items)
            sb.Append($" if [ -e \"$BACKUP\"/{Q(it.TargetName)} ]; then rm -rf \"$TARGET\"/{Q(it.TargetName)}; mv \"$BACKUP\"/{Q(it.TargetName)} \"$TARGET\"/{Q(it.TargetName)}; fi;");
        sb.AppendLine(" launch; exit 1; }");
        sb.AppendLine("rm -rf \"$BACKUP\" && mkdir -p \"$BACKUP\" || { echo 'cannot create backup dir'; launch; exit 1; }");
        foreach (var it in items)
        {
            sb.AppendLine($"if [ -e \"$TARGET\"/{Q(it.TargetName)} ]; then mv \"$TARGET\"/{Q(it.TargetName)} \"$BACKUP\"/{Q(it.TargetName)} || restore; fi");
            sb.AppendLine($"mv \"$STAGE\"/{Q(it.StagedName)} \"$TARGET\"/{Q(it.TargetName)} || restore");
        }
        if (t.Platform == UpdatePlatform.Linux)
            sb.AppendLine($"chmod +x \"$TARGET\"/{Q(UpdateArchive.LinuxExe)} 2>/dev/null");
        sb.AppendLine("rm -rf \"$BACKUP\" \"$STAGE\"");
        sb.AppendLine("echo \"$(date) update installed\"");
        sb.AppendLine("launch");
        return sb.ToString();
    }

    private static string BuildCmd(InstallTarget t, string staged, IReadOnlyList<SwapItem> items, int pid, string log)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("rem Tank 3D updater: waits for the game to exit, swaps in the new version, relaunches it.");
        sb.AppendLine("setlocal EnableExtensions");
        sb.AppendLine($"set \"STAGE={staged}\"");
        sb.AppendLine($"set \"TARGET={t.Directory}\"");
        sb.AppendLine($"set \"BACKUP=%TARGET%\\{BackupDirName}\"");
        sb.AppendLine($"set \"LOG={log}\"");
        sb.AppendLine(">>\"%LOG%\" echo %date% %time% updating in %TARGET%");
        sb.AppendLine("set /a N=0");
        sb.AppendLine(":wait");
        sb.AppendLine($"tasklist /FI \"PID eq {pid}\" /NH 2>nul | find \" {pid} \" >nul || goto exited");
        sb.AppendLine("set /a N+=1");
        sb.AppendLine("if %N% gtr 60 (>>\"%LOG%\" echo game did not exit & exit /b 1)");
        sb.AppendLine("timeout /t 1 /nobreak >nul");
        sb.AppendLine("goto wait");
        sb.AppendLine(":exited");
        sb.AppendLine("if exist \"%BACKUP%\" rmdir /s /q \"%BACKUP%\"");
        sb.AppendLine("mkdir \"%BACKUP%\" || goto restore");
        foreach (var it in items)
        {
            string target = $"%TARGET%\\{it.TargetName}", backup = $"%BACKUP%\\{it.TargetName}", stagedItem = $"%STAGE%\\{it.StagedName}";
            sb.AppendLine($"if exist \"{target}\" move /y \"{target}\" \"{backup}\" >nul || goto restore");
            sb.AppendLine(it.IsDirectory
                ? $"robocopy \"{stagedItem}\" \"{target}\" /E /MOVE /NFL /NDL /NJH /NJS >nul & if errorlevel 8 goto restore"
                : $"move /y \"{stagedItem}\" \"{target}\" >nul || goto restore");
        }
        sb.AppendLine("rmdir /s /q \"%BACKUP%\" 2>nul");
        sb.AppendLine("rmdir /s /q \"%STAGE%\" 2>nul");
        sb.AppendLine(">>\"%LOG%\" echo %date% %time% update installed");
        sb.AppendLine("goto launch");
        sb.AppendLine(":restore");
        sb.AppendLine(">>\"%LOG%\" echo update failed, restoring previous version");
        foreach (var it in items)
        {
            string target = $"%TARGET%\\{it.TargetName}", backup = $"%BACKUP%\\{it.TargetName}";
            string remove = it.IsDirectory ? $"rmdir /s /q \"{target}\"" : $"del /f /q \"{target}\"";
            sb.AppendLine($"if exist \"{backup}\" ( if exist \"{target}\" {remove} ) & if exist \"{backup}\" move /y \"{backup}\" \"{target}\" >nul");
        }
        sb.AppendLine(":launch");
        sb.AppendLine($"start \"\" \"{t.RelaunchPath}\"");
        sb.AppendLine("exit /b 0");
        return sb.ToString();
    }

    /// <summary>POSIX single-quote quoting.</summary>
    internal static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Path of the relaunch target, expressed relative to $TARGET so it follows the swapped-in copy.</summary>
    private static string QT(InstallTarget t, string path) =>
        "\"$TARGET\"/" + Q(Path.GetRelativePath(t.Directory, path));
}
