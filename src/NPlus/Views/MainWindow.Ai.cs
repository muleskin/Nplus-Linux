using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using NPlus.Ai;
using NPlus.Core;
using NPlus.Dialogs;

namespace NPlus.Views;

public partial class MainWindow
{
    // ---- AI state ----
    private AiSettings _ai = new();
    private readonly List<ChatMessage> _conversation = new();
    private CancellationTokenSource? _aiCts;
    private bool _aiBusy;
    private bool _agentMode;

    private const string AiSystemPrompt =
        "You are a helpful assistant embedded in the n+ text editor. Be concise and practical. " +
        "When the user shares code or text, work with exactly what they provide.";

    // ---- AI panel controls ----
    private Border _aiPanel = null!;
    private GridSplitter _aiSplitter = null!;
    private StackPanel _aiMessages = null!;
    private ScrollViewer _aiScroll = null!;
    private TextBox _aiInput = null!;
    private Button _aiSendBtn = null!;
    private Button _aiStopBtn = null!;
    private CheckBox _agentToggle = null!;
    private TextBlock _aiProviderLabel = null!;
    private MenuItem? _aiEnableItem;

    // ===================================================================== Panel

    private Border BuildAiPanel()
    {
        var title = new TextBlock { Text = "AI Chat", Margin = new Thickness(8, 4, 8, 0), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _aiProviderLabel = new TextBlock { Margin = new Thickness(8, 0), FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var titleStack = new StackPanel { Children = { title, _aiProviderLabel } };

        var settingsBtn = new Button { Content = "⚙", Width = 24, Height = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ToolTip.SetTip(settingsBtn, "AI settings");
        settingsBtn.Click += (_, _) => OpenAiSettings();
        var clearBtn = new Button { Content = "🗑", Width = 24, Height = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ToolTip.SetTip(clearBtn, "Clear conversation");
        clearBtn.Click += (_, _) => ClearConversation();
        var close = new Button { Content = "✕", Width = 24, Height = 22, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => HideAiPanel();

        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { settingsBtn, clearBtn, close } };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(headerButtons, 1);
        header.Children.Add(titleStack);
        header.Children.Add(headerButtons);

        _aiMessages = new StackPanel { Margin = new Thickness(8) };
        _aiScroll = new ScrollViewer { Content = _aiMessages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _aiInput = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "Ask anything…  (Enter to send, Shift+Enter for a new line)",
            MinHeight = 56,
            MaxHeight = 140,
        };
        _aiInput.AddHandler(KeyDownEvent, OnAiInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _aiSendBtn = new Button { Content = "Send", MinWidth = 70, IsDefault = false };
        _aiSendBtn.Click += (_, _) => SendCurrentInput();
        _aiStopBtn = new Button { Content = "Stop", MinWidth = 70, IsVisible = false };
        _aiStopBtn.Click += (_, _) => _aiCts?.Cancel();
        var inputButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { _aiStopBtn, _aiSendBtn } };

        _agentToggle = new CheckBox { Content = "Agent (let AI edit this tab)", VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_agentToggle, "When on, the AI can read and propose edits to the active tab — every change is shown for confirmation first.");
        _agentToggle.IsCheckedChanged += (_, _) => _agentMode = _agentToggle.IsChecked == true;

        var btnRow = new Grid();
        btnRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        btnRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(_agentToggle, 0);
        Grid.SetColumn(inputButtons, 1);
        btnRow.Children.Add(_agentToggle);
        btnRow.Children.Add(inputButtons);

        var inputArea = new StackPanel { Spacing = 6, Margin = new Thickness(8), Children = { _aiInput, btnRow } };

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(inputArea, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(inputArea);
        dock.Children.Add(_aiScroll);

        return new Border { Child = dock, BorderThickness = new Thickness(1, 0, 0, 0), IsVisible = false };
    }

    private void OnAiInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            SendCurrentInput();
        }
    }

    private void ToggleAiPanel()
    {
        if (_aiPanel.IsVisible) HideAiPanel();
        else ShowAiPanel();
    }

    private void ShowAiPanel()
    {
        _aiPanel.IsVisible = true;
        _aiSplitter.IsVisible = true;
        _rootContent.ColumnDefinitions[4].Width = new GridLength(Math.Max(320, Width * 0.30), GridUnitType.Pixel);
        UpdateAiProviderLabel();
        UpdateToggleButtonVisuals();
        _aiInput.Focus();
    }

    private void HideAiPanel()
    {
        _aiPanel.IsVisible = false;
        _aiSplitter.IsVisible = false;
        _rootContent.ColumnDefinitions[4].Width = new GridLength(0, GridUnitType.Pixel);
        UpdateToggleButtonVisuals();
    }

    private void UpdateAiProviderLabel()
    {
        string name = AiProviders.TryParse(_ai.Provider, out var p) ? AiProviders.Info(p).DisplayName : _ai.Provider;
        _aiProviderLabel.Text = _ai.Enabled ? name : $"{name} — disabled";
    }

    // ===================================================================== Conversation

    private void ClearConversation()
    {
        _aiCts?.Cancel();
        _conversation.Clear();
        _aiMessages.Children.Clear();
    }

    /// <summary>Adds a chat bubble and returns its body block so streaming can keep updating it.</summary>
    private SelectableTextBlock AddMessageBubble(string role, string text)
    {
        bool user = role == ChatMessage.User;
        IBrush bg = user
            ? new SolidColorBrush(_isDarkMode ? Color.FromRgb(48, 58, 72) : Color.FromRgb(225, 236, 250))
            : new SolidColorBrush(_isDarkMode ? Color.FromRgb(44, 48, 54) : Color.FromRgb(240, 240, 240));

        var name = new TextBlock { Text = user ? "You" : "Assistant", FontWeight = FontWeight.SemiBold, FontSize = 11, Opacity = 0.7 };
        var body = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        var stack = new StackPanel { Spacing = 3, Children = { name, body } };
        var bubble = new Border { Child = stack, Background = bg, CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8) };
        _aiMessages.Children.Add(bubble);
        AiScrollToEnd();
        return body;
    }

    private void AiScrollToEnd() =>
        Dispatcher.UIThread.Post(() => _aiScroll.Offset = _aiScroll.Offset.WithY(_aiScroll.Extent.Height), DispatcherPriority.Background);

    private void SendCurrentInput()
    {
        var text = _aiInput.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _aiBusy) return;
        if (!EnsureAiReady()) return;
        _aiInput.Text = "";
        if (_agentMode) _ = RunAgent(text);
        else _ = SendUserMessage(text);
    }

    private async Task SendUserMessage(string userText)
    {
        AddMessageBubble(ChatMessage.User, userText);
        _conversation.Add(new ChatMessage(ChatMessage.User, userText));
        await StreamAssistantReply();
    }

    private async Task StreamAssistantReply()
    {
        if (!AiProviders.TryParse(_ai.Provider, out var provider)) return;
        var cfg = _ai.ConfigFor(provider.ToString());

        SetAiBusy(true);
        var body = AddMessageBubble(ChatMessage.Assistant, "");
        var sb = new StringBuilder();
        var cts = new CancellationTokenSource();
        _aiCts = cts;

        var messages = new List<ChatMessage> { new(ChatMessage.System, AiSystemPrompt) };
        messages.AddRange(_conversation);

        try
        {
            var client = new AiClient(provider, cfg);
            await foreach (var token in client.StreamAsync(messages, cts.Token))
            {
                sb.Append(token);
                body.Text = sb.ToString();
                AiScrollToEnd();
            }
            if (sb.Length == 0) body.Text = "(no content returned)";
            _conversation.Add(new ChatMessage(ChatMessage.Assistant, sb.ToString()));
        }
        catch (OperationCanceledException)
        {
            body.Text = sb.Length > 0 ? sb + "\n\n[stopped]" : "[stopped]";
            if (sb.Length > 0) _conversation.Add(new ChatMessage(ChatMessage.Assistant, sb.ToString()));
        }
        catch (Exception ex)
        {
            body.Text = "⚠ " + ex.Message;
            body.Foreground = Brushes.IndianRed;
        }
        finally
        {
            SetAiBusy(false);
            _aiCts = null;
        }
    }

    private void SetAiBusy(bool busy)
    {
        _aiBusy = busy;
        _aiSendBtn.IsEnabled = !busy;
        _aiStopBtn.IsVisible = busy;
    }

    // ===================================================================== Agent mode

    private async Task RunAgent(string userText)
    {
        if (GetActiveEditor()?.Document == null)
        {
            ShowMessage("AI", "Agent mode works on a text tab — open or focus one first.");
            return;
        }
        if (!AiProviders.TryParse(_ai.Provider, out var provider)) return;
        var cfg = _ai.ConfigFor(provider.ToString());

        AddMessageBubble(ChatMessage.User, userText);
        _conversation.Add(new ChatMessage(ChatMessage.User, userText));

        SetAiBusy(true);
        var cts = new CancellationTokenSource();
        _aiCts = cts;
        try
        {
            var client = new AiClient(provider, cfg);
            var editor = BuildScriptContext("agent");
            var runner = new AgentRunner(client, editor, ApplyAgentWrites,
                msg => AddMessageBubble(ChatMessage.Assistant, msg),
                AddAgentStatus);

            string finalMsg = await runner.RunAsync(new List<ChatMessage>(_conversation), cts.Token);
            // The final message was already shown as a bubble during the loop; keep it in history only.
            if (!string.IsNullOrWhiteSpace(finalMsg))
                _conversation.Add(new ChatMessage(ChatMessage.Assistant, finalMsg));
        }
        catch (OperationCanceledException) { AddAgentStatus("[stopped]"); }
        catch (Exception ex)
        {
            var b = AddMessageBubble(ChatMessage.Assistant, "⚠ " + ex.Message);
            b.Foreground = Brushes.IndianRed;
        }
        finally
        {
            SetAiBusy(false);
            _aiCts = null;
        }
    }

    private void AddAgentStatus(string text)
    {
        var tb = new TextBlock
        {
            Text = "• " + text,
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            Opacity = 0.6,
            Margin = new Thickness(2, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
        _aiMessages.Children.Add(tb);
        AiScrollToEnd();
    }

    /// <summary>Confirm callback for the agent: previews proposed edits, then applies them as one undo step.</summary>
    private async Task<bool> ApplyAgentWrites(IReadOnlyList<AgentAction> writes)
    {
        if (GetActiveEditor()?.Document == null) return false;
        bool ok = await ShowWriteConfirmDialog(writes);
        if (!ok) return false;
        return ApplyWritesToEditor(writes);
    }

    private bool ApplyWritesToEditor(IReadOnlyList<AgentAction> writes)
    {
        var ed = GetActiveEditor();
        if (ed?.Document == null) return false;
        using (ed.Document.RunUpdate())
        {
            foreach (var w in writes)
            {
                string text = w.Text ?? "";
                switch (w.Op)
                {
                    case "set_text":
                        ed.Document.Text = text;
                        break;
                    case "set_lines":
                        ed.Document.Text = string.Join(GetNewline(ed), w.Lines ?? new List<string>());
                        break;
                    case "replace_selection":
                        if (ed.SelectionLength > 0)
                            ed.Document.Replace(ed.SelectionStart, ed.SelectionLength, text);
                        else { ed.Document.Insert(ed.CaretOffset, text); ed.CaretOffset += text.Length; }
                        break;
                    case "insert":
                        ed.Document.Insert(ed.CaretOffset, text);
                        ed.CaretOffset += text.Length;
                        break;
                }
            }
        }
        return true;
    }

    private async Task<bool> ShowWriteConfirmDialog(IReadOnlyList<AgentAction> writes)
    {
        string Describe(AgentAction w) => w.Op switch
        {
            "set_text" => $"Replace the ENTIRE document ({(w.Text ?? "").Length} chars)",
            "set_lines" => $"Replace document with {(w.Lines?.Count ?? 0)} line(s)",
            "replace_selection" => $"Replace selection / insert at caret ({(w.Text ?? "").Length} chars)",
            "insert" => $"Insert at caret ({(w.Text ?? "").Length} chars)",
            _ => w.Op,
        };

        var details = new StackPanel { Spacing = 10 };
        foreach (var w in writes)
        {
            string preview = w.Op == "set_lines"
                ? string.Join("\n", w.Lines ?? new List<string>())
                : (w.Text ?? "");
            if (preview.Length > 1500) preview = preview.Substring(0, 1500) + "\n…";

            details.Children.Add(new TextBlock { Text = "▸ " + Describe(w), FontWeight = FontWeight.SemiBold });
            details.Children.Add(new Border
            {
                Background = new SolidColorBrush(_isDarkMode ? Color.FromRgb(40, 44, 50) : Color.FromRgb(245, 245, 245)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Child = new SelectableTextBlock
                {
                    Text = preview,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"),
                    FontSize = 12,
                },
            });
        }

        var scroll = new ScrollViewer { Content = details, MaxHeight = 380, MaxWidth = 560 };
        bool accepted = false;
        var apply = new Button { Content = "Apply", MinWidth = 90, IsDefault = true };
        var reject = new Button { Content = "Reject", MinWidth = 90, IsCancel = true };
        var dialog = new Window
        {
            Title = $"AI wants to make {writes.Count} change(s) to {ActiveDoc?.BaseTitle ?? "this tab"}",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        apply.Click += (_, _) => { accepted = true; dialog.Close(); };
        reject.Click += (_, _) => dialog.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { apply, reject } };
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { scroll, buttons } };
        await dialog.ShowDialog(this);
        return accepted;
    }

    /// <summary>Returns true if AI is enabled and the active provider is configured; otherwise explains why.</summary>
    private bool EnsureAiReady()
    {
        if (!_ai.Enabled)
        {
            ShowMessage("AI", "AI features are turned off.\n\nEnable them in the AI menu (AI ▸ Enable AI) or AI ▸ Settings.");
            return false;
        }
        if (!AiProviders.TryParse(_ai.Provider, out var provider))
        {
            ShowMessage("AI", "No AI provider selected. Choose one in AI ▸ Settings.");
            return false;
        }
        var info = AiProviders.Info(provider);
        var cfg = _ai.ConfigFor(provider.ToString());
        if (info.NeedsApiKey && string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            ShowMessage("AI", $"{info.DisplayName} needs an API key.\n\nAdd it in AI ▸ Settings (and use “Test connection” to verify).");
            return false;
        }
        return true;
    }

    // ===================================================================== Selection actions

    private async void OpenAiSettings()
    {
        bool saved = await AiSettingsDialog.Show(this, _ai);
        if (!saved) return;
        _ai.Save();
        UpdateAiProviderLabel();
        if (_aiEnableItem != null) _aiEnableItem.IsChecked = _ai.Enabled;
        UpdateToggleButtonVisuals();
    }

    private void ToggleAiEnabled()
    {
        _ai.Enabled = !_ai.Enabled;
        _ai.Save();
        if (_aiEnableItem != null) _aiEnableItem.IsChecked = _ai.Enabled;
        UpdateAiProviderLabel();
    }

    private void AiAction(string instruction, bool fenceCode)
    {
        if (!EnsureAiReady()) return;
        var ed = GetActiveEditor();
        var sel = ed?.SelectedText ?? "";
        if (string.IsNullOrWhiteSpace(sel))
        {
            ShowMessage("AI", "Select some text in the editor first.");
            return;
        }
        ShowAiPanel();
        string prompt = fenceCode ? $"{instruction}\n\n```\n{sel}\n```" : $"{instruction}\n\n{sel}";
        _ = SendUserMessage(prompt);
    }

    private void AiExplainSelection() => AiAction("Explain the following:", true);
    private void AiImproveSelection() => AiAction("Improve the following and briefly note the key changes:", true);
    private void AiSummarizeSelection() => AiAction("Summarize the following:", false);

    private async void AiAskAboutSelection()
    {
        if (!EnsureAiReady()) return;
        var ed = GetActiveEditor();
        var sel = ed?.SelectedText ?? "";
        if (string.IsNullOrWhiteSpace(sel)) { ShowMessage("AI", "Select some text in the editor first."); return; }
        string? question = await MessageBoxes.Prompt(this, "Ask about selection", "What would you like to ask about the selected text?");
        if (string.IsNullOrWhiteSpace(question)) return;
        ShowAiPanel();
        await SendUserMessage($"{question}\n\n```\n{sel}\n```");
    }

    /// <summary>Drops the current selection into the chat input so the user can frame their own question.</summary>
    private void AiSendSelectionToChat()
    {
        var ed = GetActiveEditor();
        var sel = ed?.SelectedText ?? "";
        if (string.IsNullOrWhiteSpace(sel)) { ShowMessage("AI", "Select some text in the editor first."); return; }
        ShowAiPanel();
        string prefix = string.IsNullOrEmpty(_aiInput.Text) ? "" : _aiInput.Text + "\n\n";
        _aiInput.Text = $"{prefix}```\n{sel}\n```";
        _aiInput.CaretIndex = _aiInput.Text.Length;
        _aiInput.Focus();
    }

    // ---- Editor right-click context menu (Cut/Copy/Paste + AI actions) ----

    private ContextMenu BuildEditorContextMenu(TextEditor editor)
    {
        MenuItem Item(string header, Action onClick)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => onClick();
            return mi;
        }

        var ai = new MenuItem { Header = "AI" };
        var aiItems = (System.Collections.IList)ai.Items;
        aiItems.Add(Item("Explain Selection", AiExplainSelection));
        aiItems.Add(Item("Improve Selection", AiImproveSelection));
        aiItems.Add(Item("Summarize Selection", AiSummarizeSelection));
        aiItems.Add(Item("Ask about Selection…", AiAskAboutSelection));
        aiItems.Add(new Separator());
        aiItems.Add(Item("Send Selection to Chat", AiSendSelectionToChat));

        var menu = new ContextMenu();
        var items = (System.Collections.IList)menu.Items;
        items.Add(Item("Cut", () => editor.Cut()));
        items.Add(Item("Copy", () => editor.Copy()));
        items.Add(Item("Paste", () => editor.Paste()));
        items.Add(new Separator());
        items.Add(ai);
        return menu;
    }
}
