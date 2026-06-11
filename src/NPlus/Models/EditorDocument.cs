using System.Collections.Generic;
using System.IO;
using System.Text;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using NPlus.Controls;
using NPlus.Core;

namespace NPlus.Models;

/// <summary>
/// Per-tab document state — the Avalonia equivalent of the original's per-TabPage
/// dictionaries (encoding, dirty flag, color tag, watchers, live-tail offset).
/// A tab hosts EITHER a text editor or a hex view.
/// </summary>
public sealed class EditorDocument
{
    public TabItem TabItem { get; }

    // Exactly one of these is non-null.
    public TextEditor? Editor { get; set; }
    public HexView? Hex { get; set; }

    public string? FilePath { get; set; }
    public Encoding Encoding { get; set; } = new UTF8Encoding(false);

    /// <summary>1-based index into the tab color palette; 0 = no color.</summary>
    public int ColorIndex { get; set; }

    public bool IsReadOnly { get; set; }
    public bool IsLive { get; set; }

    public TextMate.Installation? TextMate { get; set; }
    public FoldingManager? FoldingManager { get; set; }
    public NPlus.Editor.BraceFoldingStrategy? FoldingStrategy { get; set; }
    public NPlus.Editor.MarkSegmentRenderer? MarkRenderer { get; set; }

    // External-change detection + live-tail watchers.
    public FileSystemWatcher? ChangeWatcher { get; set; }
    public FileSystemWatcher? LiveWatcher { get; set; }
    public long LiveOffset { get; set; }
    public bool ChangePromptOpen { get; set; }

    // Bookmarked line numbers (1-based) for the editor.
    public HashSet<int> Bookmarks { get; } = new();

    // Header sub-controls (kept so headers update cheaply).
    public TextBlock? TitleBlock { get; set; }
    public Ellipse? StatusDot { get; set; }
    public Border? HeaderBorder { get; set; }

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set { _isDirty = value; HeaderChanged?.Invoke(this); }
    }

    /// <summary>The tab title without the dirty asterisk.</summary>
    public string BaseTitle { get; set; } = "new 1";

    /// <summary>Raised when the title/dirty/color state changes so the UI can repaint the header.</summary>
    public System.Action<EditorDocument>? HeaderChanged;

    public bool IsHex => Hex != null;

    public EditorDocument(TabItem tabItem)
    {
        TabItem = tabItem;
    }

    public string DisplayTitle => IsDirty ? BaseTitle + "*" : BaseTitle;

    /// <summary>Current document text (empty for hex tabs handled separately).</summary>
    public string Text => Editor?.Document?.Text ?? string.Empty;

    public void RaiseHeaderChanged() => HeaderChanged?.Invoke(this);

    public void DisposeWatchers()
    {
        try { ChangeWatcher?.Dispose(); } catch { }
        try { LiveWatcher?.Dispose(); } catch { }
        ChangeWatcher = null;
        LiveWatcher = null;
    }
}
