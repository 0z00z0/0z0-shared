using Xunit;

namespace ZeroZero.Win32.Tests;

/// <summary>
/// The test host carries no manifest dependency on common controls 6, so the dialog cannot be
/// shown from here — which is exactly the condition the layer must report clearly, and the one
/// path that can be exercised without a dialog on screen.
/// </summary>
public class NativeTaskDialogTests
{
    private static readonly TaskDialogRequest Request = new()
    {
        Caption = "caption",
        Headline = "headline",
        Buttons = [new TaskDialogButton(100, "one")],
    };

    [Fact]
    public void IsAvailable_IsFalseInAProcessWithoutACommonControls6Manifest()
    {
        Assert.False(NativeTaskDialog.IsAvailable);
    }

    [Fact]
    public void Show_NamesTheManifestDependencyWhenTheDialogIsUnavailable()
    {
        // An assertion rather than a return: in a host that did carry the manifest this test would
        // put a modal dialog on screen and hang the run, and that must be a red test instead.
        Assert.False(NativeTaskDialog.IsAvailable);

        var error = Assert.Throws<InvalidOperationException>(() => NativeTaskDialog.Show(IntPtr.Zero, Request));

        Assert.Contains("Microsoft.Windows.Common-Controls", error.Message);
        Assert.IsType<EntryPointNotFoundException>(error.InnerException);
    }
}
