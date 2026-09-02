using Microsoft.UI.Xaml;

namespace ZeroZero.Controls.WinUI;

/// <summary>
/// Everything a text prompt shows and accepts. The wording is the caller's throughout: the
/// prompt carries text and owns none, so the confirm button says what the answer does
/// ("Rename", "Connect") rather than a generic "OK" in every instance.
/// </summary>
public sealed class TextPromptOptions
{
    /// <summary>The heading, one line.</summary>
    public required string Title { get; init; }

    /// <summary>What is being asked, and why. Wraps.</summary>
    public required string Message { get; init; }

    /// <summary>The wording on the confirm button, which sits on the right.</summary>
    public required string Confirm { get; init; }

    /// <summary>The wording on the cancel button. "Cancel" unless the caller has a reason.</summary>
    public string Cancel { get; init; } = "Cancel";

    /// <summary>An extra line beneath the field — a consequence, a format hint. None by default.</summary>
    public string? Note { get; init; }

    /// <summary>What the field holds when the prompt opens, selected so typing replaces it.</summary>
    public string InitialText { get; init; } = "";

    /// <summary>Shown in the empty field.</summary>
    public string Placeholder { get; init; } = "";

    /// <summary>The most characters the field accepts; zero for no limit.</summary>
    public int MaxLength { get; init; }

    /// <summary>Whether an empty or whitespace-only answer can be confirmed. Off, so the confirm
    /// button waits for text; a caller that wants "empty means default" turns it on.</summary>
    public bool AllowEmpty { get; init; }

    /// <summary>The theme the prompt renders in. Default follows the application; an application
    /// pinned to one theme passes it here, as it does for its title bars.</summary>
    public ElementTheme Theme { get; init; } = ElementTheme.Default;
}
