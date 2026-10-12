using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZeroZero.Mqtt.WinUI;

/// <summary>The device id's own confirmation dialogue. The id is the <c>unique_id</c> stem, the
/// device identifier and every topic segment, so changing it renames every published entity.</summary>
internal static class DeviceIdDialog
{
    /// <summary>Asks for a new id, gated on a valid different id and an acknowledgement. Returns the
    /// id to store, or null when the dialogue was dismissed.</summary>
    /// <param name="resultStyle">The panel's result tier, passed in so no host key can restyle it.</param>
    public static async Task<string?> AskAsync(
        FrameworkElement owner, MqttPanelSetup setup, MqttStrings strings, string current,
        Style? resultStyle, Style? errorStyle)
    {
        var idBox = new TextBox
        {
            Text = setup.Settings.Read().DeviceId,
            PlaceholderText = MqttIdentity.Default(setup.TopicRoot, Environment.MachineName),
        };
        var errorText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Style = errorStyle,
        };
        // Echoes the sanitised id that will be published.
        var previewText = new TextBlock { Style = resultStyle };
        // Names the mechanism, not a consequence; a host adds its own through DeviceIdConsequence.
        var ack = new CheckBox { Content = strings.Get("DeviceIdAcknowledge") };

        var body = new StackPanel { Spacing = 8, Width = 420 };
        body.Children.Add(new TextBlock
        {
            Text = strings.Format("DeviceIdCurrent", current),
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock { Text = strings.Get("DeviceIdNew"), Style = resultStyle });
        body.Children.Add(idBox);
        body.Children.Add(previewText);
        body.Children.Add(errorText);
        body.Children.Add(new TextBlock
        {
            Text = strings.Get("DeviceIdWarning"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        if (setup.DeviceIdConsequence is { Length: > 0 } consequence)
            body.Children.Add(new TextBlock { Text = consequence, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(ack);

        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            // Rooted beside the page, so it does not inherit a RequestedTheme set on the panel's subtree.
            RequestedTheme = owner.ActualTheme,
            Title = strings.Get("DeviceIdTitle"),
            Content = body,
            PrimaryButtonText = strings.Get("DeviceIdConfirm"),
            CloseButtonText = strings.Get("DeviceIdCancel"),
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
        };

        // Re-derived from scratch on every edit, so no "was valid" flag can go stale.
        void Revalidate()
        {
            string raw = idBox.Text ?? "";
            string? error = MqttIdentity.Validate(raw, strings);
            string candidate = MqttIdentity.Effective(raw, setup.TopicRoot, Environment.MachineName);

            errorText.Text = error ?? "";
            errorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
            previewText.Text = error is null ? strings.Format("DeviceIdPreview", candidate) : "";
            dialog.IsPrimaryButtonEnabled = CanConfirm(error, candidate, current, ack.IsChecked == true);
        }

        idBox.TextChanged += (_, _) => Revalidate();
        ack.Checked += (_, _) => Revalidate();
        ack.Unchecked += (_, _) => Revalidate();
        Revalidate();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        // The sanitised form, so the card's effective id and the store agree; blank is the default sentinel.
        string entered = (idBox.Text ?? "").Trim();
        return entered.Length == 0 ? "" : MqttIdentity.Normalise(entered);
    }

    /// <summary>Whether the confirm button is live. Pure.</summary>
    internal static bool CanConfirm(string? error, string candidate, string current, bool acknowledged) =>
        error is null && candidate != current && acknowledged;
}
