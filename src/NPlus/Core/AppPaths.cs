using System;
using System.IO;

namespace NPlus.Core;

/// <summary>
/// Cross-platform per-user storage locations for nplus.
/// On Linux this resolves to ~/.config/nplus (XDG); on Windows to %APPDATA%\nplus.
/// Mirrors the original WinForms app's %APPDATA%\nplus layout.
/// </summary>
public static class AppPaths
{
    public static string Root { get; }
    public static string BackupsDir { get; }
    public static string ScriptsDir { get; }
    public static string SessionFile { get; }
    public static string SettingsFile { get; }
    public static string RecentFilesFile { get; }
    public static string MacrosFile { get; }

    static AppPaths()
    {
        // ApplicationData maps to %APPDATA% (Windows) and $XDG_CONFIG_HOME (~/.config) on Unix.
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        Root = Path.Combine(baseDir, "nplus");
        BackupsDir = Path.Combine(Root, "backups");
        ScriptsDir = Path.Combine(Root, "scripts");
        SessionFile = Path.Combine(Root, "session.txt");
        SettingsFile = Path.Combine(Root, "settings.txt");
        RecentFilesFile = Path.Combine(Root, "recentfiles.txt");
        MacrosFile = Path.Combine(Root, "macros.json");

        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(BackupsDir);
            Directory.CreateDirectory(ScriptsDir);
        }
        catch { /* best effort */ }
    }
}
