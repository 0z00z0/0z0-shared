using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroZero.Controls.WinUI;
using static ZeroZero.Brand.WinUI.TestHarness.HarnessArguments;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>The text prompt on screen, one per theme; with <c>--confirm</c> or <c>--cancel</c> one
/// prompt is answered through its own field and button and its result written to a file. Flags:
/// docs/test-harness.md.</summary>
internal static class PromptScenario
{
    public static async void Show(string? confirmWith, bool cancel)
    {
        static TextPromptOptions Options(ElementTheme theme, string title) => new()
        {
            Title = title,
            Message = "The name the device is announced under. Entities keep their ids; only the "
                    + "label shown for the device changes.",
            Confirm = "Rename",
            Note = "Applies on the next publish.",
            InitialText = "Harness (demo)",
            Placeholder = "A name for this device",
            MaxLength = 64,
            Theme = theme,
        };

        if (confirmWith is null && !cancel)
        {
            _ = TextPromptWindow.ShowAsync(Options(ElementTheme.Light, "Text Prompt Light"));
            _ = TextPromptWindow.ShowAsync(Options(ElementTheme.Dark, "Text Prompt Dark"));
            return;
        }

        var window = new TextPromptWindow(Options(ElementTheme.Dark, "Text Prompt Driven"));
        window.Activate();
        // The rig reaches into the realised tree, as it does for the panel: the field and the
        // buttons are the prompt's own and nothing an application would call belongs on it for a
        // rig's sake.
        await Task.Delay(1500);
        string resultPath = Path.Combine(Path.GetTempPath(), "text-prompt-result.txt");
        if (window.Content is DependencyObject root)
        {
            // --type "<text>" edits the field before either answer, so a cancel after an edit is
            // its own case and not only a cancel of the untouched, still-selected initial text.
            string? typed = confirmWith ?? ValueAfter(Environment.GetCommandLineArgs(), "--type");
            if (typed is not null && HarnessTree.Find<TextBox>(root) is { } field) field.Text = typed;
            string wanted = cancel ? "Cancel" : "Rename";
            if (!HarnessTree.Press(root, button => button.Content is string text && text == wanted))
            {
                // Written instead of the answer: the prompt would otherwise wait for a person.
                File.WriteAllText(resultPath, $"<miss: no button \"{wanted}\">");
                return;
            }
        }
        string? result = await window.Result;
        File.WriteAllText(resultPath, result ?? "<null>");
        // No Exit(): the prompt was the last window, so the rig is already shutting down, and
        // calling Exit() during the last-window shutdown crashes the process.
    }
}
