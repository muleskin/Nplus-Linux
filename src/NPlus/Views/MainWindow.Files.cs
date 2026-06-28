using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using NPlus.Controls;
using NPlus.Core;
using NPlus.Dialogs;
using NPlus.Models;

namespace NPlus.Views;

public enum SaveCloseChoice { Save, Discard, Cancel }

public partial class MainWindow
{
    // Above this, a text file opens in large-file mode (async load, read-only, no highlighting/folding).
    private const long LargeFileModeThreshold = 16L * 1024 * 1024;
    // Above this, prompt before opening — a full in-memory string this large risks running out of memory.
    private const long LargeTextWarn = 384L * 1024 * 1024;
    private const long LargeBinaryWarn = 256L * 1024 * 1024;

    // ---- New / Open ----

    private void NewTab() => AddNewTab($"new {_newCounter++}");

    private async void OpenFile()
    {
        // If there's no working native file-picker backend (common on minimal Linux
        // installs without an xdg-desktop-portal), fall back to a typed-path prompt.
        if (!StorageProvider.CanOpen)
        {
            await OpenByPathPrompt();
            return;
        }
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open file(s)",
                AllowMultiple = true,
            });
            if (files == null || files.Count == 0) return; // user cancelled
            var paths = files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToArray();
            OpenFilesFromPaths(paths);
        }
        catch (Exception)
        {
            // Native dialog backend unavailable/threw — use the manual fallback.
            await OpenByPathPrompt();
        }
    }

    /// <summary>Open a file by typing its path — a backend-independent fallback (also the File ▸ Open by Path… command).</summary>
    private async Task OpenByPathPrompt()
    {
        string start = ActiveDoc?.FilePath is { } p && !string.IsNullOrEmpty(p)
            ? Path.GetDirectoryName(p) + Path.DirectorySeparatorChar
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + Path.DirectorySeparatorChar;
        string? input = await MessageBoxes.Prompt(this, "Open file", "Enter the full path of the file to open:", start);
        if (string.IsNullOrWhiteSpace(input)) return;
        input = ExpandUserPath(input.Trim());
        if (!File.Exists(input)) { ShowMessage("n+", $"File not found:\n{input}"); return; }
        OpenFilesFromPaths(new[] { input });
    }

    private static string ExpandUserPath(string path)
    {
        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
        return path;
    }

    public void OpenFilesFromPaths(string[] paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path)) continue;

            // Already open? Just select it.
            var existing = _docs.Values.FirstOrDefault(d =>
                !string.IsNullOrEmpty(d.FilePath) &&
                string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { _tabs.SelectedItem = existing.TabItem; continue; }

            EditorDocument doc;
            // Reuse a single empty untitled tab.
            var blank = _docs.Values.FirstOrDefault(d =>
                d.FilePath == null && !d.IsDirty && d.Editor != null && d.Editor.Document.TextLength == 0);
            if (blank != null && _docs.Count >= 1)
            {
                doc = blank;
                _tabs.SelectedItem = doc.TabItem;
            }
            else
            {
                doc = AddNewTab(Path.GetFileName(path), path);
            }
            LoadFileIntoEditor(doc, path);
        }
        UpdateStatusBar();
    }

    // ---- Load ----

    private async void LoadFileIntoEditor(EditorDocument doc, string path)
    {
        if (!File.Exists(path)) { ShowMessage("n+", $"File not found:\n{path}"); return; }

        long size = new FileInfo(path).Length;
        bool binary = IsBinaryFile(path);

        if ((binary && size > LargeBinaryWarn) || (!binary && size > LargeTextWarn))
        {
            var r = await MessageBoxes.Show(this, "Large file",
                $"This file is {size / (1024 * 1024)} MB. Open it anyway?", MessageButtons.YesNo);
            if (r != MessageResult.Yes) return;
        }

        if (binary)
        {
            LoadBinaryIntoTab(doc, path);
            return;
        }

        bool large = size >= LargeFileModeThreshold;
        try
        {
            var enc = EncodingHelper.DetectFileEncoding(path);
            string name = Path.GetFileName(path);

            // Swap a hex tab back to a text editor if needed (must be on the UI thread).
            if (doc.Editor == null) ConvertHexTabToText(doc);

            bool locked;
            string content;
            if (large)
            {
                // Show a placeholder and read the file off the UI thread so the window stays responsive.
                doc.BaseTitle = name + "  [loading…]";
                doc.RaiseHeaderChanged();
                var loaded = await Task.Run(() =>
                {
                    string c = ReadAllTextShared(path, enc, out bool l);
                    return (text: c, locked: l);
                });
                // The tab may have been closed while we were reading.
                if (!_docs.ContainsKey(doc.TabItem)) return;
                content = loaded.text;
                locked = loaded.locked;
            }
            else
            {
                content = ReadAllTextShared(path, enc, out locked);
            }

            doc.IsLargeFile = large;
            doc.Encoding = enc;
            doc.FilePath = path;
            doc.IsReadOnly = locked || large; // large files are read-only
            ConfigureEditorForLargeMode(doc, large);
            SetEditorText(doc, content);
            doc.BaseTitle = name + (large ? "  [LARGE — read-only]" : (locked ? " [READ-ONLY]" : ""));
            doc.IsDirty = false;
            if (doc.Editor != null) doc.Editor.IsReadOnly = doc.IsReadOnly;
            ApplySyntax(doc); // a no-op grammar for large files (see ApplySyntax)
            doc.RaiseHeaderChanged();
            StartFileChangeWatch(doc);
            AddToRecent(path);
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            ShowMessage("n+", $"Could not open file:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Turns the heavy editor features off for large files (and restores them otherwise, so a tab
    /// reused for a normal file after a large one behaves normally again).
    /// </summary>
    private void ConfigureEditorForLargeMode(EditorDocument doc, bool large)
    {
        var ed = doc.Editor;
        if (ed == null) return;
        if (large)
        {
            UninstallFolding(doc);
            ed.Options.HighlightCurrentLine = false;
            ed.WordWrap = false;
        }
        else
        {
            ed.Options.HighlightCurrentLine = true;
            ed.WordWrap = _wordWrap;
            if (_foldingEnabled) InstallFolding(doc, ed);
        }
    }

    private void LoadBinaryIntoTab(EditorDocument doc, string path)
    {
        byte[] bytes = ReadAllBytesShared(path);
        var hex = new HexView(bytes, path);
        WireHexDirty(doc, hex);
        doc.Editor = null;
        doc.Hex = hex;
        doc.IsLargeFile = false;
        doc.TabItem.Content = hex;
        doc.FilePath = path;
        doc.BaseTitle = Path.GetFileName(path);
        doc.IsDirty = false;
        var bg = _isDarkMode ? new SolidColorBrush(Color.FromRgb(30, 30, 35)) : (IBrush)Brushes.White;
        var fg = _isDarkMode ? new SolidColorBrush(Color.FromRgb(240, 240, 240)) : (IBrush)Brushes.Black;
        hex.SetColors(bg, fg);
        hex.SetFontSize(13 * _zoom);
        doc.RaiseHeaderChanged();
        StartFileChangeWatch(doc);
        AddToRecent(path);
        UpdateToggleButtonVisuals();
    }

    private void ConvertHexTabToText(EditorDocument doc)
    {
        var editor = CreateEditor(doc);
        doc.Hex = null;
        doc.Editor = editor;
        doc.TabItem.Content = editor;
        ApplyViewOptions(editor);
        ApplyZoomToEditor(editor);
        if (_isDarkMode)
        {
            editor.Background = new SolidColorBrush(Color.FromRgb(30, 30, 35));
            editor.Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        }
        doc.TextMate?.SetTheme(_registryOptions.LoadTheme(_isDarkMode ? TextMateSharp.Grammars.ThemeName.DarkPlus : TextMateSharp.Grammars.ThemeName.LightPlus));
    }

    /// <summary>Sets editor text without leaving a dirty flag set.</summary>
    private void SetEditorText(EditorDocument doc, string text)
    {
        if (doc.Editor?.Document == null) return;
        doc.Editor.Document.Text = text;
        doc.IsDirty = false;
    }

    // ---- Save ----

    private void SaveFile()
    {
        var doc = ActiveDoc;
        if (doc == null) return;
        if (doc.FilePath == null) { SaveFileAs(); return; }
        SaveTab(doc);
    }

    private async void SaveAll()
    {
        var dirty = _docs.Values.Where(d => d.IsDirty).ToList();
        if (dirty.Count == 0) return;
        foreach (var d in dirty)
        {
            if (d.FilePath == null) { _tabs.SelectedItem = d.TabItem; await SaveFileAsAsync(d); }
            else SaveTab(d);
        }
    }

    private void SaveTab(EditorDocument doc)
    {
        if (doc.FilePath == null) { SaveFileAs(); return; }
        if (doc.IsReadOnly) { ShowMessage("n+", "This file is read-only and cannot be saved."); return; }
        try
        {
            StopFileChangeWatch(doc);
            if (doc.IsHex)
                File.WriteAllBytes(doc.FilePath, doc.Hex!.Bytes);
            else
                File.WriteAllText(doc.FilePath, doc.Editor!.Document.Text, doc.Encoding);
            doc.IsDirty = false;
            doc.BaseTitle = Path.GetFileName(doc.FilePath);
            ApplySyntax(doc);
            doc.RaiseHeaderChanged();
            StartFileChangeWatch(doc);
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            ShowMessage("n+", $"Could not save:\n{ex.Message}");
            StartFileChangeWatch(doc);
        }
    }

    private async void SaveFileAs()
    {
        var doc = ActiveDoc;
        if (doc != null) await SaveFileAsAsync(doc);
    }

    private async Task SaveFileAsAsync(EditorDocument doc)
    {
        string? path = null;

        if (StorageProvider.CanSave)
        {
            try
            {
                IStorageFolder? startFolder = null;
                if (!string.IsNullOrEmpty(doc.FilePath))
                {
                    var dir = Path.GetDirectoryName(doc.FilePath);
                    if (dir != null) startFolder = await StorageProvider.TryGetFolderFromPathAsync(dir);
                }
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save As",
                    SuggestedFileName = doc.FilePath != null ? Path.GetFileName(doc.FilePath) : doc.BaseTitle,
                    SuggestedStartLocation = startFolder,
                });
                if (file == null) return; // user cancelled
                path = file.TryGetLocalPath();
            }
            catch { /* fall through to the path prompt */ }
        }

        if (string.IsNullOrEmpty(path))
        {
            // No native save dialog — prompt for a destination path.
            string suggest = doc.FilePath ?? (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + Path.DirectorySeparatorChar + doc.BaseTitle);
            string? input = await MessageBoxes.Prompt(this, "Save As", "Enter the full path to save to:", suggest);
            if (string.IsNullOrWhiteSpace(input)) return;
            path = ExpandUserPath(input.Trim());
        }

        try
        {
            StopFileChangeWatch(doc);
            if (doc.IsHex)
                File.WriteAllBytes(path, doc.Hex!.Bytes);
            else
                File.WriteAllText(path, doc.Editor!.Document.Text, doc.Encoding);
            doc.FilePath = path;
            doc.IsReadOnly = false;
            if (doc.Editor != null) doc.Editor.IsReadOnly = false;
            doc.BaseTitle = Path.GetFileName(path);
            doc.IsDirty = false;
            ApplySyntax(doc);
            doc.RaiseHeaderChanged();
            AddToRecent(path);
            StartFileChangeWatch(doc);
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            ShowMessage("n+", $"Could not save:\n{ex.Message}");
        }
    }

    private async void RevertToSaved()
    {
        var doc = ActiveDoc;
        if (doc?.FilePath == null) return;
        var r = await MessageBoxes.Show(this, "Revert", "Discard unsaved changes and reload from disk?", MessageButtons.YesNo);
        if (r == MessageResult.Yes) LoadFileIntoEditor(doc, doc.FilePath);
    }

    private async Task<SaveCloseChoice> PromptSaveBeforeCloseAsync(EditorDocument doc)
    {
        var r = await MessageBoxes.Show(this, "n+",
            $"Save changes to {doc.BaseTitle}?", MessageButtons.YesNoCancel);
        return r switch
        {
            MessageResult.Yes => SaveCloseChoice.Save,
            MessageResult.No => SaveCloseChoice.Discard,
            _ => SaveCloseChoice.Cancel,
        };
    }

    // ---- Encoding ----

    private void SetEncoding(string name)
    {
        var doc = ActiveDoc;
        if (doc == null) return;
        doc.Encoding = EncodingHelper.GetEncodingFromName(name);
        if (!doc.IsDirty) doc.IsDirty = true;
        UpdateStatusBar();
    }

    private void ConvertEncoding(string name)
    {
        var doc = ActiveDoc;
        if (doc?.Editor == null) return;
        var target = EncodingHelper.GetEncodingFromName(name);
        byte[] currentBytes = doc.Encoding.GetBytes(doc.Editor.Document.Text);
        string converted = target.GetString(currentBytes);
        doc.Editor.Document.Text = converted;
        doc.Encoding = target;
        doc.IsDirty = true;
        UpdateStatusBar();
    }

    // ---- Hex view toggle ----

    private void ToggleHexView()
    {
        var doc = ActiveDoc;
        if (doc == null) return;
        if (doc.IsHex)
        {
            // Hex → text
            string text = doc.Encoding.GetString(doc.Hex!.Bytes);
            ConvertHexTabToText(doc);
            SetEditorText(doc, text);
            ApplySyntax(doc);
        }
        else if (doc.Editor != null)
        {
            // Text → hex
            byte[] bytes = doc.Encoding.GetBytes(doc.Editor.Document.Text);
            var hex = new HexView(bytes, doc.FilePath);
            WireHexDirty(doc, hex);
            doc.Editor = null;
            doc.Hex = hex;
            doc.TabItem.Content = hex;
            var bg = _isDarkMode ? new SolidColorBrush(Color.FromRgb(30, 30, 35)) : (IBrush)Brushes.White;
            var fg = _isDarkMode ? new SolidColorBrush(Color.FromRgb(240, 240, 240)) : (IBrush)Brushes.Black;
            hex.SetColors(bg, fg);
            hex.SetFontSize(13 * _zoom);
        }
        UpdateToggleButtonVisuals();
        UpdateStatusBar();
    }

    private void WireHexDirty(EditorDocument doc, HexView hex)
    {
        hex.BytesChanged += () =>
        {
            if (!doc.IsDirty) doc.IsDirty = true;
            UpdateStatusBar();
        };
    }

    // ---- Binary detection (ported from original IsBinaryFile) ----

    private static bool IsBinaryFile(string filePath)
    {
        byte[] head = new byte[8192];
        int len;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            len = fs.Read(head, 0, head.Length);
        if (len == 0) return false;

        if (len >= 2 && ((head[0] == 0xFE && head[1] == 0xFF) || (head[0] == 0xFF && head[1] == 0xFE)))
            return false; // UTF-16 BOM
        bool hasNull = false;
        for (int i = 0; i < len; i++) if (head[i] == 0) { hasNull = true; break; }
        if (!hasNull) return false;
        if (EncodingHelper.LooksLikeBomlessUtf16(head, len) != null) return false;
        return true;
    }

    private static string ReadAllTextShared(string path, Encoding enc, out bool locked)
    {
        locked = false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            locked = true;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    // ---- External-change watcher ----

    private void StartFileChangeWatch(EditorDocument doc)
    {
        if (string.IsNullOrEmpty(doc.FilePath) || doc.IsLive) return;
        StopFileChangeWatch(doc);
        try
        {
            var dir = Path.GetDirectoryName(doc.FilePath);
            if (string.IsNullOrEmpty(dir)) return;
            var w = new FileSystemWatcher(dir, Path.GetFileName(doc.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            w.Changed += (_, _) => Dispatcher.UIThread.Post(() => HandleFileChanged(doc));
            w.Deleted += (_, _) => Dispatcher.UIThread.Post(() => HandleFileDeleted(doc));
            w.Renamed += (_, e) => Dispatcher.UIThread.Post(() => HandleFileRenamed(doc, e.FullPath));
            w.EnableRaisingEvents = true;
            doc.ChangeWatcher = w;
        }
        catch { /* watching is best-effort */ }
    }

    private void StopFileChangeWatch(EditorDocument doc)
    {
        try { doc.ChangeWatcher?.Dispose(); } catch { }
        doc.ChangeWatcher = null;
    }

    private async void HandleFileChanged(EditorDocument doc)
    {
        if (doc.IsLive || doc.ChangePromptOpen || doc.FilePath == null || !File.Exists(doc.FilePath)) return;
        doc.ChangePromptOpen = true;
        try
        {
            var r = await MessageBoxes.Show(this, "File changed on disk",
                $"{Path.GetFileName(doc.FilePath)} was modified by another program.\n\nReload it?\n(Yes = reload, No = keep my version and mark modified)",
                MessageButtons.YesNo);
            if (r == MessageResult.Yes)
                LoadFileIntoEditor(doc, doc.FilePath);
            else
                doc.IsDirty = true;
        }
        finally { doc.ChangePromptOpen = false; }
    }

    private async void HandleFileDeleted(EditorDocument doc)
    {
        if (doc.ChangePromptOpen || doc.FilePath == null) return;
        doc.ChangePromptOpen = true;
        try
        {
            var r = await MessageBoxes.Show(this, "File deleted",
                $"{Path.GetFileName(doc.FilePath)} was deleted on disk.\n\nClose this tab? (No = keep as a modified buffer)",
                MessageButtons.YesNo);
            if (r == MessageResult.Yes) CloseTab(doc);
            else { doc.IsDirty = true; doc.FilePath = null; StopFileChangeWatch(doc); }
        }
        finally { doc.ChangePromptOpen = false; }
    }

    private async void HandleFileRenamed(EditorDocument doc, string newPath)
    {
        if (doc.ChangePromptOpen) return;
        doc.ChangePromptOpen = true;
        try
        {
            var r = await MessageBoxes.Show(this, "File renamed",
                $"The file was renamed to {Path.GetFileName(newPath)}.\n\nTrack the new name?",
                MessageButtons.YesNo);
            if (r == MessageResult.Yes)
            {
                doc.FilePath = newPath;
                doc.BaseTitle = Path.GetFileName(newPath);
                doc.RaiseHeaderChanged();
                StartFileChangeWatch(doc);
            }
            else { doc.IsDirty = true; doc.FilePath = null; StopFileChangeWatch(doc); }
        }
        finally { doc.ChangePromptOpen = false; }
    }

    // ---- Live tail ----

    private void ToggleLiveMonitor()
    {
        var doc = ActiveDoc;
        if (doc?.Editor == null) return;
        if (doc.FilePath == null || doc.IsDirty)
        {
            ShowMessage("n+", "Save the file before enabling live monitoring.");
            return;
        }

        if (doc.IsLive)
        {
            doc.IsLive = false;
            try { doc.LiveWatcher?.Dispose(); } catch { }
            doc.LiveWatcher = null;
            doc.RaiseHeaderChanged();
            StartFileChangeWatch(doc);
        }
        else
        {
            StopFileChangeWatch(doc);
            doc.LiveOffset = new FileInfo(doc.FilePath).Length;
            try
            {
                var dir = Path.GetDirectoryName(doc.FilePath)!;
                var w = new FileSystemWatcher(dir, Path.GetFileName(doc.FilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 65536,
                };
                w.Changed += (_, _) => Dispatcher.UIThread.Post(() => LiveTailRead(doc));
                w.EnableRaisingEvents = true;
                doc.LiveWatcher = w;
                doc.IsLive = true;
                doc.RaiseHeaderChanged();
            }
            catch (Exception ex) { ShowMessage("n+", $"Could not start live monitor:\n{ex.Message}"); }
        }
        UpdateToggleButtonVisuals();
    }

    private void LiveTailRead(EditorDocument doc)
    {
        if (!doc.IsLive || doc.Editor == null || doc.FilePath == null) return;
        try
        {
            long curLen = new FileInfo(doc.FilePath).Length;
            if (curLen < doc.LiveOffset)
            {
                // Truncated/rolled — reload from scratch.
                string full = ReadAllTextShared(doc.FilePath, doc.Encoding, out _);
                doc.Editor.Document.Text = full;
                doc.LiveOffset = curLen;
            }
            else if (curLen > doc.LiveOffset)
            {
                using var fs = new FileStream(doc.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(doc.LiveOffset, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, doc.Encoding, false);
                string appended = reader.ReadToEnd();
                doc.LiveOffset = curLen;
                doc.Editor.AppendText(appended);
            }
            doc.IsDirty = false;
            doc.Editor.ScrollToLine(doc.Editor.Document.LineCount);
        }
        catch { /* best effort tail */ }
    }

    // ---- Recent files ----

    private void AddToRecent(string path)
    {
        _recent.Add(path);
        RebuildRecentFilesMenu();
    }

    private void RebuildRecentFilesMenu()
    {
        // Mutate via ItemCollection's public methods (RemoveAt/Insert); the non-generic
        // IList facade on Avalonia's ItemCollection is read-only for some operations.
        var menuItems = _fileMenu.Items;
        var read = (IList)menuItems; // index access for inspection only

        for (int i = read.Count - 1; i >= 0; i--)
            if (read[i] is Control c && (c.Tag as string) == "recent")
                menuItems.RemoveAt(i);

        if (_recent.Files.Count == 0) return;

        int exitIndex = -1;
        for (int i = 0; i < read.Count; i++)
            if (read[i] is MenuItem mi && (mi.Header as string) == "Exit") { exitIndex = i; break; }
        if (exitIndex < 0) return;

        menuItems.Insert(exitIndex, new Separator { Tag = "recent" });

        for (int i = 0; i < _recent.Files.Count; i++)
        {
            string path = _recent.Files[i];
            var item = new MenuItem { Header = $"{i + 1}. {Path.GetFileName(path)}", Tag = "recent" };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => OpenRecentFile(path);
            menuItems.Insert(exitIndex + 1 + i, item);
        }
    }

    private async void OpenRecentFile(string path)
    {
        if (!File.Exists(path))
        {
            var r = await MessageBoxes.Show(this, "n+", $"File not found:\n{path}\n\nRemove from recent list?", MessageButtons.YesNo);
            if (r == MessageResult.Yes) { _recent.Remove(path); RebuildRecentFilesMenu(); }
            return;
        }
        OpenFilesFromPaths(new[] { path });
    }

    // ---- Session (hot exit) ----

    private void SaveSession()
    {
        SessionStore.ClearBackups();
        var entries = new List<SessionEntry>();
        int counter = 0;
        foreach (TabItem tab in ((IList)_tabs.Items))
        {
            if (!_docs.TryGetValue(tab, out var doc)) continue;
            string originalPath = doc.FilePath ?? "";
            string title = doc.DisplayTitle;
            string backupPath = "";

            if (!doc.IsHex && (doc.IsDirty || string.IsNullOrEmpty(originalPath)))
            {
                backupPath = SessionStore.BackupPathFor(counter);
                SessionStore.WriteBackup(backupPath, doc.Editor!.Document.Text);
            }
            else if (doc.IsHex && doc.IsDirty)
            {
                title = doc.BaseTitle; // don't claim hex tab was backed up
            }

            entries.Add(new SessionEntry
            {
                OriginalPath = originalPath,
                BackupPath = backupPath,
                TabTitle = title,
                ColorIndex = doc.ColorIndex,
            });
            counter++;
        }
        SessionStore.Save(entries);
    }

    private void LoadSession()
    {
        var entries = SessionStore.Load();
        if (entries.Count == 0) return;

        foreach (var e in entries)
        {
            string display = e.TabTitle.TrimEnd('*');
            if (string.IsNullOrEmpty(e.OriginalPath) && string.IsNullOrEmpty(display)) display = $"new {_newCounter++}";

            var doc = AddNewTab(display, string.IsNullOrEmpty(e.OriginalPath) ? null : e.OriginalPath);

            if (!string.IsNullOrEmpty(e.BackupPath) && File.Exists(e.BackupPath))
            {
                SetEditorText(doc, File.ReadAllText(e.BackupPath));
                doc.IsDirty = true;
                ApplySyntax(doc);
            }
            else if (!string.IsNullOrEmpty(e.OriginalPath) && File.Exists(e.OriginalPath))
            {
                LoadFileIntoEditor(doc, e.OriginalPath);
            }

            if (e.ColorIndex >= 1 && e.ColorIndex <= TabColors.Length)
            {
                doc.ColorIndex = e.ColorIndex;
                doc.RaiseHeaderChanged();
            }
        }
    }

    // ---- Linux desktop integration (replaces Windows registry shell integration) ----

    private async void InstallDesktopEntry()
    {
        try
        {
            string exe = Environment.ProcessPath ?? "nplus";
            string appsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "..", "local", "share", "applications");
            // Prefer XDG ~/.local/share/applications
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appsDir = Path.Combine(home, ".local", "share", "applications");
            Directory.CreateDirectory(appsDir);
            string desktop = Path.Combine(appsDir, "nplus.desktop");
            File.WriteAllText(desktop,
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=n+\n" +
                "Comment=Lightweight text/code editor\n" +
                $"Exec=\"{exe}\" %F\n" +
                "Terminal=false\n" +
                "Categories=Utility;TextEditor;Development;\n" +
                "MimeType=text/plain;\n");
            await MessageBoxes.Show(this, "n+", $"Desktop entry installed:\n{desktop}\n\nYou may need to log out/in or run 'update-desktop-database'.", MessageButtons.Ok);
        }
        catch (Exception ex) { ShowMessage("n+", $"Could not install desktop entry:\n{ex.Message}"); }
    }

    private async void RemoveDesktopEntry()
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string desktop = Path.Combine(home, ".local", "share", "applications", "nplus.desktop");
            if (File.Exists(desktop)) File.Delete(desktop);
            await MessageBoxes.Show(this, "n+", "Desktop entry removed.", MessageButtons.Ok);
        }
        catch (Exception ex) { ShowMessage("n+", $"Could not remove desktop entry:\n{ex.Message}"); }
    }
}
