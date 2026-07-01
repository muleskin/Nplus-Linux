using System;
using System.Globalization;
using System.IO;

namespace NPlus.Core;

/// <summary>
/// Application settings persisted to settings.txt (one value per line), matching the
/// original WinForms 12-line format so existing config remains readable.
/// </summary>
public sealed class AppSettings
{
    public const double ZoomMin = 0.5;
    public const double ZoomMax = 3.0;

    public bool IsDarkMode { get; set; }
    public bool WordWrap { get; set; }
    public bool ShowCharacters { get; set; }
    public bool ShowIndentGuides { get; set; }
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public int WindowWidth { get; set; } = 1100;
    public int WindowHeight { get; set; } = 720;
    public bool IsMaximized { get; set; }
    public double ZoomLevel { get; set; } = 1.0;
    public bool CheckForUpdatesOnStartup { get; set; }
    public bool FoldingEnabled { get; set; } = true;
    public bool MultiLineTabs { get; set; }

    public bool HasSavedBounds => WindowX != int.MinValue && WindowY != int.MinValue;

    public static AppSettings Load()
    {
        var s = new AppSettings();
        if (!File.Exists(AppPaths.SettingsFile)) return s;
        try
        {
            string[] lines = File.ReadAllLines(AppPaths.SettingsFile);
            if (lines.Length >= 2)
            {
                bool.TryParse(lines[0], out var dm); s.IsDarkMode = dm;
                bool.TryParse(lines[1], out var ww); s.WordWrap = ww;
            }
            if (lines.Length >= 4)
            {
                bool.TryParse(lines[2], out var sc); s.ShowCharacters = sc;
                bool.TryParse(lines[3], out var ig); s.ShowIndentGuides = ig;
            }
            if (lines.Length >= 8 &&
                int.TryParse(lines[4], out var x) && int.TryParse(lines[5], out var y) &&
                int.TryParse(lines[6], out var w) && int.TryParse(lines[7], out var h))
            {
                s.WindowX = x; s.WindowY = y; s.WindowWidth = w; s.WindowHeight = h;
            }
            if (lines.Length >= 9 && bool.TryParse(lines[8], out var max)) s.IsMaximized = max;
            if (lines.Length >= 10 &&
                double.TryParse(lines[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) &&
                zoom >= ZoomMin && zoom <= ZoomMax)
                s.ZoomLevel = zoom;
            if (lines.Length >= 11 && bool.TryParse(lines[10], out var cu)) s.CheckForUpdatesOnStartup = cu;
            if (lines.Length >= 12 && bool.TryParse(lines[11], out var fold)) s.FoldingEnabled = fold;
            if (lines.Length >= 13 && bool.TryParse(lines[12], out var mlt)) s.MultiLineTabs = mlt;
        }
        catch { /* Ignore corrupt settings file */ }
        return s;
    }

    public void Save()
    {
        try
        {
            File.WriteAllLines(AppPaths.SettingsFile, new[]
            {
                IsDarkMode.ToString(),
                WordWrap.ToString(),
                ShowCharacters.ToString(),
                ShowIndentGuides.ToString(),
                WindowX.ToString(),
                WindowY.ToString(),
                WindowWidth.ToString(),
                WindowHeight.ToString(),
                IsMaximized.ToString(),
                ZoomLevel.ToString(CultureInfo.InvariantCulture),
                CheckForUpdatesOnStartup.ToString(),
                FoldingEnabled.ToString(),
                MultiLineTabs.ToString(),
            });
        }
        catch { /* Ignore write errors */ }
    }
}
