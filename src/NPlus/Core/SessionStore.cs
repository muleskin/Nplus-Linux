using System.Collections.Generic;
using System.IO;

namespace NPlus.Core;

/// <summary>One persisted tab in the session snapshot (pipe-delimited in session.txt).</summary>
public sealed class SessionEntry
{
    public string OriginalPath { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public string TabTitle { get; set; } = "";
    public int ColorIndex { get; set; }
    /// <summary>True for the tab that was active when the session was saved.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Hot-exit session persistence: a list of open tabs plus per-tab text backups for
/// unsaved content. Mirrors the original session.txt + backups\backup_N.tmp scheme.
/// </summary>
public static class SessionStore
{
    /// <summary>Deletes all stale backup files (called before writing a fresh session).</summary>
    public static void ClearBackups()
    {
        try
        {
            var di = new DirectoryInfo(AppPaths.BackupsDir);
            if (di.Exists)
                foreach (var file in di.GetFiles()) file.Delete();
        }
        catch { /* Ignore locked files */ }
    }

    public static string BackupPathFor(int index) =>
        Path.Combine(AppPaths.BackupsDir, $"backup_{index}.tmp");

    public static void WriteBackup(string backupPath, string text)
    {
        try { File.WriteAllText(backupPath, text); } catch { }
    }

    public static void Save(IEnumerable<SessionEntry> entries)
    {
        var lines = new List<string>();
        foreach (var e in entries)
            lines.Add($"{e.OriginalPath}|{e.BackupPath}|{e.TabTitle}|{e.ColorIndex}|{(e.IsActive ? 1 : 0)}");
        try { File.WriteAllLines(AppPaths.SessionFile, lines); } catch { }
    }

    public static List<SessionEntry> Load()
    {
        var result = new List<SessionEntry>();
        if (!File.Exists(AppPaths.SessionFile)) return result;
        try
        {
            foreach (var line in File.ReadAllLines(AppPaths.SessionFile))
            {
                var parts = line.Split('|');
                if (parts.Length < 3) continue;
                int colorIndex = 0;
                if (parts.Length >= 4) int.TryParse(parts[3], out colorIndex);
                bool isActive = parts.Length >= 5 && parts[4].Trim() == "1";
                result.Add(new SessionEntry
                {
                    OriginalPath = parts[0],
                    BackupPath = parts[1],
                    TabTitle = parts[2],
                    ColorIndex = colorIndex,
                    IsActive = isActive,
                });
            }
        }
        catch { /* Ignore corrupt session file */ }
        return result;
    }
}
