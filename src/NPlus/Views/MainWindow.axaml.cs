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
                   _btnLive = null!, _btnJson = null!, _btnHex = null!, _btnAi = null!;

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
        _ai = AiSettings.Load();

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

        // Center: json panel | splitter | (tabs over find-results) | splitter | AI chat panel
        _rootContent = new Grid();
        _rootContent.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Pixel)); // 0 json panel
        _rootContent.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));        // 1 json splitter
        _rootContent.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));   // 2 editor area
        _rootContent.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));        // 3 ai splitter
        _rootContent.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Pixel)); // 4 ai panel

        _jsonPanel = BuildJsonPanel();
        Grid.SetColumn(_jsonPanel, 0);
        _rootContent.Children.Add(_jsonPanel);

        _jsonSplitter = new GridSplitter { Width = 4, IsVisible = false, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(_jsonSplitter, 1);
        _rootContent.Children.Add(_jsonSplitter);

        _aiSplitter = new GridSplitter { Width = 4, IsVisible = false, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(_aiSplitter, 3);
        _rootContent.Children.Add(_aiSplitter);

        _aiPanel = BuildAiPanel();
        Grid.SetColumn(_aiPanel, 4);
        _rootContent.Children.Add(_aiPanel);

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
        Tool("🔴", "Start recording macro", StartRecording);
        Tool("■", "Stop recording macro", StopRecording);
        Tool("▶", "Playback macro (Ctrl+Shift+P)", () => PlaybackMacro(true));
        Sep();
        _btnLive = Tool("📡", "Live monitor (tail) current file", () => ToggleLiveMonitor());
        _btnJson = Tool("{ }", "JSON tree explorer", ToggleJsonPanel);
        _btnHex = Tool("⧉", "Toggle hex view", ToggleHexView);
        Tool("🔍", "Find in Files (Ctrl+Shift+F)", () => ShowFind(2));
        Sep();
        _btnAi = Tool("🤖", "AI chat panel (Ctrl+Shift+A)", ToggleAiPanel);
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
        editor.ContextMenu = BuildEditorContextMenu(editor);

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

        // Wrap in a Border so the tab can take on the applied color as its background.
        var border = new Border
        {
            Child = panel,
            Padding = new Thickness(8, 3),
            CornerRadius = new CornerRadius(3),
            ContextMenu = BuildTabColorMenu(doc), // right-click → color tag menu
        };
        doc.HeaderBorder = border;

        UpdateTabHeader(doc);
        return border;
    }

    private static readonly Color LiveColor = Color.FromRgb(212, 175, 55); // gold

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

        // Tab background carries the applied color (or gold while live-monitoring);
        // the text switches to black/white for contrast.
        Color? bg = null;
        if (doc.IsLive) bg = LiveColor;
        else if (doc.ColorIndex >= 1 && doc.ColorIndex <= TabColors.Length) bg = TabColors[doc.ColorIndex - 1];

        if (doc.HeaderBorder != null)
            doc.HeaderBorder.Background = bg.HasValue ? new SolidColorBrush(bg.Value) : Brushes.Transparent;

        if (doc.TitleBlock != null)
            doc.TitleBlock.Foreground = bg.HasValue
                ? ContrastBrush(bg.Value)
                : (_isDarkMode ? Brushes.White : Brushes.Black);
    }

    /// <summary>Black or white, whichever reads better on the given background color.</summary>
    private static IBrush ContrastBrush(Color c)
    {
        double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        return luminance > 140 ? Brushes.Black : Brushes.White;
    }

    private ContextMenu BuildTabColorMenu(EditorDocument doc)
    {
        var menu = new ContextMenu();
        string[] names = { "Yellow", "Green", "Blue", "Red", "Orange" };
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i + 1;
            var item = new MenuItem { Header = $"Apply {names[i]}" };
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
        // Large files run without highlighting to stay responsive.
        if (doc.IsLargeFile) { doc.TextMate.SetGrammar(null); return; }
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
        _btnAi.Background = _aiPanel.IsVisible ? on : off;
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
        if (doc?.IsLargeFile == true) _lblEncoding.Text += "  •  LARGE (read-only)";
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
        _ai.Save();
        _aiCts?.Cancel();
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

    private void ShowUserGuide()
    {
        var content = new StackPanel { Spacing = 2 };

        void Section(string heading, string body)
        {
            content.Children.Add(new TextBlock
            {
                Text = heading,
                FontWeight = FontWeight.Bold,
                FontSize = 15,
                Margin = new Thickness(0, 14, 0, 3),
            });
            content.Children.Add(new SelectableTextBlock { Text = body, TextWrapping = TextWrapping.Wrap });
        }

        content.Children.Add(new TextBlock { Text = "n+ — User's Guide", FontWeight = FontWeight.Bold, FontSize = 19 });
        content.Children.Add(new TextBlock
        {
            Text = "A lightweight, cross-platform text and code editor. This guide covers what's built in; "
                 + "the README has the full reference.",
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
        });

        Section("Files, tabs & sessions",
            "• New/Open/Save/Save As/Save All from the File menu or toolbar; Open by Path… works without a native file dialog.\n"
          + "• Tabs show a save dot (green = saved, amber = unsaved); right-click a tab for a colour tag.\n"
          + "• Hot exit: open tabs, unsaved edits, window position and zoom are restored next launch.\n"
          + "• External changes are detected — you're prompted to reload, keep, or track a rename. Locked files open read-only.");

        Section("Editing & lines",
            "• Syntax highlighting (TextMate) for many languages, brace folding, word wrap, whitespace/EOL display.\n"
          + "• Column/block selection with Alt+drag (toggle with Ctrl+Alt+A).\n"
          + "• Line ops: duplicate, split/join, move up/down, remove duplicate/empty lines, insert blanks, reverse/randomise.\n"
          + "• Sort lines: lexicographic, locale, integer, decimal (dot/comma) or by length, asc/desc, case-insensitive.\n"
          + "• Blank ops: trim leading/trailing, tabs ↔ spaces, EOL → space.");

        Section("Find, Replace, Mark & Find in Files",
            "• Find (Ctrl+F), Replace (Ctrl+H), Mark (Ctrl+B) with Normal, Extended (\\n, \\t) and Regex modes; F3 finds next.\n"
          + "• Mark highlights every match and can bookmark each matching line.\n"
          + "• Find in Files (Ctrl+Shift+F) and Replace in Files across a folder with type filters and recursion; results dock at the bottom — double-click a hit to jump to it.");

        Section("Bookmarks",
            "• Toggle with Ctrl+F2; navigate with F2 / Shift+F2.\n"
          + "• Copy, cut or delete bookmarked lines, delete non-bookmarked lines, or paste-to-bookmarks.");

        Section("Encoding & Hex view",
            "• Auto-detected encoding with convert-to options: ANSI (Windows-1252), UTF-8, UTF-8 BOM, UTF-16 LE/BE BOM.\n"
          + "• Read-only hex view for binary files (toolbar ⧉), and a read-only fallback for locked files.");

        Section("JSON tools",
            "• Format / pretty-print the current document, or explore it in a dockable visual tree (Tools ▸ JSON).");

        Section("Macros",
            "• Record keystrokes, navigation and Find/Replace actions; play back once, N times, or to end-of-file.\n"
          + "• Save, load and edit macros step-by-step; saved macros persist between sessions.\n"
          + "• Alt+Shift+S trims trailing spaces and saves in one step.");

        Section("Lua scripting",
            "• Tools ▸ Scripting (Lua) runs a script against the active tab. Run the current document with F5, run any .lua file, "
          + "or pick one from your scripts folder (Open Scripts Folder).\n"
          + "• Scripts use the 'editor' API: text()/set_text, selection()/replace_selection, insert(), lines()/set_lines, "
          + "line_count(), caret_line(), file_path(), language(). print(...) output is shown after the run; each run is one undo step.\n"
          + "• Sandboxed: string/table/math/os(time) are available, but file I/O, os.execute and loadfile/dofile are not.\n"
          + "• Starter examples (reverse lines, upper-case, insert date, word count) are seeded on first run.");

        Section("AI assistant (optional)",
            "• Off by default — nothing reaches the network until you enable it in AI ▸ Settings.\n"
          + "• Choose a backend: OpenAI (ChatGPT), Azure OpenAI, Google Gemini, Anthropic Claude, Ollama (local) or Perplexity — "
          + "each with its own key/endpoint/model. Use 'Test connection' to verify it.\n"
          + "• Chat panel: Ctrl+Shift+A or the 🤖 toolbar button. Responses stream token-by-token; Stop cancels.\n"
          + "• Selection actions (AI menu or editor right-click): Explain, Improve, Summarize, Ask about Selection…, or Send Selection to Chat.");

        Section("AI agent mode",
            "• Tick 'Agent (let AI edit this tab)' in the chat panel to let the assistant act on the active tab, not just chat.\n"
          + "• It reads the document/selection, then proposes edits (replace selection, rewrite document, set lines, insert).\n"
          + "• Every change is shown for confirmation before it's applied, and each confirmed batch is a single undo step. "
          + "Reject leaves the document untouched.");

        Section("Live monitoring (tail)",
            "• Toggle 'Live' (📡) on a saved file to auto-reload and auto-scroll as it grows — tail rolling logs in place.");

        Section("View, themes & zoom",
            "• Light/Dark theme toggle (syntax colours follow). Fold view, collapse/expand all.\n"
          + "• Zoom in/out/reset with F11 / F12 / Ctrl+0; the level persists between sessions.");

        Section("Keyboard shortcuts",
            "Ctrl+N/O/S  New / Open / Save        Ctrl+Shift+S  Save As\n"
          + "Ctrl+F/H/B  Find / Replace / Mark    F3            Find Next\n"
          + "Ctrl+Shift+F  Find in Files          Ctrl+Alt+A    Column mode\n"
          + "Ctrl+D  Duplicate line              Ctrl+I / Ctrl+J  Split / Join lines\n"
          + "Ctrl+Shift+L  Delete line            Ctrl+Shift+Up/Down  Move line\n"
          + "Ctrl+F2  Toggle bookmark            F2 / Shift+F2  Next / Prev bookmark\n"
          + "Ctrl+Shift+P  Playback macro         Alt+Shift+S  Trim trailing + save\n"
          + "F5  Run document as Lua             Ctrl+Shift+A  Toggle AI chat panel\n"
          + "F11 / F12 / Ctrl+0  Zoom in / out / reset");

        Section("Where settings live",
            "Config, session, recent files, macros, Lua scripts and AI settings live under ~/.config/nplus/ "
          + "(scripts in scripts/, AI provider settings in ai.json).");

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var close = new Button { Content = "Close", MinWidth = 90, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = "n+ — User's Guide",
            Width = 680,
            Height = 620,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        close.Click += (_, _) => dialog.Close();

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        DockPanel.SetDock(close, Dock.Bottom);
        close.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(close);
        root.Children.Add(scroll);
        dialog.Content = root;

        _ = dialog.ShowDialog(this);
    }
}
