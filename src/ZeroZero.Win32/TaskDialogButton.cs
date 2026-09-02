namespace ZeroZero.Win32;

/// <summary>
/// One button of a task dialog. <see cref="Id"/> is what <see cref="NativeTaskDialog.Show"/>
/// returns when the button is pressed, so ids are the caller's vocabulary; keep them clear of
/// <see cref="CancelId"/>. As a command link, a line break in <see cref="Text"/> separates the
/// title from the note beneath it.
/// </summary>
public sealed record TaskDialogButton(int Id, string Text)
{
    /// <summary>What the dialog reports when closed by its title-bar cross or Escape: IDCANCEL.</summary>
    public const int CancelId = 2;
}
