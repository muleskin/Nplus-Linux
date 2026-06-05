using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NPlus.Core;

namespace NPlus.Views;

public partial class MainWindow
{
    private Border BuildJsonPanel()
    {
        _jsonHeader = new TextBlock { Text = "JSON", Margin = new Thickness(8, 4), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = "✕", Width = 24, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => HideJsonPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(_jsonHeader, 0);
        Grid.SetColumn(close, 1);
        header.Children.Add(_jsonHeader);
        header.Children.Add(close);

        _jsonTree = new TreeView();

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(_jsonTree);

        return new Border { Child = dock, BorderThickness = new Thickness(0, 0, 1, 0), IsVisible = false };
    }

    private void FormatJson()
    {
        var ed = GetActiveEditor();
        if (ed?.Document == null) return;
        try
        {
            ed.Document.Text = JsonTools.Format(ed.Document.Text);
        }
        catch (JsonException ex)
        {
            ShowMessage("JSON", $"Invalid JSON:\n{ex.Message}");
        }
    }

    private void ToggleJsonPanel()
    {
        if (_jsonPanel.IsVisible) HideJsonPanel();
        else ShowJsonTree();
        UpdateToggleButtonVisuals();
    }

    private void HideJsonPanel()
    {
        _jsonPanel.IsVisible = false;
        _jsonSplitter.IsVisible = false;
        _rootContent.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Pixel);
        UpdateToggleButtonVisuals();
    }

    private void ShowJsonTree()
    {
        var ed = GetActiveEditor();
        if (ed?.Document == null) return;
        if (!JsonTools.TryParse(ed.Document.Text, out var doc) || doc == null)
        {
            ShowMessage("JSON", "The current document is not valid JSON.");
            return;
        }

        using (doc)
        {
            var root = new TreeViewItem { Header = "root", IsExpanded = true };
            BuildJsonNode(doc.RootElement, root);
            _jsonTree.ItemsSource = new List<TreeViewItem> { root };
        }

        _jsonPanel.IsVisible = true;
        _jsonSplitter.IsVisible = true;
        _rootContent.ColumnDefinitions[0].Width = new GridLength(Width * 0.33, GridUnitType.Pixel);
    }

    private static void BuildJsonNode(JsonElement element, TreeViewItem parent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var node = new TreeViewItem { Header = prop.Name };
                    AddScalarOrRecurse(prop.Value, node);
                    AddChild(parent, node);
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var node = new TreeViewItem { Header = $"[{i++}]" };
                    AddScalarOrRecurse(item, node);
                    AddChild(parent, node);
                }
                break;
            default:
                parent.Header = $"{parent.Header} : {Scalar(element)}";
                break;
        }
    }

    private static void AddScalarOrRecurse(JsonElement value, TreeViewItem node)
    {
        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            BuildJsonNode(value, node);
        else
            node.Header = $"{node.Header} : {Scalar(value)}";
    }

    private static string Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => $"\"{e.GetString()}\"",
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => e.GetRawText(),
    };

    private static void AddChild(TreeViewItem parent, TreeViewItem child)
    {
        var items = parent.ItemsSource as List<TreeViewItem> ?? new List<TreeViewItem>();
        items.Add(child);
        parent.ItemsSource = items;
    }
}
