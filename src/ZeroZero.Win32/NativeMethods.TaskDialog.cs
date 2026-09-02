using System.Runtime.InteropServices;

namespace ZeroZero.Win32;

internal static partial class NativeMethods
{
    internal const uint TDF_ALLOW_DIALOG_CANCELLATION = 0x0008;
    internal const uint TDF_USE_COMMAND_LINKS = 0x0010;
    internal const uint TDF_POSITION_RELATIVE_TO_WINDOW = 0x1000;

    // MAKEINTRESOURCEW of -1 to -4: the stock icons, as the low word of a pointer.
    internal const int TD_WARNING_ICON = 0xFFFF;
    internal const int TD_ERROR_ICON = 0xFFFE;
    internal const int TD_INFORMATION_ICON = 0xFFFD;
    internal const int TD_SHIELD_ICON = 0xFFFC;

    // commctrl.h declares both structures under pack(1) on every architecture. Without the same
    // packing here the 64-bit layout gains alignment padding, cbSize disagrees with what the
    // dialog expects, and TaskDialogIndirect fails with E_INVALIDARG before showing anything.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct TASKDIALOG_BUTTON
    {
        public int nButtonID;
        public IntPtr pszButtonText;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public IntPtr hwndParent;
        public IntPtr hInstance;
        public uint dwFlags;
        public uint dwCommonButtons;
        public IntPtr pszWindowTitle;
        // A union of HICON and PCWSTR; the stock icons travel as the resource-id form.
        public IntPtr mainIcon;
        public IntPtr pszMainInstruction;
        public IntPtr pszContent;
        public uint cButtons;
        public IntPtr pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public IntPtr pRadioButtons;
        public int nDefaultRadioButton;
        public IntPtr pszVerificationText;
        public IntPtr pszExpandedInformation;
        public IntPtr pszExpandedControlText;
        public IntPtr pszCollapsedControlText;
        public IntPtr footerIcon;
        public IntPtr pszFooter;
        public IntPtr pfCallback;
        public IntPtr lpCallbackData;
        public uint cxWidth;
    }

    // Exported by common controls 6 only: a process whose manifest declares no dependency on
    // version 6 loads a comctl32 without the entry point, and the call throws
    // EntryPointNotFoundException.
    [LibraryImport("comctl32.dll")]
    internal static partial int TaskDialogIndirect(in TASKDIALOGCONFIG config, out int button, IntPtr radioButton, IntPtr verificationChecked);
}
