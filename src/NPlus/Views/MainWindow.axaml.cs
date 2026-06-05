using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using NPlus.Controls;
using NPlus.Core;
using NPlus.Models;
using TextMateSharp.Grammars;

namespace NPlus.Views;

public partial class MainWindow : Window
{
    // ---- Persistent state ----
    private readonly AppSettings _settings;
    private readonly RecentFilesStore _recent = new();
    private readonly MacroStore _macros = new();
    private readonly RegistryOptions _registryOptions = new(ThemeName.DarkPlus);

    // ---- View state ----
    private bool _isDarkMode;
    private bool _wordWrap;
    private bool _showCharacters;
    private bool _showIndentGuides;
    private bool _foldingEnabled = true;
    private bool _columnMode;
    private double _zoom = 1.0;
    private const double ZoomStep = 0.1, ZoomMin = AppSettings.ZoomMin, ZoomMax = AppSettings.ZoomMax;
    private const double BaseEditorFontSize = 14;
    private bool _checkForUpdatesOnStartup;

    // ---- Tab colors (matches original palette) ----
    private static readonly Color[] TabColors =
    {
        Color.FromRgb(240, 219, 79),   // 1 Yellow
        Color.FromRgb(120, 198, 110),  // 2 Green
        Color.FromRgb(95, 160, 228),   // 3 Blue
        Color.FromRgb(224, 90, 90),    // 4 Red
        Color.FromRgb(240, 150, 90),   // 5 Orange
    };

    // ---- Controls ----
    private TabControl _tabs = null!;
    private Menu _menu = null!;
    private StackPanel _toolbar = null!;
    private Border _statusBar = null!;
    private TextBlock _lblLength = null!, _lblCaret = null!, _lblEncoding = null!, _lblInsert = null!, _lblZoom = null!;
    private Grid _rootContent = null!;     // hosts side panel + tabs
    private GridSplitter _jsonSplitter = null!;
    private Border _jsonPanel = null!;
    private TreeView _jsonTree = null!;
    private TextBlock _jsonHeader = null!;
    private Border _findResultsPanel = null!;
    private ListBox _findResultsList = null!;
    private TextBlock _findResultsHeader = null!;
    private RowDefinition _findResultsRow = null!;

    // Toolbar toggle buttons we recolor on state change.
    private Button _btnShowChars = null!, _btnIndent = null!, _btnWrap = null!, _btnColumn = null!,
                   _btnLive = null!, _btnJson = null!, _btnHex = null!;

    private readonly Dictionary<TabItem, EditorDocument> _docs = new();
    private int _newCounter = 1;
    private bool _restoreMaximized;

    public MainWindow() : this(Array.Empty<string>()) { }

    public MainWindow(string[] filesToOpen)
    {
        _settings = AppSettings.Load();
        _isDarkMode = _settings.IsDarkMode;
        _wordWrap = _settings.WordWrap;
        _showCharacters = _settings.ShowCharacters;
        _showIndentGuides = _settings.ShowIndentGuides;
        _foldingEnabled = _settings.FoldingEnabled;
        _zoom = _settings.ZoomLevel;
        _checkForUpdatesOnStartup = _settings.CheckForUpdatesOnStartup;

        _recent.Load();
        _macros.Load();

        InitializeComponent();
        BuildUi();
        RestoreWindowBounds();
        ApplyTheme();

        // Restore session, then open any command-line files; ensure at least one tab.
        LoadSession();
        if (filesToOpen is { Length: > 0 })
            OpenFilesFromPaths(filesToOpen);
        if (_tabs.ItemCount == 0)
            AddNewTab($"new {_newCounter++}");

        if (_tabs.SelectedIndex < 0 && _tabs.ItemCount > 0)
            _tabs.SelectedIndex = 0;

        Closing += OnWindowClosing;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    // ===================================================================== UI

    private void BuildUi()
    {
        var dock = new DockPanel { LastChildFill = true };

        _menu = BuildMenu();
        DockPanel.SetDock(_menu, Dock.Top);
        dock.Children.Add(_menu);

        _toolbar = BuildToolbar();
        var toolbarBorder = new Border { Child = _toolbar, Padding = new Thickness(4, 2) };
        DockPanel.SetDock(toolbarBorder, Dock.Top);
        dock.Children.Add(toolbarBorder);

        _statusBar = BuildStatusBar();
        DockPanel.SetDock(_statusBar, Dock.Bottom);
        dock.Children.Add(_statusBar);

        // Center: a grid with optional JSON side panel | splitter | (tabs over find-results)
        _rootContent = new Grid();
        _rootContent.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Pixel)); // json panel
        _rootContent.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));        // splitter
        _rootContent.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));   // editor area

        _jsonPanel = BuildJsonPanel();
        Grid.SetColumn(_jsonPanel, 0);
        _rootContent.Children.Add(_jsonPanel);

        _jsonSplitter = new GridSplitter { Width = 4, IsVisible = false, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(_jsonSplitter, 1);
        _rootContent.Children.Add(_jsonSplitter);

        // Editor area: tabs on top row, find-results on bottom row.
        var editorArea = new Grid();
        editorArea.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        _findResultsRow = new RowDefinition(0, GridUnitType.Pixel);
        editorArea.RowDefinitions.Add(_findResultsRow);

        _tabs = new TabControl { Padding = new Thickness(0), Margin = new Thickness(0) };
        _tabs.SelectionChanged += OnTabSelectionChanged;
        Grid.SetRow(_tabs, 0);
        editorArea.Children.Add(_tabs);

        _findResultsPanel = BuildFindResultsPanel();
        Grid.SetRow(_findResultsPanel, 1);
        editorArea.Children.Add(_findResultsPanel);

        Grid.SetColumn(editorArea, 2);
        _rootContent.Children.Add(editorArea);

        dock.Children.Add(_rootContent);
        Content = dock;
    }

    private StackPanel BuildToolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        Button Tool(string glyph, string tip, Action onClick)
        {
            var b = new Button
            {
                Content = glyph,
                 Width = 30,
                Height = 28,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 14,
            };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => onClick();
            bar.Children.Add(b);
            return b;
        }
        void Sep() => bar.Children.Add(new Border { Width = 1, Margin = new Thickness(3, 4), Background = Brushes.Gray });

        Tool("📄", "New Document (Ctrl+N)", NewTab);
        Sep();
        Tool("💾", "Save (Ctrl+S)", SaveFile);
        Tool("💾²", "Save All", SaveAll);
        Tool("↺", "Revert to Saved", RevertToSaved);
        Sep();
        _btnShowChars = Tool("¶", "Show whitespace / EOL", ToggleShowCharacters);
        _btnIndent = Tool("⋮", "Indent guides", ToggleIndentGuides);
        _btnWrap = Tool("⤵", "Word wrap", ToggleWordWrap);
        _btnColumn = Tool("☰", "Column / block selection (Ctrl+Alt+A)", ToggleColumnMode);
        Sep();
        Tool("↶", "Undo", () => GetActiveEditor()?.Undo());
        Tool("●", "Start recording macro", StartRecording);
        Tool("■", "Stop recording macro", StopRecording);
        Tool("▶", "Playback macro (Ctrl+Shift+P)", () => PlaybackMacro(true));
        Sep();
        _btnLive = Tool("🔴", "Live monitor (tail) current file", () => ToggleLiveMonitor());
        _btnJson = Tool("{ }", "JSON tree explorer", ToggleJsonPanel);
        _btnHex = Tool("⧉", "Toggle hex view", ToggleHexView);
        Tool("🔍", "Find in Files (Ctrl+Shift+F)", () => ShowFind(2));
        Sep();
        Tool("☼", "Toggle light / dark theme", ToggleTheme);
        Tool("?", "User's Guide", ShowUserGuide);
        return bar;
    }

    private Border BuildStatusBar()
    {
        var grid = new Grid { Margin = new Thickness(8, 2) };
        for (int i = 0; i < 5; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(i == 0 ? GridLength.Star : GridLength.Auto));

        _lblLength = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _lblCaret = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        _lblEncoding = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        _lblInsert = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        _lblZoom = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        Grid.SetColumn(_lblLength, 0);
        Grid.SetColumn(_lblCaret, 1);
        Grid.SetColumn(_lblEncoding, 2);
        Grid.SetColumn(_lblInsert, 3);
        Grid.SetColumn(_lblZoom, 4);
        grid.Children.Add(_lblLength);
        grid.Children.Add(_lblCaret);
        grid.Children.Add(_lblEncoding);
        grid.Children.Add(_lblInsert);
        grid.Children.Add(_lblZoom);

        return new Border { Child = grid, Height = 24, BorderThickness = new Thickness(0, 1, 0, 0) };
    }

    // ===================================================================== Tabs

    public EditorDocument? ActiveDoc =>
        _tabs.SelectedItem is TabItem ti && _docs.TryGetValue(ti, out var d) ? d : null;

    public TextEditor? GetActiveEditor() => ActiveDoc?.Editor;

    private EditorDocument AddNewTab(string title, string? path = null)
    {
        var tab = new TabItem();
        var doc = new EditorDocument(tab) { BaseTitle = title, FilePath = path };

        var editor = CreateEditor(doc);
        doc.Editor = editor;
        tab.Content = editor;

        doc.HeaderChanged = UpdateTabHeader;
        tab.Header = BuildTabHeader(doc);
        _docs[tab] = doc;

        var items = (System.Collections.IList)_tabs.Items;
        items.Add(tab);
        _tabs.SelectedItem = tab;

        ApplyThemeToDoc(doc);
        ApplySyntax(doc);
        ApplyViewOptions(editor);
        ApplyZoomToEditor(editor);
        UpdateStatusBar();
        return doc;
    }

    private TextEditor CreateEditor(EditorDocument doc)
    {
        var editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = BaseEditorFontSize * _zoom,
            WordWrap = _wordWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.HighlightCurrentLine = true;

        var installation = editor.InstallTextMate(_registryOptions);
        doc.TextMate = installation;

        // Bookmark bar + mark-all highlight renderers.
        editor.TextArea.TextView.BackgroundRenderers.Add(new NPlus.Editor.LineMarkerRenderer(doc.Bookmarks));
        var markRenderer = new NPlus.Editor.MarkSegmentRenderer();
        editor.TextArea.TextView.BackgroundRenderers.Add(markRenderer);
        doc.MarkRenderer = markRenderer;

        if (_foldingEnabled)
            InstallFolding(doc, editor);

        editor.TextChanged += (_, _) =>
        {
            if (!doc.IsDirty) doc.IsDirty = true;
            UpdateStatusBar();
            ScheduleFoldingUpdate(doc);
        };
        editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatusBar();

        WireMacroRecording(editor);
        return editor;
    }

    private Control BuildTabHeader(EditorDocument doc)
    {
        var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var close = new Button
        {
            Content = "✕",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 11,
        };
        close.Click += (_, _) => CloseTab(doc);

        doc.StatusDot = dot;
        doc.TitleBlock = title;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(dot);
        panel.Children.Add(title);
        panel.Children.Add(close);

        // Right-click → color tag menu.
        panel.ContextMenu = BuildTabColorMenu(doc);
        UpdateTabHeader(doc);
        return panel;
    }

    private void UpdateTabHeader(EditorDocument doc)
    {
        if (doc.TitleBlock != null) doc.TitleBlock.Text = doc.DisplayTitle;
        if (doc.StatusDot != null)
        {
            // Amber = unsaved, green = saved (matches original tab dot semantics).
            doc.StatusDot.Fill = doc.IsDirty
                ? new SolidColorBrush(Color.FromRgb(240, 173, 78))
                : new SolidColorBrush(Color.FromRgb(92, 184, 92));
        }
        if (doc.TitleBlock != null)
        {
            if (doc.IsLive)
                doc.TitleBlock.Foreground = new SolidColorBrush(Color.FromRgb(212, 175, 55));
            else if (doc.ColorIndex >= 1 && doc.ColorIndex <= TabColors.Length)
                doc.TitleBlock.Foreground = new SolidColorBrush(TabColors[doc.ColorIndex - 1]);
            else
                doc.TitleBlock.Foreground = _isDarkMode ? Brushes.White : Brushes.Black;
        }
    }

    private ContextMenu BuildTabColorMenu(EditorDocument doc)
    {
        var menu = new ContextMenu();
        string[] names = { "Yellow", "Green", "Blue", "Red", "Orange" };
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i + 1;
            var item = new MenuItem { Header = names[i] };
            item.Click += (_, _) => { doc.ColorIndex = idx; UpdateTabHeader(doc); };
            ((System.Collections.IList)menu.Items).Add(item);
        }
        var remove = new MenuItem { Header = "Remove Color" };
        remove.Click += (_, _) => { doc.ColorIndex = 0; UpdateTabHeader(doc); };
        ((System.Collections.IList)menu.Items).Add(new Separator());
        ((System.Collections.IList)menu.Items).Add(remove);
        return menu;
    }

    private async void CloseTab(EditorDocument doc)
    {
        try
        {
            if (doc.IsDirty)
            {
                var result = await PromptSaveBeforeCloseAsync(doc);
                if (result == SaveCloseChoice.Cancel) return;
                if (result == SaveCloseChoice.Save) SaveTab(doc);
            }

            // Use the ItemCollection's public API directly. The non-generic IList.Remove
            // on Avalonia's ItemCollection throws "collection is read-only"; the public
            // ItemCollection.Remove/Add/RemoveAt are the supported mutators.
            var items = _tabs.Items;

            // Always keep at least one tab. Create the replacement BEFORE removing the
            // last tab so the TabControl never transiently holds zero items.
            if (items.Count <= 1)
            {
                AddNewTab($"new {_newCounter++}"); // moves selection to the new tab
            }
            else if (ReferenceEquals(_tabs.SelectedItem, doc.TabItem))
            {
                // Move selection off the tab we're about to remove.
                _tabs.SelectedIndex = 0;
                if (ReferenceEquals(_tabs.SelectedItem, doc.TabItem))
                    _tabs.SelectedIndex = 1;
            }

            doc.DisposeWatchers();
            _tabs.Items.Remove(doc.TabItem);
            _docs.Remove(doc.TabItem);
        }
        catch (Exception ex)
        {
            ShowMessage("n+", $"Could not close tab:\n{ex.Message}");
        }
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateStatusBar();
        UpdateToggleButtonVisuals();
    }

    // ===================================================================== Syntax / Theme

    private void ApplySyntax(EditorDocument doc)
    {
        if (doc.Editor == null || doc.TextMate == null) return;
        string? langId = SyntaxMap.GetLanguageId(doc.FilePath);
        if (langId != null)
        {
            var scope = _registryOptions.GetScopeByLanguageId(langId);
            if (scope != null) { doc.TextMate.SetGrammar(scope); return; }
        }
        doc.TextMate.SetGrammar(null);
    }

    private void ApplyTheme()
    {
        RequestedThemeVariant = _isDarkMode ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        foreach (var doc in _docs.Values)
        {
            ApplyThemeToDoc(doc);
            UpdateTabHeader(doc);
        }
        UpdateToggleButtonVisuals();
    }

    /// <summary>
    /// Applies the current light/dark palette to a single document. Must run for every
    /// editor — including tabs created after startup — so the TextMate token theme matches
    /// the editor background (otherwise text renders the same color as the background).
    /// </summary>
    private void ApplyThemeToDoc(EditorDocument doc)
    {
        var tmTheme = _registryOptions.LoadTheme(_isDarkMode ? ThemeName.DarkPlus : ThemeName.LightPlus);
        IBrush editorBg = _isDarkMode ? new SolidColorBrush(Color.FromRgb(30, 30, 35)) : Brushes.White;
        IBrush editorFg = _isDarkMode ? new SolidColorBrush(Color.FromRgb(240, 240, 240)) : Brushes.Black;

        doc.TextMate?.SetTheme(tmTheme);
        if (doc.Editor != null)
        {
            doc.Editor.Background = editorBg;
            doc.Editor.Foreground = editorFg;
        }
        doc.Hex?.SetColors(editorBg, editorFg);
    }

    private void ToggleTheme()
    {
        _isDarkMode = !_isDarkMode;
        ApplyTheme();
    }

    // ===================================================================== View options / Zoom

    private void ApplyViewOptions(TextEditor editor)
    {
        editor.WordWrap = _wordWrap;
        editor.Options.ShowSpaces = _showCharacters;
        editor.Options.ShowTabs = _showCharacters;
        editor.Options.ShowEndOfLine = _showCharacters;
    }

    private void ToggleShowCharacters()
    {
        _showCharacters = !_showCharacters;
        foreach (var d in _docs.Values) if (d.Editor != null) ApplyViewOptions(d.Editor);
        UpdateToggleButtonVisuals();
    }

    private void ToggleIndentGuides()
    {
        _showIndentGuides = !_showIndentGuides;
        // AvaloniaEdit has no built-in indent guides; tracked for parity and column ruler hint.
        foreach (var d in _docs.Values)
            if (d.Editor != null)
                d.Editor.Options.ShowColumnRulers = _showIndentGuides;
        UpdateToggleButtonVisuals();
    }

    private void ToggleWordWrap()
    {
        _wordWrap = !_wordWrap;
        foreach (var d in _docs.Values) if (d.Editor != null) d.Editor.WordWrap = _wordWrap;
        UpdateToggleButtonVisuals();
    }

    private void ZoomIn() { _zoom = Math.Min(ZoomMax, Math.Round(_zoom + ZoomStep, 2)); ApplyZoom(); }
    private void ZoomOut() { _zoom = Math.Max(ZoomMin, Math.Round(_zoom - ZoomStep, 2)); ApplyZoom(); }
    private void ZoomReset() { _zoom = 1.0; ApplyZoom(); }

    private void ApplyZoom()
    {
        foreach (var d in _docs.Values)
        {
            if (d.Editor != null) ApplyZoomToEditor(d.Editor);
            d.Hex?.SetFontSize(13 * _zoom);
        }
        UpdateStatusBar();
    }

    private void ApplyZoomToEditor(TextEditor editor) => editor.FontSize = Math.Max(8, BaseEditorFontSize * _zoom);

    private void UpdateToggleButtonVisuals()
    {
        var on = new SolidColorBrush(Color.FromRgb(135, 206, 250)); // LightSkyBlue
        IBrush off = Brushes.Transparent;
        _btnShowChars.Background = _showCharacters ? on : off;
        _btnIndent.Background = _showIndentGuides ? on : off;
        _btnWrap.Background = _wordWrap ? on : off;
        _btnColumn.Background = _columnMode ? on : off;
        _btnLive.Background = ActiveDoc?.IsLive == true ? on : off;
        _btnJson.Background = _jsonPanel.IsVisible ? on : off;
        _btnHex.Background = ActiveDoc?.IsHex == true ? on : off;
    }

    // ===================================================================== Status bar

    private void UpdateStatusBar()
    {
        var doc = ActiveDoc;
        var editor = doc?.Editor;
        if (editor?.Document != null)
        {
            int len = editor.Document.TextLength;
            var caret = editor.TextArea.Caret;
            _lblLength.Text = $"Length: {len:N0}";
            _lblCaret.Text = $"Ln: {caret.Line}  Col: {caret.Column}  Pos: {editor.CaretOffset}";
            _lblInsert.Text = editor.TextArea.OverstrikeMode ? "OVR" : "INS";
        }
        else if (doc?.IsHex == true)
        {
            _lblLength.Text = $"Length: {doc.Hex!.Bytes.Length:N0} bytes";
            _lblCaret.Text = "Hex view";
            _lblInsert.Text = "";
        }
        else
        {
            _lblLength.Text = "Length: 0";
            _lblCaret.Text = "";
            _lblInsert.Text = "";
        }
        _lblEncoding.Text = doc != null ? EncodingHelper.GetEncodingName(doc.Encoding) : "";
        _lblZoom.Text = $"{(int)Math.Round(_zoom * 100)}%";
    }

    // ===================================================================== Window lifecycle

    private void RestoreWindowBounds()
    {
        if (_settings.HasSavedBounds)
        {
            Position = new PixelPoint(_settings.WindowX, _settings.WindowY);
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }
        _restoreMaximized = _settings.IsMaximized;
        if (_restoreMaximized) WindowState = WindowState.Maximized;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSession();
        SaveSettingsFromState();
        _macros.Save();
        foreach (var d in _docs.Values) d.DisposeWatchers();
    }

    private void SaveSettingsFromState()
    {
        _settings.IsDarkMode = _isDarkMode;
        _settings.WordWrap = _wordWrap;
        _settings.ShowCharacters = _showCharacters;
        _settings.ShowIndentGuides = _showIndentGuides;
        _settings.FoldingEnabled = _foldingEnabled;
        _settings.ZoomLevel = _zoom;
        _settings.CheckForUpdatesOnStartup = _checkForUpdatesOnStartup;
        _settings.IsMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowX = Position.X;
            _settings.WindowY = Position.Y;
            _settings.WindowWidth = (int)Width;
            _settings.WindowHeight = (int)Height;
        }
        _settings.Save();
    }

    private void Exit() => Close();

    private void ShowUserGuide() =>
        ShowMessage("n+ — User's Guide", "n+ is a lightweight text/code editor.\n\nSee the README for the full feature list and keyboard shortcuts.");
}
