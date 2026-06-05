namespace NPlus.Core;

public enum MacroActionType
{
    InsertText,
    NewLine,
    Backspace,
    Delete,
    KeyCommand,
    FindReplace,
    ReplaceAll,
    MarkAll,
    ClearMarks,
}

/// <summary>
/// Portable navigation/edit commands captured during macro recording.
/// Replaces the original's Scintilla Command enum ids with a toolkit-neutral set.
/// </summary>
public enum MacroKeyCommand
{
    None = 0,
    CharLeft, CharRight, LineUp, LineDown,
    WordLeft, WordRight, LineStart, LineEnd,
    DocumentStart, DocumentEnd, PageUp, PageDown,
    CharLeftExtend, CharRightExtend, LineUpExtend, LineDownExtend,
    WordLeftExtend, WordRightExtend, LineStartExtend, LineEndExtend,
    DocumentStartExtend, DocumentEndExtend, PageUpExtend, PageDownExtend,
    Tab, BackTab,
}

/// <summary>
/// A single recorded macro action. Pure data — playback is implemented in the UI layer
/// (MacroPlayer) against the AvaloniaEdit editor. Serialized to macros.json.
/// </summary>
public sealed class MacroStep
{
    public MacroActionType ActionType { get; set; }
    public string? Data { get; set; }
    public MacroKeyCommand CommandId { get; set; } = MacroKeyCommand.None;
    public string? SearchText { get; set; }
    public string? ReplaceText { get; set; }
    public int Flags { get; set; }
    public bool IsRegex { get; set; }
    public bool IsBackward { get; set; }
    public bool IsWrap { get; set; }
    public bool IsReplace { get; set; }
    public bool IsPurge { get; set; }
    public bool IsBookmark { get; set; }

    public MacroStep() { }

    public MacroStep(MacroActionType type, string? data = null)
    {
        ActionType = type;
        Data = data;
    }

    /// <summary>Human-readable description for the macro editor list.</summary>
    public string Describe() => ActionType switch
    {
        MacroActionType.InsertText => $"Insert \"{Data}\"",
        MacroActionType.NewLine => "New Line",
        MacroActionType.Backspace => "Backspace",
        MacroActionType.Delete => "Delete",
        MacroActionType.KeyCommand => $"Key Command ({CommandId})",
        MacroActionType.FindReplace => IsReplace
            ? $"Replace \"{SearchText}\" → \"{ReplaceText}\""
            : $"Find \"{SearchText}\"",
        MacroActionType.ReplaceAll => $"Replace All \"{SearchText}\" → \"{ReplaceText}\"",
        MacroActionType.MarkAll => $"Mark All \"{SearchText}\"",
        MacroActionType.ClearMarks => "Clear All Marks",
        _ => ActionType.ToString(),
    };
}
