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

        var inputArea = new StackPanel { Spacing = 6, Margin = new Thickness(8), Children = { _aiInput, inputButtons } };

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
        _ = SendUserMessage(text);
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
