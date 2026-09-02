using System.Runtime.InteropServices;

namespace ZeroZero.Win32;

/// <summary>
/// The byte-packed configuration a task dialog is shown from, and the unmanaged strings and button
/// array it points at. Alive from construction until disposed, which must be after the dialog has
/// closed: the dialog reads the memory while it is on screen.
/// </summary>
internal sealed class TaskDialogMarshalling : IDisposable
{
    private readonly List<IntPtr> _allocations = [];

    internal NativeMethods.TASKDIALOGCONFIG Config;

    internal TaskDialogMarshalling(IntPtr owner, TaskDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Buttons.Count == 0)
            throw new ArgumentException("A task dialog needs at least one button.", nameof(request));

        uint flags = 0;
        if (request.AllowCancel) flags |= NativeMethods.TDF_ALLOW_DIALOG_CANCELLATION;
        if (request.CommandLinks) flags |= NativeMethods.TDF_USE_COMMAND_LINKS;
        // Centred on the owner rather than on the monitor, where there is one.
        if (owner != IntPtr.Zero) flags |= NativeMethods.TDF_POSITION_RELATIVE_TO_WINDOW;

        Config = new NativeMethods.TASKDIALOGCONFIG
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.TASKDIALOGCONFIG>(),
            hwndParent = owner,
            dwFlags = flags,
            pszWindowTitle = Allocate(request.Caption),
            mainIcon = IconResource(request.Icon),
            pszMainInstruction = Allocate(request.Headline),
            pszContent = Allocate(request.Body),
            cButtons = (uint)request.Buttons.Count,
            pButtons = AllocateButtons(request.Buttons),
            nDefaultButton = request.DefaultButtonId ?? request.Buttons[0].Id,
            pszExpandedInformation = Allocate(request.Detail),
        };
    }

    public void Dispose()
    {
        foreach (IntPtr allocation in _allocations) Marshal.FreeHGlobal(allocation);
        _allocations.Clear();
    }

    private IntPtr Allocate(string? text)
    {
        if (text is null) return IntPtr.Zero;

        IntPtr pointer = Marshal.StringToHGlobalUni(text);
        _allocations.Add(pointer);
        return pointer;
    }

    private IntPtr AllocateButtons(IReadOnlyList<TaskDialogButton> buttons)
    {
        int stride = Marshal.SizeOf<NativeMethods.TASKDIALOG_BUTTON>();
        IntPtr array = Marshal.AllocHGlobal(stride * buttons.Count);
        _allocations.Add(array);

        for (int i = 0; i < buttons.Count; i++)
        {
            var button = new NativeMethods.TASKDIALOG_BUTTON
            {
                nButtonID = buttons[i].Id,
                pszButtonText = Allocate(buttons[i].Text),
            };
            Marshal.StructureToPtr(button, array + i * stride, fDeleteOld: false);
        }

        return array;
    }

    private static IntPtr IconResource(TaskDialogIcon icon) => icon switch
    {
        TaskDialogIcon.Information => NativeMethods.TD_INFORMATION_ICON,
        TaskDialogIcon.Warning => NativeMethods.TD_WARNING_ICON,
        TaskDialogIcon.Error => NativeMethods.TD_ERROR_ICON,
        TaskDialogIcon.Shield => NativeMethods.TD_SHIELD_ICON,
        _ => IntPtr.Zero,
    };
}
