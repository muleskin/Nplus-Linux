using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using NPlus.Core;
using NPlus.Editor;
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
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            switch (e.Key)
            {
                case Key.Enter: _currentMacro.Add(new MacroStep(MacroActionType.NewLine)); break;
                case Key.Back: _currentMacro.Add(new MacroStep(MacroActionType.Backspace)); break;
                case Key.Delete: _currentMacro.Add(new MacroStep(MacroActionType.Delete)); break;
                case Key.Tab: _currentMacro.Add(KeyCmd(shift ? MacroKeyCommand.BackTab : MacroKeyCommand.Tab)); break;
                default:
                    // Capture Shift (select) and Ctrl (word/document) modifiers so navigation that
                    // builds a selection replays as a selection — not a bare caret move.
                    var cmd = MapNavigationKey(e.Key, ctrl, shift);
                    if (cmd != MacroKeyCommand.None) _currentMacro.Add(KeyCmd(cmd));
                    break;
            }
        }, RoutingStrategies.Tunnel);
    }

    private static MacroStep KeyCmd(MacroKeyCommand cmd) =>
        new() { ActionType = MacroActionType.KeyCommand, CommandId = cmd };

    private static MacroKeyCommand MapNavigationKey(Key key, bool ctrl, bool shift) => key switch
    {
        Key.Left => ctrl ? (shift ? MacroKeyCommand.WordLeftExtend : MacroKeyCommand.WordLeft)
                         : (shift ? MacroKeyCommand.CharLeftExtend : MacroKeyCommand.CharLeft),
        Key.Right => ctrl ? (shift ? MacroKeyCommand.WordRightExtend : MacroKeyCommand.WordRight)
                          : (shift ? MacroKeyCommand.CharRightExtend : MacroKeyCommand.CharRight),
        Key.Up => shift ? MacroKeyCommand.LineUpExtend : MacroKeyCommand.LineUp,
        Key.Down => shift ? MacroKeyCommand.LineDownExtend : MacroKeyCommand.LineDown,
        Key.Home => ctrl ? (shift ? MacroKeyCommand.DocumentStartExtend : MacroKeyCommand.DocumentStart)
                         : (shift ? MacroKeyCommand.LineStartExtend : MacroKeyCommand.LineStart),
        Key.End => ctrl ? (shift ? MacroKeyCommand.DocumentEndExtend : MacroKeyCommand.DocumentEnd)
                        : (shift ? MacroKeyCommand.LineEndExtend : MacroKeyCommand.LineEnd),
        Key.PageUp => shift ? MacroKeyCommand.PageUpExtend : MacroKeyCommand.PageUp,
        Key.PageDown => shift ? MacroKeyCommand.PageDownExtend : MacroKeyCommand.PageDown,
        _ => MacroKeyCommand.None,
    };

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
                if (!string.IsNullOrEmpty(step.Data)) ReplaceSelectionOrInsert(ed, step.Data!);
                break;
            case MacroActionType.NewLine:
                ReplaceSelectionOrInsert(ed, GetNewline(ed));
                break;
            case MacroActionType.Backspace:
                DeleteBackwardOrSelection(ed);
                break;
            case MacroActionType.Delete:
                DeleteForwardOrSelection(ed);
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
        // Tab/back-tab edit the buffer rather than move the caret.
        if (cmd == MacroKeyCommand.Tab) { ReplaceSelectionOrInsert(ed, "\t"); return; }
        if (cmd == MacroKeyCommand.BackTab) { Outdent(ed); return; }

        bool extend = IsExtend(cmd);
        int target = MacroNavigation.ComputeMoveTarget(ed.Document, BaseMove(cmd), ed.CaretOffset, PageLineCount(ed));
        ApplyMove(ed, target, extend);
    }

    private static bool IsExtend(MacroKeyCommand cmd) => cmd is
        MacroKeyCommand.CharLeftExtend or MacroKeyCommand.CharRightExtend or
        MacroKeyCommand.LineUpExtend or MacroKeyCommand.LineDownExtend or
        MacroKeyCommand.WordLeftExtend or MacroKeyCommand.WordRightExtend or
        MacroKeyCommand.LineStartExtend or MacroKeyCommand.LineEndExtend or
        MacroKeyCommand.DocumentStartExtend or MacroKeyCommand.DocumentEndExtend or
        MacroKeyCommand.PageUpExtend or MacroKeyCommand.PageDownExtend;

    /// <summary>Maps a "*Extend" selection command to its plain caret-move equivalent.</summary>
    private static MacroKeyCommand BaseMove(MacroKeyCommand cmd) => cmd switch
    {
        MacroKeyCommand.CharLeftExtend => MacroKeyCommand.CharLeft,
        MacroKeyCommand.CharRightExtend => MacroKeyCommand.CharRight,
        MacroKeyCommand.LineUpExtend => MacroKeyCommand.LineUp,
        MacroKeyCommand.LineDownExtend => MacroKeyCommand.LineDown,
        MacroKeyCommand.WordLeftExtend => MacroKeyCommand.WordLeft,
        MacroKeyCommand.WordRightExtend => MacroKeyCommand.WordRight,
        MacroKeyCommand.LineStartExtend => MacroKeyCommand.LineStart,
        MacroKeyCommand.LineEndExtend => MacroKeyCommand.LineEnd,
        MacroKeyCommand.DocumentStartExtend => MacroKeyCommand.DocumentStart,
        MacroKeyCommand.DocumentEndExtend => MacroKeyCommand.DocumentEnd,
        MacroKeyCommand.PageUpExtend => MacroKeyCommand.PageUp,
        MacroKeyCommand.PageDownExtend => MacroKeyCommand.PageDown,
        _ => cmd,
    };

    private static int PageLineCount(TextEditor ed)
    {
        double lineHeight = ed.FontSize > 0 ? ed.FontSize * 1.3 : 16;
        double height = ed.TextArea.TextView.Bounds.Height;
        int n = (height > 0) ? (int)(height / lineHeight) - 1 : 0;
        return n > 0 ? n : 20; // sensible fallback before the view is laid out
    }

    /// <summary>Applies a caret move, either extending the selection (Shift) or collapsing it.</summary>
    private static void ApplyMove(TextEditor ed, int target, bool extend)
    {
        target = Math.Clamp(target, 0, ed.Document.TextLength);
        if (extend)
        {
            int caret = ed.CaretOffset;
            int anchor;
            if (ed.SelectionLength > 0)
            {
                int s = ed.SelectionStart, e = s + ed.SelectionLength;
                anchor = caret == e ? s : (caret == s ? e : s); // keep the fixed (non-caret) end
            }
            else anchor = caret;
            ed.Select(Math.Min(anchor, target), Math.Abs(target - anchor));
            ed.CaretOffset = target;
        }
        else
        {
            ed.TextArea.ClearSelection();
            ed.CaretOffset = target;
        }
    }

    // ---- Selection-aware edits (typing/Delete/Backspace replace an active selection) ----

    private static void ReplaceSelectionOrInsert(TextEditor ed, string text)
    {
        var d = ed.Document;
        if (ed.SelectionLength > 0)
        {
            int start = ed.SelectionStart;
            d.Replace(start, ed.SelectionLength, text);
            ed.TextArea.ClearSelection();
            ed.CaretOffset = start + text.Length;
        }
        else
        {
            int o = ed.CaretOffset;
            d.Insert(o, text);
            ed.CaretOffset = o + text.Length;
        }
    }

    private static void DeleteBackwardOrSelection(TextEditor ed)
    {
        var d = ed.Document;
        if (ed.SelectionLength > 0)
        {
            int start = ed.SelectionStart;
            d.Remove(start, ed.SelectionLength);
            ed.TextArea.ClearSelection();
            ed.CaretOffset = start;
        }
        else if (ed.CaretOffset > 0)
        {
            int o = ed.CaretOffset;
            d.Remove(o - 1, 1);
            ed.CaretOffset = o - 1;
        }
    }

    private static void DeleteForwardOrSelection(TextEditor ed)
    {
        var d = ed.Document;
        if (ed.SelectionLength > 0)
        {
            int start = ed.SelectionStart;
            d.Remove(start, ed.SelectionLength);
            ed.TextArea.ClearSelection();
            ed.CaretOffset = start;
        }
        else if (ed.CaretOffset < d.TextLength)
        {
            d.Remove(ed.CaretOffset, 1);
        }
    }

    private static void Outdent(TextEditor ed)
    {
        var d = ed.Document;
        var line = d.GetLineByOffset(ed.CaretOffset);
        if (line.Length == 0) return;
        string head = d.GetText(line.Offset, Math.Min(4, line.Length));
        int removed = head[0] == '\t' ? 1 : 0;
        if (removed == 0) while (removed < head.Length && head[removed] == ' ') removed++;
        if (removed > 0) d.Remove(line.Offset, removed);
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
