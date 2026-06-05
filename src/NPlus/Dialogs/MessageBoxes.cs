using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace NPlus.Dialogs;

public enum MessageButtons { Ok, YesNo, YesNoCancel, OkCancel }
public enum MessageResult { Ok, Yes, No, Cancel }

/// <summary>Minimal async message-box dialogs (Avalonia ships none).</summary>
public static class MessageBoxes
{
    public static async Task<MessageResult> Show(Window owner, string title, string message, MessageButtons buttons = MessageButtons.Ok)
    {
        var result = MessageResult.Cancel;

        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
        });

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        void Add(string text, MessageResult r, bool isDefault = false)
        {
            var b = new Button { Content = text, MinWidth = 84, IsDefault = isDefault };
            b.Click += (_, _) => { result = r; dialog.Close(); };
            buttonRow.Children.Add(b);
        }

        switch (buttons)
        {
            case MessageButtons.Ok: Add("OK", MessageResult.Ok, true); break;
            case MessageButtons.OkCancel: Add("OK", MessageResult.Ok, true); Add("Cancel", MessageResult.Cancel); break;
            case MessageButtons.YesNo: Add("Yes", MessageResult.Yes, true); Add("No", MessageResult.No); break;
            case MessageButtons.YesNoCancel: Add("Yes", MessageResult.Yes, true); Add("No", MessageResult.No); Add("Cancel", MessageResult.Cancel); break;
        }

        stack.Children.Add(buttonRow);
        dialog.Content = stack;
        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Prompts for a single line of text. Returns null if cancelled.</summary>
    public static async Task<string?> Prompt(Window owner, string title, string label, string initial = "")
    {
        string? result = null;
        var box = new TextBox { Text = initial, Width = 360 };
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = label });
        stack.Children.Add(box);

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", MinWidth = 84, IsDefault = true };
        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        cancel.Click += (_, _) => { result = null; dialog.Close(); };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        stack.Children.Add(row);

        dialog.Content = stack;
        box.AttachedToVisualTree += (_, _) => box.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }
}
