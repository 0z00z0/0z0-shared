# Replacing an application's own native helper

A checklist for an application that already has a helper of its own doing this work — the same
preferred-app-mode calls, the same monitor and DPI numbers, a message box and a task dialog — and
wants to delete it. Nothing new appears on screen: the point is that behaviour survives the swap.
The surface is in [`zerozero-win32.md`](zerozero-win32.md) and the reference routes are in
[`consuming.md`](consuming.md); neither is repeated here.

## What it does not do

Read this first. Whatever an existing helper does from this list stays with the application.

- **Dark chrome is two undocumented uxtheme calls, and the second one is a choice.** The first sets
  the process-wide preferred app mode. The second flushes menu themes, so a menu built before the
  call drops its cached light theme and redraws dark. A helper that makes the preferred-app-mode
  call and then refreshes the immersive colour policy is making a *different* second call, not an
  extra one — neither is a superset of the other, and swapping loses whatever the one being
  replaced did. Compare the second call against the surface the application actually shows: a menu
  already on the screen is what the flush answers, and for a tray application the context menu is
  most of the native chrome there is.
- **Nothing reads the system theme, and nothing themes an individual window.** A helper doing
  either keeps that code.
- **Monitor metrics answer for the cursor's monitor, the primary monitor, and any point given.**
  There is no enumeration of monitors, and no lookup from a window handle. A helper that places a
  window on the display *another window* occupies keeps that lookup; one placing a window at a
  remembered position uses `ForPoint` on the centre of the remembered rectangle.
- **The message boxes are four fixed shapes** — information, warning, error, and a yes-or-no
  question — each with an owner and a topmost option. No custom button wording, no choice of default
  button, no timeout.
- **The task dialog carries caption, headline, body, an expandable detail, a stock icon, buttons,
  the system's own Cancel button and sizing to content.** No progress bar, no verification checkbox,
  no radio buttons, no footer, no hyperlinks, and no callback while it is on screen.
- **The task dialog has no fallback of its own.** On a machine where it cannot be shown, `Show`
  throws. An application whose menu item must always lead somewhere keeps its own simpler prompt and
  branches on `NativeTaskDialog.IsAvailable`: no message box can carry a row of custom buttons, so
  any fallback chosen here would change which button ids a caller can get back, and the wording of
  the simpler prompt is the application's.
- **Which monitor a window goes on, and what any dialog says, stay with the caller.**

## This needs a manifest, which is not the same as needing the build kit

The task dialog exists in common controls version 6 only, and the monitor numbers agree with the
scale a window is drawn at only under per-monitor-v2 awareness. Both are declared in the
executable's own manifest, which no library can write on its behalf.

**Read the application's existing manifest before deciding.** The build kit's template carries four
things and nothing beyond them — the common-controls-6 dependency, per-monitor-v2 awareness, the
supported Windows versions, and the requested execution level (see
[`consume-build-kit.md`](consume-build-kit.md)). A hand-written manifest already declaring the
first two is complete for this assembly: the dialogs and the metrics can be swapped on their own,
and the kit is then a separate decision on its own merits. Where either declaration is missing, the
two are one piece of work — take the kit first and then this, or hand-write what is missing.

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
   working call. On one monitor the point lookup and the primary-monitor fallback give the same
   numbers, so a swap checked on a single screen proves nothing about the case it exists for.
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
