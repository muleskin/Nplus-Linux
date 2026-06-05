using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using AvaloniaEdit;
using NPlus.Core;
using NPlus.Dialogs;
using NPlus.Models;
using NPlus.Search;

namespace NPlus.Views;

public partial class MainWindow
{
    private bool _isRecording;
    private List<MacroStep> _currentMacro = new();

    public void RecordMacroStep(MacroStep step)
    {
        if (_isRecording) _currentMacro.Add(step);
    }

    private void WireMacroRecording(TextEditor editor)
    {
        editor.TextArea.TextEntered += (_, e) =>
        {
            if (!_isRecording || string.IsNullOrEmpty(e.Text)) return;
            if (e.Text == "\r" || e.Text == "\n") return; // handled as NewLine via KeyDown
            _currentMacro.Add(new MacroStep(MacroActionType.InsertText, e.Text));
        };
        editor.AddHandler(KeyDownEvent, (s, e) =>
        {
            if (!_isRecording) return;
            switch (e.Key)
            {
                case Key.Enter: _currentMacro.Add(new MacroStep(MacroActionType.NewLine)); break;
                case Key.Back: _currentMacro.Add(new MacroStep(MacroActionType.Backspace)); break;
                case Key.Delete: _currentMacro.Add(new MacroStep(MacroActionType.Delete)); break;
                case Key.Left: _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = MacroKeyCommand.CharLeft }); break;
                case Key.Right: _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = MacroKeyCommand.CharRight }); break;
                case Key.Up: _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = MacroKeyCommand.LineUp }); break;
                case Key.Down: _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = MacroKeyCommand.LineDown }); break;
                case Key.Home: _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = MacroKeyCommand.LineStart }); break;
                case Key.End: _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = MacroKeyCommand.LineEnd }); break;
                case Key.Tab: _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = MacroKeyCommand.Tab }); break;
            }
        }, RoutingStrategies.Tunnel);
    }

    private void StartRecording()
    {
        _isRecording = true;
        _currentMacro = new List<MacroStep>();
        Title = "n+ — RECORDING MACRO…";
    }

    private void StopRecording()
    {
        _isRecording = false;
        Title = "n+";
    }

    private void PlaybackMacro(bool wrapInUndo)
    {
        var ed = GetActiveEditor();
        var doc = ActiveDoc;
        if (ed?.Document == null || doc == null || _currentMacro.Count == 0) return;
        if (wrapInUndo)
        {
            using (ed.Document.RunUpdate())
                foreach (var step in _currentMacro) ExecuteMacroStep(ed, doc, step);
        }
        else
        {
            foreach (var step in _currentMacro) ExecuteMacroStep(ed, doc, step);
        }
    }

    private void ExecuteMacroStep(TextEditor ed, EditorDocument doc, MacroStep step)
    {
        var d = ed.Document;
        switch (step.ActionType)
        {
            case MacroActionType.InsertText:
                if (!string.IsNullOrEmpty(step.Data)) { d.Insert(ed.CaretOffset, step.Data); ed.CaretOffset += step.Data!.Length; }
                break;
            case MacroActionType.NewLine:
                { string nl = GetNewline(ed); d.Insert(ed.CaretOffset, nl); ed.CaretOffset += nl.Length; }
                break;
            case MacroActionType.Backspace:
                if (ed.CaretOffset > 0) { d.Remove(ed.CaretOffset - 1, 1); ed.CaretOffset--; }
                break;
            case MacroActionType.Delete:
                if (ed.CaretOffset < d.TextLength) d.Remove(ed.CaretOffset, 1);
                break;
            case MacroActionType.KeyCommand:
                ExecuteKeyCommand(ed, step.CommandId);
                break;
            case MacroActionType.FindReplace:
            {
                var o = new SearchOptions { Regex = step.IsRegex, Backward = step.IsBackward, Wrap = step.IsWrap };
                if (step.IsReplace) SearchEngine.ReplaceNext(ed, step.SearchText ?? "", step.ReplaceText ?? "", o);
                else SearchEngine.FindNext(ed, step.SearchText ?? "", o);
                break;
            }
            case MacroActionType.ReplaceAll:
                SearchEngine.ReplaceAll(ed, step.SearchText ?? "", step.ReplaceText ?? "", new SearchOptions { Regex = step.IsRegex });
                break;
            case MacroActionType.MarkAll:
                SearchEngine.MarkAll(ed, doc, step.SearchText ?? "", new SearchOptions { Regex = step.IsRegex }, step.IsBookmark, step.IsPurge);
                break;
            case MacroActionType.ClearMarks:
                SearchEngine.ClearMarks(doc, ed);
                break;
        }
    }

    private static void ExecuteKeyCommand(TextEditor ed, MacroKeyCommand cmd)
    {
        var d = ed.Document;
        int offset = ed.CaretOffset;
        switch (cmd)
        {
            case MacroKeyCommand.CharLeft: ed.CaretOffset = Math.Max(0, offset - 1); break;
            case MacroKeyCommand.CharRight: ed.CaretOffset = Math.Min(d.TextLength, offset + 1); break;
            case MacroKeyCommand.LineUp:
            {
                var line = d.GetLineByOffset(offset);
                if (line.PreviousLine != null) ed.CaretOffset = Math.Min(line.PreviousLine.Offset + (offset - line.Offset), line.PreviousLine.EndOffset);
                break;
            }
            case MacroKeyCommand.LineDown:
            {
                var line = d.GetLineByOffset(offset);
                if (line.NextLine != null) ed.CaretOffset = Math.Min(line.NextLine.Offset + (offset - line.Offset), line.NextLine.EndOffset);
                break;
            }
            case MacroKeyCommand.LineStart: ed.CaretOffset = d.GetLineByOffset(offset).Offset; break;
            case MacroKeyCommand.LineEnd: ed.CaretOffset = d.GetLineByOffset(offset).EndOffset; break;
            case MacroKeyCommand.Tab: d.Insert(offset, "\t"); ed.CaretOffset = offset + 1; break;
        }
    }

    // ---- Macro management dialogs ----

    private async void SaveMacroDialog()
    {
        if (_currentMacro.Count == 0) { ShowMessage("n+", "No macro recorded yet."); return; }
        string? name = await MessageBoxes.Prompt(this, "Save Macro", "Macro name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        _macros.Macros[name] = _currentMacro.Select(Clone).ToList();
        _macros.Save();
        ShowMessage("n+", $"Macro \"{name}\" saved.");
    }

    private async void LoadMacroDialog()
    {
        var name = await ChooseMacro("Load Saved Macro");
        if (name == null) return;
        _currentMacro = _macros.Macros[name].Select(Clone).ToList();
        ShowMessage("n+", $"Loaded macro \"{name}\" ({_currentMacro.Count} steps).");
    }

    private async void ModifyMacros()
    {
        var name = await ChooseMacro("Modify / Delete Macros");
        if (name == null) return;
        var r = await MessageBoxes.Show(this, "n+", $"Delete macro \"{name}\"?", MessageButtons.YesNo);
        if (r == MessageResult.Yes) { _macros.Macros.Remove(name); _macros.Save(); }
    }

    private async void EditMacroDialog()
    {
        // Offer the active recorded macro plus saved ones.
        var options = new List<string>();
        if (_currentMacro.Count > 0) options.Add("<Active recorded macro>");
        options.AddRange(_macros.Macros.Keys);
        if (options.Count == 0) { ShowMessage("n+", "No macros to edit."); return; }

        var choice = await ChooseFromList("Edit Macro Steps", options);
        if (choice == null) return;
        bool active = choice == "<Active recorded macro>";
        var steps = active ? _currentMacro : _macros.Macros[choice];
        await ShowMacroEditor(steps, choice, active);
    }

    private async void RunMacroMultiple()
    {
        if (_currentMacro.Count == 0) { ShowMessage("n+", "No macro recorded yet."); return; }
        var (toEof, times) = await ShowRunMultipleDialog();
        if (times < 0) return;
        var ed = GetActiveEditor();
        var doc = ActiveDoc;
        if (ed?.Document == null || doc == null) return;

        using (ed.Document.RunUpdate())
        {
            if (toEof)
            {
                int guard = 0;
                int last = -1;
                while (ed.CaretOffset < ed.Document.TextLength && ed.CaretOffset != last && guard++ < 1_000_000)
                {
                    last = ed.CaretOffset;
                    foreach (var s in _currentMacro) ExecuteMacroStep(ed, doc, s);
                }
            }
            else
            {
                for (int i = 0; i < times; i++)
                    foreach (var s in _currentMacro) ExecuteMacroStep(ed, doc, s);
            }
        }
    }

    private void TrimTrailingSpaceAndSave()
    {
        var doc = ActiveDoc;
        var ed = doc?.Editor;
        if (doc == null || ed == null) return;
        ed.Document.Text = TextTransforms.TrimTrailing(ed.Document.Text, GetNewline(ed));
        SaveTab(doc);
    }

    private static MacroStep Clone(MacroStep s) => new()
    {
        ActionType = s.ActionType,
        Data = s.Data,
        CommandId = s.CommandId,
        SearchText = s.SearchText,
        ReplaceText = s.ReplaceText,
        Flags = s.Flags,
        IsRegex = s.IsRegex,
        IsBackward = s.IsBackward,
        IsWrap = s.IsWrap,
        IsReplace = s.IsReplace,
        IsPurge = s.IsPurge,
        IsBookmark = s.IsBookmark,
    };

    private System.Threading.Tasks.Task<string?> ChooseMacro(string title)
    {
        if (_macros.Macros.Count == 0) { ShowMessage("n+", "No saved macros."); return System.Threading.Tasks.Task.FromResult<string?>(null); }
        return ChooseFromList(title, _macros.Macros.Keys.ToList());
    }

    private async System.Threading.Tasks.Task<string?> ChooseFromList(string title, IList<string> items)
    {
        string? result = null;
        var list = new ListBox { ItemsSource = items, Height = 220, Width = 320 };
        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        var dialog = new Window { Title = title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => { result = list.SelectedItem as string; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        dialog.Content = new StackPanel { Margin = new Thickness(14), Spacing = 10, Children = { list, buttons } };
        await dialog.ShowDialog(this);
        return result;
    }

    private async System.Threading.Tasks.Task<(bool toEof, int times)> ShowRunMultipleDialog()
    {
        bool toEof = false;
        int times = -1;
        var radEof = new RadioButton { Content = "Run to end of file", GroupName = "runmode" };
        var radN = new RadioButton { Content = "Run N times:", GroupName = "runmode", IsChecked = true };
        var num = new NumericUpDown { Value = 1, Minimum = 1, Maximum = 100000, Width = 120 };
        var ok = new Button { Content = "Run", MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        var dialog = new Window { Title = "Run Macro Multiple Times", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => { toEof = radEof.IsChecked == true; times = toEof ? 0 : (int)(num.Value ?? 1); dialog.Close(); };
        cancel.Click += (_, _) => { times = -1; dialog.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var nRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { radN, num } };
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { radEof, nRow, buttons } };
        await dialog.ShowDialog(this);
        return (toEof, times);
    }

    private async System.Threading.Tasks.Task ShowMacroEditor(List<MacroStep> steps, string name, bool active)
    {
        var list = new ListBox { Height = 280, Width = 420 };
        void Refresh() => list.ItemsSource = steps.Select((s, i) => $"{i + 1}. {s.Describe()}").ToList();
        Refresh();

        Button B(string t, Action a) { var b = new Button { Content = t, MinWidth = 90 }; b.Click += (_, _) => a(); return b; }

        var up = B("Move Up", () => { int i = list.SelectedIndex; if (i > 0) { (steps[i], steps[i - 1]) = (steps[i - 1], steps[i]); Refresh(); list.SelectedIndex = i - 1; } });
        var down = B("Move Down", () => { int i = list.SelectedIndex; if (i >= 0 && i < steps.Count - 1) { (steps[i], steps[i + 1]) = (steps[i + 1], steps[i]); Refresh(); list.SelectedIndex = i + 1; } });
        var del = B("Delete", () => { int i = list.SelectedIndex; if (i >= 0) { steps.RemoveAt(i); Refresh(); } });
        var dup = B("Duplicate", () => { int i = list.SelectedIndex; if (i >= 0) { steps.Insert(i + 1, Clone(steps[i])); Refresh(); } });
        var addText = B("Add Insert Text", async () => { var t = await MessageBoxes.Prompt(this, "Add Insert Text", "Text to insert:"); if (t != null) { steps.Add(new MacroStep(MacroActionType.InsertText, t)); Refresh(); } });
        var addNl = B("Add New Line", () => { steps.Add(new MacroStep(MacroActionType.NewLine)); Refresh(); });

        var apply = new Button { Content = "Apply && Close", MinWidth = 110, IsDefault = true };
        var dialog = new Window { Title = $"Edit Macro — {name}", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        apply.Click += (_, _) =>
        {
            if (!active) { _macros.Macros[name] = steps; _macros.Save(); }
            else { _currentMacro = steps; }
            dialog.Close();
        };

        var sideButtons = new StackPanel { Spacing = 6, Children = { up, down, del, dup, addText, addNl } };
        var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { list, sideButtons } };
        dialog.Content = new StackPanel { Margin = new Thickness(14), Spacing = 12, Children = { body, apply } };
        await dialog.ShowDialog(this);
    }
}
