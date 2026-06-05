using System.Collections.Generic;
using System.IO;

namespace NPlus.Core;

/// <summary>Recent-files list (max 10), persisted one path per line to recentfiles.txt.</summary>
public sealed class RecentFilesStore
{
    public const int MaxRecentFiles = 10;
    private readonly List<string> _files = new();

    public IReadOnlyList<string> Files => _files;

    public void Load()
    {
        _files.Clear();
        if (!File.Exists(AppPaths.RecentFilesFile)) return;
        try
        {
            foreach (var line in File.ReadAllLines(AppPaths.RecentFilesFile))
                if (!string.IsNullOrWhiteSpace(line) && _files.Count < MaxRecentFiles)
                    _files.Add(line.Trim());
        }
        catch { /* Ignore corrupt recent files */ }
    }

    public void Save()
    {
        try { File.WriteAllLines(AppPaths.RecentFilesFile, _files); }
        catch { /* Ignore write errors */ }
    }

    public void Add(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        _files.Remove(filePath);
        _files.Insert(0, filePath);
        if (_files.Count > MaxRecentFiles)
            _files.RemoveRange(MaxRecentFiles, _files.Count - MaxRecentFiles);
        Save();
    }

    public void Remove(string filePath)
    {
        if (_files.Remove(filePath)) Save();
    }
}
