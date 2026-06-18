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
    public static string AiSettingsFile { get; }

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
        AiSettingsFile = Path.Combine(Root, "ai.json");

        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(BackupsDir);
            Directory.CreateDirectory(ScriptsDir);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Restricts a file to owner-only access where the platform supports it. Used for files
    /// that may hold secrets (e.g. ai.json). On Unix this sets mode 0600; on Windows the file
    /// already lives under the per-user %APPDATA% tree and any secrets are DPAPI-encrypted, so
    /// no extra ACL work is needed.
    /// </summary>
    public static void TryRestrictToOwner(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows() && File.Exists(path))
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* best effort */ }
    }
}
