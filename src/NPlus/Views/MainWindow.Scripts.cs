using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NPlus.Core;
using NPlus.Dialogs;
using NPlus.Scripting;

namespace NPlus.Views;

public partial class MainWindow
{
    private MenuItem _scriptsMenu = null!;

    // ---- Menu construction ----

    private MenuItem BuildScriptsMenu()
    {
        StarterScripts.EnsureSeeded(AppPaths.ScriptsDir);

        _scriptsMenu = new MenuItem { Header = "Scripting (Lua)" };
        RebuildScriptsMenu();
        // Re-scan the scripts folder each time the menu opens so newly added files appear.
        _scriptsMenu.SubmenuOpened += (_, _) => RebuildScriptsMenu();
        return _scriptsMenu;
    }

    private void RebuildScriptsMenu()
    {
        var items = (IList)_scriptsMenu.Items;
        items.Clear();
        items.Add(Mi("Run Current Document as Script", RunActiveDocAsScript, new KeyGesture(Key.F5)));
        items.Add(Mi("Run Script File…", () => _ = RunScriptFromFile()));
        items.Add(new Separator());

        string[] files = Array.Empty<string>();
        try
        {
            if (Directory.Exists(AppPaths.ScriptsDir))
                files = Directory.GetFiles(AppPaths.ScriptsDir, "*.lua")
                                 .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch { /* listing is best-effort */ }

        if (files.Length == 0)
        {
            items.Add(new MenuItem { Header = "(no scripts in folder)", IsEnabled = false });
        }
        else
        {
            foreach (var file in files)
            {
                string path = file;
                items.Add(Mi($"Run: {Path.GetFileName(path)}", () => _ = RunScriptFile(path)));
            }
        }

        items.Add(new Separator());
        items.Add(Mi("Open Scripts Folder", OpenScriptsFolder));
        items.Add(Mi("Scripting Help…", ShowScriptingHelp));
    }

    // ---- Running scripts ----

    private async void RunActiveDocAsScript()
    {
        var ed = GetActiveEditor();
        if (ed?.Document == null)
        {
            ShowMessage("n+", "Open or focus a text tab to run its contents as a Lua script.");
            return;
        }
        await RunLua(ed.Document.Text, ActiveDoc?.BaseTitle ?? "script");
    }

    private async Task RunScriptFile(string path)
    {
        string code;
        try { code = File.ReadAllText(path); }
        catch (Exception ex) { ShowMessage("n+", $"Could not read script:\n{ex.Message}"); return; }
        await RunLua(code, Path.GetFileName(path));
    }

    private async Task RunScriptFromFile()
    {
        string? path = await PickLuaFile();
        if (path == null) return;
        if (!File.Exists(path)) { ShowMessage("n+", $"File not found:\n{path}"); return; }
        await RunScriptFile(path);
    }

    private async Task RunLua(string code, string name)
    {
        var ed = GetActiveEditor();
        if (ed == null)
        {
            ShowMessage("n+", "Scripts run against a text tab. Switch to one first.");
            return;
        }

        var host = new LuaHost();
        var ctx = BuildScriptContext(name);

        ScriptResult result;
        // Group every buffer edit the script makes into a single undo step.
        if (ed.Document != null)
            using (ed.Document.RunUpdate())
                result = host.Run(code, ctx);
        else
            result = host.Run(code, ctx);

        UpdateStatusBar();
        await ShowScriptResult(name, result);
    }

    /// <summary>Builds the <c>editor</c> API delegates bound to the currently active tab.</summary>
    private ScriptContext BuildScriptContext(string name)
    {
        var ed = GetActiveEditor();
        var doc = ActiveDoc;
        return new ScriptContext
        {
            Name = name,
            GetText = () => ed?.Document?.Text ?? string.Empty,
            SetText = txt => { if (ed?.Document != null) ed.Document.Text = txt; },
            GetSelection = () => ed?.SelectedText ?? string.Empty,
            ReplaceSelection = txt =>
            {
                if (ed?.Document == null) return;
                if (ed.SelectionLength > 0)
                    ed.Document.Replace(ed.SelectionStart, ed.SelectionLength, txt);
                else
                {
                    ed.Document.Insert(ed.CaretOffset, txt);
                    ed.CaretOffset += txt.Length;
                }
            },
            Insert = txt =>
            {
                if (ed?.Document == null) return;
                ed.Document.Insert(ed.CaretOffset, txt);
                ed.CaretOffset += txt.Length;
            },
            LineCount = () => ed?.Document?.LineCount ?? 0,
            CaretLine = () => ed?.Document != null ? ed.Document.GetLineByOffset(ed.CaretOffset).LineNumber : 1,
            FilePath = () => doc?.FilePath,
            Language = () => SyntaxMap.GetLanguageId(doc?.FilePath),
        };
    }

    // ---- File picker (mirrors OpenFile's native-then-prompt fallback) ----

    private async Task<string?> PickLuaFile()
    {
        if (StorageProvider.CanOpen)
        {
            try
            {
                var start = await StorageProvider.TryGetFolderFromPathAsync(AppPaths.ScriptsDir);
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Run Lua Script",
                    AllowMultiple = false,
                    SuggestedStartLocation = start,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Lua scripts") { Patterns = new[] { "*.lua" } },
                        FilePickerFileTypes.All,
                    },
                });
                if (files != null && files.Count > 0)
                    return files[0].TryGetLocalPath();
                return null; // cancelled
            }
            catch { /* fall through to the typed-path prompt */ }
        }

        string fallback = AppPaths.ScriptsDir + Path.DirectorySeparatorChar;
        string? input = await MessageBoxes.Prompt(this, "Run Lua Script", "Enter the full path of the .lua file to run:", fallback);
        return string.IsNullOrWhiteSpace(input) ? null : input.Trim();
    }

    // ---- Scripts folder ----

    private void OpenScriptsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ScriptsDir);
            string opener =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "explorer" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
            Process.Start(new ProcessStartInfo { FileName = opener, Arguments = $"\"{AppPaths.ScriptsDir}\"", UseShellExecute = true });
        }
        catch
        {
            ShowMessage("n+", $"Scripts folder:\n{AppPaths.ScriptsDir}");
        }
    }

    // ---- Output / help dialogs ----

    private async Task ShowScriptResult(string name, ScriptResult result)
    {
        // Nothing printed and no error: a silent buffer transform — stay out of the way.
        if (result.Success && string.IsNullOrWhiteSpace(result.Output))
            return;

        var body = new StringBuilder();
        if (!string.IsNullOrEmpty(result.Output)) body.Append(result.Output.TrimEnd('\n'));
        if (!result.Success)
        {
            if (body.Length > 0) body.Append("\n\n");
            body.Append("Error:\n").Append(result.Error);
        }

        var text = new SelectableTextBlock
        {
            Text = body.ToString(),
            FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"),
            TextWrapping = TextWrapping.Wrap,
        };
        var scroll = new ScrollViewer { Content = text, MaxHeight = 360, MaxWidth = 560 };

        var close = new Button { Content = "Close", MinWidth = 84, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = result.Success ? $"Script output — {name}" : $"Script error — {name}",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { scroll, close } };
        await dialog.ShowDialog(this);
    }

    private void ShowScriptingHelp() => ShowMessage("n+ — Lua Scripting",
        "Tools ▸ Scripting (Lua) runs a Lua script against the active tab.\n\n" +
        "Run the current document with F5, pick a .lua file, or choose one from your\n" +
        "scripts folder (Open Scripts Folder to manage them).\n\n" +
        "The global 'editor' table is your API:\n" +
        "  editor.text()                  whole document text\n" +
        "  editor.set_text(s)             replace the whole document\n" +
        "  editor.selection()             selected text (\"\" if none)\n" +
        "  editor.replace_selection(s)    replace selection / insert at caret\n" +
        "  editor.insert(s)               insert at the caret\n" +
        "  editor.lines()                 array table of lines\n" +
        "  editor.set_lines(t)            replace document from an array table\n" +
        "  editor.line_count()            number of lines\n" +
        "  editor.caret_line()            1-based caret line\n" +
        "  editor.file_path()             path of the file, or nil\n" +
        "  editor.language()              syntax language id, or nil\n\n" +
        "print(...) is captured and shown after the run. Scripts are sandboxed:\n" +
        "the string/table/math/os(time) libraries are available, but file I/O,\n" +
        "os.execute and loadfile/dofile are disabled.");
}
