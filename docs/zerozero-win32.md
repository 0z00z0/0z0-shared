# The Win32 foundation assembly

`ZeroZero.Win32` is the raw native layer: monitor, DPI and taskbar metrics as plain numbers, the native task
dialog and the four message boxes, and dark native chrome for the process. Plain `net10.0`, no
package references, no project references, no XAML and no Windows App SDK — which is what makes it
**foundation** rather than a component, and what lets a console tool take it as readily as a WinUI
application. The About window and the text prompt take their monitor metrics from here, the tray
assembly the taskbar's scale, and the update component the marshalling of its dialogs.

The assembly is versioned as `Win32Version` in `Versions.props` and released under `win32-v<x.y.z>`
tags, with notes under `docs/release-notes/win32/`; [`releasing.md`](releasing.md) has the
procedure. A component that references it can only release once the version it references is on
the feed, so a change here releases first.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows 10 1809 (build 10.0.17763) or later. The assembly declares itself Windows-only, so a call from code that may run elsewhere is a compiler warning. |
| Manifest | The task dialog needs common controls version 6, declared in the consuming application's own manifest (below). `PerMonitorV2` DPI awareness in the same manifest is what makes the monitor metrics agree with the scale the window is drawn at. |

## What it contains

- **`MonitorMetrics`** — the work area and scale of the monitor under the cursor (`ForCursor`), the
  primary monitor's work area (`PrimaryWorkArea`), the scale a window is drawn at
  (`ScaleForWindow`), the scale of the display the taskbar sits on (`ScaleForTaskbar`) — under
  per-monitor awareness the process's own scale follows its last window, which is not where a
  notification icon is drawn — and the pixels a frame adds around the client area
  (`NonClientSize`). Every
  answer is physical pixels or a plain factor; the caller decides which monitor gets which window
  and does the arithmetic. A call that fails yields something usable — the primary monitor at 100%,
  a 1080p work area, zero chrome — never an empty rectangle.
- **`NativeRect`** — a rectangle in physical pixels, with `ClampInto` for keeping a window inside a
  work area.
- **`NativeTaskDialog`** — `Show(owner, TaskDialogRequest)`: caption, headline, body, an expandable
  detail, a stock icon and the buttons — that general, and no more specific. Returns the id of the
  button pressed, or `TaskDialogButton.CancelId` when the dialog was closed. The wording is the
  caller's.
- **`NativeMessageBox`** — `Information`, `Warning`, `Error` and a yes-or-no `Question`, each modal
  to an owner or to nothing, each with a `topmost` option for a tray application that has no window
  to bring the box forward. A box that cannot be shown throws rather than returning as if it had.
- **`DarkChrome`** — `Apply(DarkChromeMode)` opts the process's native chrome, context menus above
  all, into the dark theme through two undocumented uxtheme entry points. Returns false on a Windows
  without them, where chrome stays light.

Not here, by design: which monitor a window goes on, the wording of any dialog, and the trust
verification the update flow carries. Each stays with the code that owns the decision.

## The manifest dependency

The task dialog is exported by common controls version 6 only, and which version a process loads is
decided by the executable's own manifest when `comctl32` loads. No library and no package can
declare it on an application's behalf. The consuming application's `app.manifest` carries:

```xml
<dependency>
  <dependentAssembly>
    <assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0"
                      processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*" />
  </dependentAssembly>
</dependency>
```

Without it `NativeTaskDialog.IsAvailable` is false and `Show` throws an
`InvalidOperationException` naming the dependency; a caller that may run without it falls back to a
message box. The build kit's manifest template declares it, so the harness, which builds under the
kit's application block, shows the dialog under `--native`.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Win32` itself; there is
nothing beneath it. An application taking the brand component, the controls assembly or the tray
assembly has it transitively and adds nothing.

The tests are in `tests/ZeroZero.Win32.Tests`, plain `net10.0`, and run on Windows only: they call
user32 and shcore against the real desktop, create a hidden framed window to measure, and read the
packed task-dialog configuration back through its pointers. No test shows the dialog or a message
box — a modal dialog would block the run — so those are looked at through the harness instead.
