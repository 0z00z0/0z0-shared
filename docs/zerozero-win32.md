# The Win32 foundation assembly

`ZeroZero.Win32` is the raw native layer: monitor, DPI and taskbar metrics as plain numbers, the
arithmetic that fits a window into a work area, the native task
dialog and the four message boxes, and dark native chrome for the process. Plain `net10.0`, no
package references, no project references, no XAML and no Windows App SDK — which is what makes it
**foundation** rather than a component, and what lets a console tool take it as readily as a WinUI
application. The About window, the text prompt, the settings shell and the update window take their
monitor metrics from here, the tray assembly the taskbar's scale, and every window that dismisses
itself on focus loss the count of transient windows that tells it not to.

The assembly is versioned as `Win32Version` in `Versions.props` and released under `win32-v<x.y.z>`
tags, with notes under `docs/release-notes/win32/`; [`releasing.md`](releasing.md) has the
procedure. A component that references it can only release once the version it references is on
the feed, so a change here releases first.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows. The assembly targets plain `net10.0` and declares itself Windows-only through `SupportedOSPlatform`, with no version: nothing here needs a build floor, and the project states none. A call from code that may run elsewhere is a compiler warning. An application taking it alongside the WinUI components inherits their floor, not one from here. |
| Manifest | The task dialog needs common controls version 6, declared in the consuming application's own manifest (below). `PerMonitorV2` DPI awareness in the same manifest is what makes the monitor metrics agree with the scale the window is drawn at. |

## What it contains

- **`MonitorMetrics`** — the work area and scale of the monitor under the cursor (`ForCursor`), of
  the monitor any given point falls on (`ForPoint`, answering for the nearest monitor when the point
  is on none, which is how a window remembered on a screen since unplugged still opens somewhere
  reachable), the primary monitor's work area (`PrimaryWorkArea`), the scale a window is drawn at
  (`ScaleForWindow`), the scale of the display the taskbar sits on (`ScaleForTaskbar`) — under
  per-monitor awareness the process's own scale follows its last window, which is not where a
  notification icon is drawn — and the pixels a frame adds around the client area
  (`NonClientSize`). Every
  answer is physical pixels or a plain factor; the caller decides which monitor gets which window
  and does the arithmetic. A call that fails yields something usable — the primary monitor at 100%,
  a 1080p work area, zero chrome — never an empty rectangle.
- **`NativeRect`** — a rectangle in physical pixels, with `ClampInto` for keeping a window inside a
  work area.
- **`WindowFit`** — the arithmetic over those numbers, so a caller on any user-interface framework
  measures and this places. `Fit` takes the rectangle a window wants, the window height its content
  needs and the work area, and answers with the rectangle to open at: grown where the content is
  taller, cut down to the work area, and moved inside it — except that a rectangle sharing no pixel
  with the work area is re-centred rather than clamped, because clamping alone jams a window
  restored onto a monitor that has gone into the nearest corner. `HeightForContent` turns a
  measured content height into a window height by the difference between what the window is and
  what it shows today, so the title bar and the scroller's padding are carried rather than added up
  by hand, and it may shrink as well as grow. `ContentFittedHeight` is the popup case: content at
  the window's scale plus its frame, floored at a minimum and capped at `HeightCap`, a share of the
  work area — `DefaultHeightFraction`, four fifths — past which the scroller takes over.
  `ToPhysicalPixels` converts at a scale, treating anything at or below zero as 100 %.
- **`NativeTaskDialog`** — `Show(owner, TaskDialogRequest)`: caption, headline, body, an expandable
  detail, a stock icon and the buttons — that general, and no more specific. `StockCancelButton`
  adds the system's own Cancel in the user's display language, rather than a custom button spelling
  it; `SizeToContent` widens the dialog to fit its longest line instead of wrapping at the standard
  width. Returns the id of the button pressed, or `TaskDialogButton.CancelId` when the dialog was
  closed. The wording is the caller's, and so is any fallback: `Show` throws where the dialog cannot
  appear, and `IsAvailable` is the branch to take before building the request.
- **`NativeMessageBox`** — `Information`, `Warning`, `Error` and a yes-or-no `Question`, each modal
  to an owner or to nothing, each with a `topmost` option for a tray application that has no window
  to bring the box forward. A box that cannot be shown throws rather than returning as if it had.
- **`DarkChrome`** — `Apply(DarkChromeMode)` opts the process's native chrome, context menus above
  all, into the dark theme through two undocumented uxtheme entry points. Returns false on a Windows
  without them, where chrome stays light.
- **`TransientWindows`** — how many of the application's own short-lived windows are on screen.
  `Enter()` counts one until the scope is disposed, and `AnyOpen` is the question a window that
  dismisses itself on focus loss asks before it does: one of its own application's windows taking
  focus is not the reader looking elsewhere. Without it, opening a window on top of a
  self-dismissing one closes the window beneath in the same gesture. A window deactivated while a
  transient was open is not re-examined when the last one closes, so it stays open until the reader
  looks away again — a window outstaying its welcome by one glance beats one vanishing mid-update.
  The About window and the update window are the two that use it today.

Not here, by design: which monitor a window goes on, what a window's content measures, the wording
of any dialog, and the trust verification the update flow carries. Each stays with the code that
owns the decision — `WindowFit` is told the work area and the heights, and never reads them.

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
nothing beneath it. An application taking the brand component, the controls assembly, the tray
component, the update component's Win32 entry point, the MQTT settings panel or the settings shell
already has it and adds nothing. Which of those take it directly is in
[`consume-win32-foundation.md`](consume-win32-foundation.md).

The tests are in `tests/ZeroZero.Win32.Tests`, plain `net10.0`, and run on Windows only: they call
user32 and shcore against the real desktop, create a hidden framed window to measure, and read the
packed task-dialog configuration back through its pointers. `WindowFitTests` needs none of that —
the arithmetic takes numbers and answers with numbers. No test shows the dialog or a message
box — a modal dialog would block the run — so those are looked at through the harness instead.

`MonitorMetricsTests.ForCursor_ReportsTheMonitorUnderTheCursorAndItsScale` fails without an
interactive desktop — a locked screen, a service session, a headless run — because it reads the
cursor twice: once through `MonitorMetrics.ForCursor`, once through the test's own independent
`GetCursorPos` import, then compares the two. `GetCursorPos` only succeeds when the calling
thread's current desktop is the input desktop; off an interactive session it returns false, and the
test's own read throws before the comparison runs, failing the test with an exception rather than
skipping it. `MonitorMetrics.ForCursor` itself never throws — the same failure there falls back to
the primary monitor at 100% — so the fault sits in the test's own probe, not in the assembly it is
testing. Expected in that state; no action needed beyond running the suite on an interactive
desktop.
