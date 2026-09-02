namespace ZeroZero.Win32;

/// <summary>
/// What a task dialog shows. The wording is the caller's; this type only carries it across.
/// </summary>
public sealed record TaskDialogRequest
{
    /// <summary>The title bar.</summary>
    public required string Caption { get; init; }

    /// <summary>The large heading beside the icon.</summary>
    public required string Headline { get; init; }

    /// <summary>The paragraph under the headline.</summary>
    public string? Body { get; init; }

    /// <summary>Text behind a "More details" toggle at the foot of the dialog, collapsed at first.</summary>
    public string? Detail { get; init; }

    /// <summary>The buttons, in order; at least one.</summary>
    public required IReadOnlyList<TaskDialogButton> Buttons { get; init; }

    public TaskDialogIcon Icon { get; init; } = TaskDialogIcon.None;

    /// <summary>The button Enter presses; the first button when unset.</summary>
    public int? DefaultButtonId { get; init; }

    /// <summary>Render the buttons as command links — the tall, left-aligned form with a note under each title.</summary>
    public bool CommandLinks { get; init; }

    /// <summary>Let the title-bar cross and Escape close the dialog, reporting <see cref="TaskDialogButton.CancelId"/>.</summary>
    public bool AllowCancel { get; init; } = true;
}
