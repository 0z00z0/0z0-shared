using System.ComponentModel;

namespace ZeroZero.Win32;

/// <summary>
/// The four native message boxes: information, warning, error, and a yes-or-no question. Each is
/// modal to its owner, or to nothing when the owner is zero, and returns when dismissed. The
/// wording is the caller's.
/// </summary>
public static class NativeMessageBox
{
    private const uint MB_OK = 0x0000;
    private const uint MB_YESNO = 0x0004;
    private const uint MB_ICONERROR = 0x0010;
    private const uint MB_ICONQUESTION = 0x0020;
    private const uint MB_ICONWARNING = 0x0030;
    private const uint MB_ICONINFORMATION = 0x0040;
    private const uint MB_TOPMOST = 0x00040000;
    private const int IDYES = 6;

    /// <param name="owner">The window the box is modal to, or zero for none.</param>
    /// <param name="topmost">Keep the box above every other window — for a tray application with
    /// no window of its own to bring it forward.</param>
    /// <exception cref="Win32Exception">The box could not be shown — an owner handle that is not
    /// a window, for one. A message that never appeared is not silently a success.</exception>
    public static void Information(IntPtr owner, string caption, string text, bool topmost = false)
        => Show(owner, caption, text, MB_OK | MB_ICONINFORMATION, topmost);

    /// <inheritdoc cref="Information"/>
    public static void Warning(IntPtr owner, string caption, string text, bool topmost = false)
        => Show(owner, caption, text, MB_OK | MB_ICONWARNING, topmost);

    /// <inheritdoc cref="Information"/>
    public static void Error(IntPtr owner, string caption, string text, bool topmost = false)
        => Show(owner, caption, text, MB_OK | MB_ICONERROR, topmost);

    /// <summary>A yes-or-no question.</summary>
    /// <returns>True for Yes; false for No or for the box closed any other way.</returns>
    /// <inheritdoc cref="Information"/>
    public static bool Question(IntPtr owner, string caption, string text, bool topmost = false)
        => Show(owner, caption, text, MB_YESNO | MB_ICONQUESTION, topmost) == IDYES;

    private static int Show(IntPtr owner, string caption, string text, uint style, bool topmost)
    {
        if (topmost) style |= MB_TOPMOST;

        int result = NativeMethods.MessageBox(owner, text, caption, style);
        if (result == 0) throw new Win32Exception();

        return result;
    }
}
