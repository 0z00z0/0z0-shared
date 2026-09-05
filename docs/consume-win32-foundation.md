# Replacing an application's own native helper

A checklist for an application that already has a helper of its own doing this work — the same
preferred-app-mode calls, the same monitor and DPI numbers, a message box and a task dialog — and
wants to delete it. Nothing new appears on screen: the point is that behaviour survives the swap.
The surface is in [`zerozero-win32.md`](zerozero-win32.md) and the reference routes are in
[`consuming.md`](consuming.md); neither is repeated here.

## What it does not do

Read this first. Whatever an existing helper does from this list stays with the application.

- **Dark chrome is two calls and nothing more:** the process-wide preferred app mode, then a flush
  so menus already created drop their old theme. Nothing reads the system theme, nothing refreshes
  the immersive colour policy, and nothing themes an individual window. A helper doing any of those
  keeps that code and loses only the two calls.
- **Monitor metrics answer for the cursor's monitor and the primary monitor.** There is no work area
  for the monitor a given window sits on, and no enumeration of monitors. A helper that places a
  window on the display another window occupies keeps that lookup.
- **The message boxes are four fixed shapes** — information, warning, error, and a yes-or-no
  question — each with an owner and a topmost option. No custom button wording, no choice of default
  button, no timeout.
- **The task dialog carries caption, headline, body, an expandable detail, a stock icon and
  buttons.** No progress bar, no verification checkbox, no radio buttons, no footer, no hyperlinks,
  and no callback while it is on screen.
- **Which monitor a window goes on, and what any dialog says, stay with the caller.**

## This and the build kit are one piece of work

The task dialog exists in common controls version 6 only, and the monitor numbers agree with the
scale a window is drawn at only under per-monitor-v2 awareness. Both are declared in the
executable's own manifest, which no library can write on its behalf, and the build kit's template
declares both — see [`consume-build-kit.md`](consume-build-kit.md). Taking this assembly without the
kit means keeping or hand-writing that manifest. Take the kit first, then this.

## The checklist

1. Reference `ZeroZero.Win32`, and only where nothing else already brought it. The brand component,
   the controls assembly, the tray component and the update component's Win32 entry point take it
   directly; the MQTT settings panel and the settings shell take it through the controls assembly.
   An application already on any of those six has it.
2. Compare the helper's dark-chrome path against `DarkChrome.Apply(mode)` before deleting anything,
   and keep what the list above says is not covered. `Apply` answers false on a Windows before
   10.0.18362, where native chrome stays light and nothing else changes.
3. Swap the metric calls one at a time, and read the answers back on a machine with two monitors at
   different scaling. Every failure path here yields a usable value — the primary monitor, 100 %, a
   1080p work area, zero chrome — rather than throwing, so a wrong monitor looks exactly like a
   working call.
4. Swap the message boxes. A box that cannot be shown throws rather than returning as though it had
   appeared; a helper that answered with a value on failure changes behaviour at that point, and the
   throw is the reason to make the swap.
5. Swap the task dialog. Where the application can run without the manifest dependency, ask
   `NativeTaskDialog.IsAvailable` and keep a message-box fallback: `Show` throws and names the
   dependency to add.
6. Delete the helper once both dialogues have been shown from the application, not from a test. No
   test in this repository shows either — a modal dialog would block the run.

## Traps

- **Rectangles are physical pixels, with the right and bottom edges exclusive.** A helper working in
  device-independent units converts at the boundary; `ScaleForWindow` and `ScaleForTaskbar` are the
  factors to convert with.
- **The taskbar's scale is not the process's.** Under per-monitor awareness the process's own scale
  follows whichever monitor its last window was on, which is not where a notification icon is drawn.
  `ScaleForTaskbar` reads the taskbar window itself.
- **`ClampInto` moves a rectangle, never resizes it.** One larger than the bounds keeps their left or
  top edge, so a title bar stays reachable.
