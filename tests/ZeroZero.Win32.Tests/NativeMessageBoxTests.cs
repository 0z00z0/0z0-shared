using System.ComponentModel;
using Xunit;

namespace ZeroZero.Win32.Tests;

/// <summary>
/// A message box that appears blocks the run, so the one path exercised against the real user32
/// is the one that fails before anything is shown. It proves the import resolves and that a
/// failure is reported rather than swallowed; the icons and the topmost flag cannot be seen from
/// here.
/// </summary>
public class NativeMessageBoxTests
{
    private const int ERROR_INVALID_WINDOW_HANDLE = 1400;

    // No window has ever had this handle in a process this size; user32 refuses it.
    private static readonly IntPtr NotAWindow = new(0x7FFF_FFF0);

    [Fact]
    public void Information_ThrowsForAnOwnerThatIsNotAWindow()
    {
        var error = Assert.Throws<Win32Exception>(() => NativeMessageBox.Information(NotAWindow, "caption", "text"));

        Assert.Equal(ERROR_INVALID_WINDOW_HANDLE, error.NativeErrorCode);
    }

    [Fact]
    public void Question_ThrowsForAnOwnerThatIsNotAWindow()
    {
        var error = Assert.Throws<Win32Exception>(() => NativeMessageBox.Question(NotAWindow, "caption", "text"));

        Assert.Equal(ERROR_INVALID_WINDOW_HANDLE, error.NativeErrorCode);
    }
}
