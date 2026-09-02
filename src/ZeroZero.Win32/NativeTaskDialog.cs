using System.Runtime.InteropServices;

namespace ZeroZero.Win32;

/// <summary>
/// The native task dialog: caption, headline, body, an expandable detail and a row of buttons.
/// Modal to its owner and returns when a button is pressed. Needs common controls version 6,
/// which only the application's own manifest can declare — see <see cref="IsAvailable"/>.
/// </summary>
public static class NativeTaskDialog
{
    private static readonly Lazy<bool> Available = new(ProbeForTaskDialog);

    /// <summary>
    /// Whether this process can show a task dialog. False when the process loaded common controls
    /// without a manifest dependency on version 6, the only version that exports the dialog; a
    /// package cannot declare that dependency on an application's behalf.
    /// </summary>
    public static bool IsAvailable => Available.Value;

    /// <param name="owner">The window the dialog is modal to and centred on, or zero for none.</param>
    /// <returns>The id of the button pressed, or <see cref="TaskDialogButton.CancelId"/> when the
    /// dialog was closed with its title-bar cross or Escape.</returns>
    /// <exception cref="InvalidOperationException">The process has no common controls version 6;
    /// the message names the manifest dependency to add.</exception>
    /// <exception cref="COMException">The dialog refused the configuration.</exception>
    public static int Show(IntPtr owner, TaskDialogRequest request)
    {
        using var marshalling = new TaskDialogMarshalling(owner, request);

        int result;
        int pressed;
        try
        {
            result = NativeMethods.TaskDialogIndirect(in marshalling.Config, out pressed, IntPtr.Zero, IntPtr.Zero);
        }
        catch (EntryPointNotFoundException inner)
        {
            throw new InvalidOperationException(
                "The task dialog needs common controls version 6. Declare a dependency on " +
                "Microsoft.Windows.Common-Controls 6.0.0.0 in the application manifest; a package " +
                "cannot declare it for the application.", inner);
        }

        Marshal.ThrowExceptionForHR(result);
        return pressed;
    }

    private static bool ProbeForTaskDialog()
    {
        return NativeLibrary.TryLoad("comctl32.dll", out IntPtr library)
            && NativeLibrary.TryGetExport(library, "TaskDialogIndirect", out _);
    }
}
