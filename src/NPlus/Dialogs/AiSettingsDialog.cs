using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NPlus.Ai;
using NPlus.Core;

namespace NPlus.Dialogs;

/// <summary>
/// Settings for the optional AI assistant: an enable toggle, a provider picker, the per-provider
/// connection fields (key / endpoint / model / api-version), and a "Test connection" button that
/// does a tiny live round-trip. Mutates the passed <see cref="AiSettings"/> in place and returns
/// true when the user saves.
/// </summary>
public static class AiSettingsDialog
{
    public static async Task<bool> Show(Window owner, AiSettings settings)
    {
        bool saved = false;

        var providers = AiProviders.All.ToList();
        var combo = new ComboBox { ItemsSource = providers.Select(p => p.DisplayName).ToList(), MinWidth = 260 };
        combo.SelectedIndex = Math.Max(0, providers.FindIndex(p => p.Provider.ToString() == settings.Provider));
        int prevIndex = combo.SelectedIndex;
        AiProvider Current() => providers[Math.Max(0, combo.SelectedIndex)].Provider;

        var enable = new CheckBox { Content = "Enable AI features (selection actions + chat panel)", IsChecked = settings.Enabled };

        var keyBox = new TextBox { PasswordChar = '•', Width = 360 };
        var reveal = new CheckBox { Content = "Show", VerticalAlignment = VerticalAlignment.Center };
        reveal.IsCheckedChanged += (_, _) => keyBox.RevealPassword = reveal.IsChecked == true;
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { keyBox, reveal } };

        var endpointBox = new TextBox { Width = 360 };
        var endpointHint = new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        var modelBox = new TextBox { Width = 360 };
        var apiVersionBox = new TextBox { Width = 360 };

        var keyLabel = new TextBlock { Text = "API key" };
        var modelLabel = new TextBlock { Text = "Model" };
        var apiVersionLabel = new TextBlock { Text = "API version" };

        TextBlock Lbl(string t) => new() { Text = t, Margin = new Thickness(0, 6, 0, 0), FontWeight = FontWeight.SemiBold };

        // ---- per-provider field load/flush ----
        void Flush(AiProvider p)
        {
            var c = settings.ConfigFor(p.ToString());
            c.ApiKey = keyBox.Text ?? "";
            c.Endpoint = endpointBox.Text ?? "";
            c.Model = modelBox.Text ?? "";
            c.ApiVersion = apiVersionBox.Text ?? "";
        }

        void Load(AiProvider p)
        {
            var info = AiProviders.Info(p);
            var c = settings.ConfigFor(p.ToString());
            keyBox.Text = c.ApiKey;
            endpointBox.Text = c.Endpoint;
            modelBox.Text = c.Model;
            apiVersionBox.Text = c.ApiVersion;

            endpointBox.PlaceholderText = string.IsNullOrEmpty(info.DefaultEndpoint) ? "(required)" : info.DefaultEndpoint;
            modelBox.PlaceholderText = string.IsNullOrEmpty(info.DefaultModel) ? "(required)" : info.DefaultModel;
            apiVersionBox.PlaceholderText = info.DefaultApiVersion;
            endpointHint.Text = info.EndpointHint;
            endpointHint.IsVisible = !string.IsNullOrEmpty(info.EndpointHint);

            // Toggle visibility/labels by provider capabilities.
            keyLabel.IsVisible = keyRow.IsVisible = info.NeedsApiKey;
            modelLabel.Text = info.ModelIsDeployment ? "Deployment name" : "Model";
            apiVersionLabel.IsVisible = apiVersionBox.IsVisible = info.NeedsApiVersion;
        }

        Load(Current());

        combo.SelectionChanged += (_, _) =>
        {
            // Persist the fields for the provider that WAS selected, then load the new one.
            if (prevIndex >= 0 && prevIndex < providers.Count) Flush(providers[prevIndex].Provider);
            prevIndex = combo.SelectedIndex;
            Load(Current());
        };

        // ---- Test connection ----
        var testBtn = new Button { Content = "Test connection", MinWidth = 130 };
        var testStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center };
        CancellationTokenSource? testCts = null;
        testBtn.Click += async (_, _) =>
        {
            Flush(Current());
            var provider = Current();
            var info = AiProviders.Info(provider);
            var cfg = settings.ConfigFor(provider.ToString());
            if (info.NeedsApiKey && string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                testStatus.Foreground = Brushes.IndianRed;
                testStatus.Text = "Enter an API key first.";
                return;
            }

            testBtn.IsEnabled = false;
            testStatus.Foreground = _neutral;
            testStatus.Text = "Testing…";
            testCts?.Cancel();
            testCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var client = new AiClient(provider, cfg);
                var (ok, message) = await client.TestConnectionAsync(testCts.Token);
                testStatus.Foreground = ok ? Brushes.SeaGreen : Brushes.IndianRed;
                testStatus.Text = (ok ? "✓ " : "✗ ") + message;
            }
            catch (Exception ex)
            {
                testStatus.Foreground = Brushes.IndianRed;
                testStatus.Text = "✗ " + ex.Message;
            }
            finally { testBtn.IsEnabled = true; }
        };

        // ---- buttons ----
        var save = new Button { Content = "Save", MinWidth = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };

        var dialog = new Window
        {
            Title = "AI Settings",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        save.Click += (_, _) =>
        {
            Flush(Current());
            settings.Enabled = enable.IsChecked == true;
            settings.Provider = Current().ToString();
            saved = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        var fields = new StackPanel { Spacing = 2, Children =
        {
            enable,
            Lbl("Provider"), combo,
            keyLabel, keyRow,
            Lbl("Endpoint"), endpointBox, endpointHint,
            modelLabel, modelBox,
            apiVersionLabel, apiVersionBox,
            new TextBlock { Height = 6 },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { testBtn, testStatus } },
        }};
        // keep the dynamic labels in the tree (visibility toggled in Load)
        keyLabel.Margin = modelLabel.Margin = apiVersionLabel.Margin = new Thickness(0, 6, 0, 0);
        keyLabel.FontWeight = modelLabel.FontWeight = apiVersionLabel.FontWeight = FontWeight.SemiBold;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { save, cancel } };
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 12, Width = 460, Children = { fields, buttons } };

        await dialog.ShowDialog(owner);
        testCts?.Cancel();
        return saved;
    }

    private static readonly IBrush _neutral = new SolidColorBrush(Color.FromRgb(120, 120, 120));
}
