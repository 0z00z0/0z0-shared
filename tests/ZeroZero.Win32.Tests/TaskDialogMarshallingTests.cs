using System.Runtime.InteropServices;
using Xunit;

namespace ZeroZero.Win32.Tests;

/// <summary>
/// The packed configuration, read back through the pointers it holds. This is the layer the
/// dialog reads while it is on screen, and the one place a marshalling mistake is visible without
/// showing anything.
/// </summary>
public class TaskDialogMarshallingTests
{
    private static readonly TaskDialogRequest Request = new()
    {
        Caption = "caption",
        Headline = "headline",
        Body = "body",
        Detail = "detail",
        Buttons = [new TaskDialogButton(100, "first"), new TaskDialogButton(101, "second")],
    };

    private const uint TDF_ALLOW_DIALOG_CANCELLATION = 0x0008;
    private const uint TDF_USE_COMMAND_LINKS = 0x0010;
    private const uint TDF_POSITION_RELATIVE_TO_WINDOW = 0x1000;

    [Fact]
    public void PackedSizes_MatchTheHeaderForThisArchitecture()
    {
        // commctrl.h under pack(1): 160 and 12 bytes on 64-bit, 96 and 8 on 32-bit. An aligned
        // layout would be 168 and 16, and the dialog would refuse the configuration.
        bool sixtyFour = IntPtr.Size == 8;

        Assert.Equal(sixtyFour ? 160 : 96, Marshal.SizeOf<NativeMethods.TASKDIALOGCONFIG>());
        Assert.Equal(sixtyFour ? 12 : 8, Marshal.SizeOf<NativeMethods.TASKDIALOG_BUTTON>());
    }

    [Fact]
    public void CbSize_IsThePackedSize()
    {
        using var marshalling = new TaskDialogMarshalling(IntPtr.Zero, Request);

        Assert.Equal((uint)Marshal.SizeOf<NativeMethods.TASKDIALOGCONFIG>(), marshalling.Config.cbSize);
    }

    [Fact]
    public void Caption_Headline_Body_And_Detail_ReachTheNativeStrings()
    {
        using var marshalling = new TaskDialogMarshalling(IntPtr.Zero, Request);
        var config = marshalling.Config;

        Assert.Equal("caption", Marshal.PtrToStringUni(config.pszWindowTitle));
        Assert.Equal("headline", Marshal.PtrToStringUni(config.pszMainInstruction));
        Assert.Equal("body", Marshal.PtrToStringUni(config.pszContent));
        Assert.Equal("detail", Marshal.PtrToStringUni(config.pszExpandedInformation));
    }

    [Fact]
    public void AbsentBodyAndDetail_MarshalAsNullPointers()
    {
        using var marshalling = new TaskDialogMarshalling(IntPtr.Zero, Request with { Body = null, Detail = null });

        Assert.Equal(IntPtr.Zero, marshalling.Config.pszContent);
        Assert.Equal(IntPtr.Zero, marshalling.Config.pszExpandedInformation);
    }

    [Fact]
    public void Buttons_MarshalInOrderWithTheirIdsAndText()
    {
        using var marshalling = new TaskDialogMarshalling(IntPtr.Zero, Request);
        var config = marshalling.Config;

        Assert.Equal(2u, config.cButtons);
        var first = ReadButton(config.pButtons, 0);
        var second = ReadButton(config.pButtons, 1);
        Assert.Equal(100, first.nButtonID);
        Assert.Equal("first", Marshal.PtrToStringUni(first.pszButtonText));
        Assert.Equal(101, second.nButtonID);
        Assert.Equal("second", Marshal.PtrToStringUni(second.pszButtonText));
    }

    [Fact]
    public void DefaultButton_IsTheFirstUnlessNamed()
    {
        using var unnamed = new TaskDialogMarshalling(IntPtr.Zero, Request);
        using var named = new TaskDialogMarshalling(IntPtr.Zero, Request with { DefaultButtonId = 101 });

        Assert.Equal(100, unnamed.Config.nDefaultButton);
        Assert.Equal(101, named.Config.nDefaultButton);
    }

    [Theory]
    [InlineData(true, false, TDF_ALLOW_DIALOG_CANCELLATION)]
    [InlineData(false, true, TDF_USE_COMMAND_LINKS)]
    [InlineData(true, true, TDF_ALLOW_DIALOG_CANCELLATION | TDF_USE_COMMAND_LINKS)]
    [InlineData(false, false, 0u)]
    public void AllowCancel_And_CommandLinks_SetTheirFlagsAndNothingElse(bool allowCancel, bool commandLinks, uint expected)
    {
        using var marshalling = new TaskDialogMarshalling(IntPtr.Zero, Request with { AllowCancel = allowCancel, CommandLinks = commandLinks });

        Assert.Equal(expected, marshalling.Config.dwFlags);
    }

    [Fact]
    public void AnOwner_PositionsTheDialogRelativeToItAndIsTheParent()
    {
        var owner = new IntPtr(0x1234);
        using var owned = new TaskDialogMarshalling(owner, Request);
        using var unowned = new TaskDialogMarshalling(IntPtr.Zero, Request);

        Assert.Equal(owner, owned.Config.hwndParent);
        Assert.Equal(TDF_POSITION_RELATIVE_TO_WINDOW, owned.Config.dwFlags & TDF_POSITION_RELATIVE_TO_WINDOW);
        Assert.Equal(0u, unowned.Config.dwFlags & TDF_POSITION_RELATIVE_TO_WINDOW);
    }

    [Theory]
    [InlineData(TaskDialogIcon.None, 0)]
    [InlineData(TaskDialogIcon.Warning, 0xFFFF)]
    [InlineData(TaskDialogIcon.Error, 0xFFFE)]
    [InlineData(TaskDialogIcon.Information, 0xFFFD)]
    [InlineData(TaskDialogIcon.Shield, 0xFFFC)]
    public void Icon_MarshalsAsTheStockResourceId(TaskDialogIcon icon, int resource)
    {
        using var marshalling = new TaskDialogMarshalling(IntPtr.Zero, Request with { Icon = icon });

        Assert.Equal(new IntPtr(resource), marshalling.Config.mainIcon);
    }

    [Fact]
    public void NoButtons_IsRefused()
    {
        var request = Request with { Buttons = [] };

        Assert.Throws<ArgumentException>(() => new TaskDialogMarshalling(IntPtr.Zero, request));
    }

    private static NativeMethods.TASKDIALOG_BUTTON ReadButton(IntPtr array, int index)
        => Marshal.PtrToStructure<NativeMethods.TASKDIALOG_BUTTON>(array + index * Marshal.SizeOf<NativeMethods.TASKDIALOG_BUTTON>());
}
