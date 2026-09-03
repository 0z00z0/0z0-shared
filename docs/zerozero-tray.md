# The tray icon component

Two assemblies under one key. `ZeroZero.Tray` is the plain half: the icon file an application
writes for its notification icon, the size the taskbar draws that icon at, and whether the
taskbar it sits on is light or dark. Plain `net10.0`, no XAML, no Windows App SDK, no drawing.
`ZeroZero.Tray.WinUI` is the host: the icon's lifecycle in a WinUI 3 application with the
notify-icon library's efficiency mode refused, the theme, display and shell-restart listeners,
the cache the rendered file lives in, the tooltip discipline, click classification and the menu
refresh protocol. The application supplies what the icon shows, what the tooltip says, what the
menu holds and what a click does.

Both are versioned as `TrayVersion` in `Versions.props` and released together under
`tray-v<x.y.z>` tags, with notes under `docs/release-notes/tray/`; [`releasing.md`](releasing.md)
has the procedure. `ZeroZero.Tray` references `ZeroZero.Win32` for the taskbar's scale, so the
component releases after `win32`.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows 10 1809 (build 10.0.17763) or later. The plain assembly declares itself Windows-only: the taskbar's scale is a window metric and the theme is a registry value. |
| Host | WinUI 3 on the Windows App SDK at the build kit's pin, unpackaged. The host takes the notify-icon library `H.NotifyIcon.WinUI` at the kit's pin, a ceiling for a consumer that references the library directly. |

## The plain half

- **`IcoFile`** — `Build(frames)` and `Write(stream, frames)` produce a multi-size icon file
  whose frames are PNG streams, the form the shell reads and the one a notify-icon library reloads
  from disk after the taskbar is recreated, which is why an application writes a file rather than
  handing over a bitmap handle that leaks. Each directory entry is filled from the PNG's own
  header — `ReadPngSize` — never from a size the caller claims, so an entry cannot disagree with
  the image behind it. A frame that is not a PNG, has a side of zero or more than 256, or repeats
  another frame's size is refused with the reason.
- **`TrayIconSlot`** — `PixelsFor(scale)` is the 16-unit slot at a display scale (20 at 125 %,
  24 at 150 %, 28 at 175 %, 32 at 200 %), and `PixelsForTaskbar()` is the slot at the taskbar's
  own scale. Under per-monitor DPI awareness the process's scale follows whichever monitor its
  last window was on, which is not where the icon is drawn; an icon rendered at the wrong size is
  resampled by the shell and comes out soft.
- **`TaskbarThemes`** — `Read()` says whether the taskbar is `TaskbarTheme.Light` or `Dark`, from
  the `SystemUsesLightTheme` value under the personalisation key. That is the system-theme
  switch, not the apps-theme switch beside it: the taskbar follows the first, and an icon drawn
  for the second lands dark strokes on a dark taskbar. `FromRegistryValue` is the mapping alone —
  a DWORD of 1 is light and anything else, absent included, is dark — and `StrokeToneFor` names
  the tone an icon's strokes need on a taskbar of that theme: `StrokeTone.Dark` on light,
  `StrokeTone.Light` on dark.

## The host

`TrayHost` is a service, not a control: it owns a shell resource and the messages that reach it,
and no visual tree. It is built from a `TrayHostOptions` and started once on the UI thread after
the XAML runtime is up.

**What the application supplies**, all through the options:

- `Name` — the icon's name in the shell's icon settings and the seed of its identity; `Id` fixes
  the identity outright. Keep either stable across versions, or the shell treats each build as a
  new icon and forgets whether the user chose to show it.
- `Icon` — a delegate from a `TrayIconRequest` (the slot in physical pixels, the taskbar's
  theme, the stroke tone that reads on it) to a `TrayIconImage`: `FromFrames(pngFrames)` for an
  application that draws its state per render, `FromFile(path)` for one with a file per state
  written once. The drawing is the application's.
- `Tooltip` — a delegate returning `TrayTooltipLine`s: a text, and optionally a suffix that
  survives truncation.
- `Menu` — a delegate returning `TrayMenuItem`s: `Command(text, action)` and `Toggle(text,
  isChecked, action)`, each with an enabled flag that defaults to true, and `Separator()`, which
  takes nothing — a rule between groups has nothing to enable.
- `LeftClick` and `DoubleClick` — the actions.
- `CacheDirectory` — where a render is written; the temporary folder when not given.
  `ReopenGuard` — how long after a pop-out's dismissal a click is dropped; the system's
  double-click time when not given.

**What the host does with them.**

- **Lifecycle.** `Start()` creates the icon with the library's efficiency mode refused. The
  library's creation call defaults to arming that mode, which puts the whole process at idle
  priority under power throttling and never restores it — measured in this family as seconds of
  message-pump stall on a left click — and the same default reached both applications
  independently. The host passes the refusal, and a test measures the process from outside
  afterwards rather than reading the argument. `Dispose()` removes the icon and the listeners.
- **Listeners, with their gating.** The taskbar's theme and display changes arrive through
  `SystemEvents`; the shell's restart and the library's DPI notice arrive through the library's
  message window. Each is marshalled to the dispatcher queue of the thread that started the host,
  and each re-reads the request: a changed slot or theme is a new render, an unchanged one is a
  push of the current icon, which repairs an icon the shell dropped without saying so — the push
  is refused, and the icon is added afresh from the state the library still holds. A refresh the
  host started on its own that throws raises `Failed` rather than the application's crash handler.
- **The cache.** A render's frames are composed into one icon file with `IcoFile` and written
  only when the bytes differ from the last write, so a state change that draws the same picture
  costs no disk write and no reload. The frame of the slot's own size is what reaches the shell,
  so nothing is resampled. An application-owned file passes through untouched.
- **Tooltip discipline** (`TrayTooltip.Compose`). Blank lines and repeats are dropped, each line
  trimmed, and the whole held to 127 UTF-16 units — the shell's tip is 128 with its terminator.
  A line that does not fit is cut before its suffix with an ellipsis, so `Battery · 84 %` keeps
  its number after a long name; a line nothing of which would fit is dropped and nothing after it
  is taken, so a later line never rises above an earlier one. A cut never lands between the
  halves of a surrogate pair.
- **Click classification** (`TrayClickPolicy`). A left click is reported at once, without the
  double-click wait, so a pop-out opens without a pause. The double-click message the shell may
  send after it is reported as `Double` only when the click before it was accepted within the
  system's double-click time; the application's `DoubleClick` then runs after its `LeftClick`
  already did. The re-open guard: a pop-out that hides on losing focus loses it to the mouse-down
  of a click on the icon, and the mouse-up would open it again — the application calls
  `NotePopOutDismissed()` from the pop-out's deactivation, and the click within the guard is
  dropped.
- **Menu refresh protocol.** The library renders the flyout as a native popup menu, from which
  only an item's command fires. The host rebuilds the flyout from the descriptor on the right
  mouse-down that precedes the mouse-up the library opens the menu on, so what opens is current
  without the application touching a control; `RefreshMenu()` rebuilds it ahead of that, and
  `ShowMenu(x, y)` opens it at a point on screen the way a right click would.
- **Refresh.** `Refresh()` after a state change asks for the icon and the tooltip again;
  `RefreshTooltip()` the tooltip alone. `CurrentRequest` is the slot and theme last rendered for,
  `IsCreated` whether the shell holds the icon, `Id` the identity it holds it under.

**Stays with the application:** the drawing, notifications — the two applications notify
through different platform APIs, and a host that picked one would impose a rewrite on the other —
what a click opens, where a pop-out goes, and what counts as a deliberate exit.

## Take the reference

Either route in [`consuming.md`](consuming.md). A WinUI application references
`ZeroZero.Tray.WinUI`, which brings `ZeroZero.Tray` and `ZeroZero.Win32` with it; a headless
renderer references `ZeroZero.Tray` alone.

```csharp
// App.xaml.cs
private TrayHost? _tray;

protected override void OnLaunched(LaunchActivatedEventArgs args)
{
    _tray = new TrayHost(new TrayHostOptions
    {
        Name = "Charge Keeper",
        Icon = request => TrayIconImage.FromFrames(
            [_renderer.Render(request.SlotPixels, request.StrokeTone), _renderer.Render(32, request.StrokeTone)]),
        Tooltip = () => [new TrayTooltipLine("Charge Keeper"), new TrayTooltipLine(_battery.Name, $" · {_battery.Percent} %")],
        Menu = () =>
        [
            TrayMenuItem.Command("Settings…", OpenSettings),
            TrayMenuItem.Toggle("Pause", _paused, TogglePause),
            TrayMenuItem.Separator(),
            TrayMenuItem.Command("Exit", ExitDeliberately),
        ],
        LeftClick = TogglePopOut,
        DoubleClick = OpenSettings,
        CacheDirectory = _dataPath,
    });
    _tray.Failed += (_, ex) => _log.Error("tray", ex);
    _tray.Start();
}

// Whenever the state the icon or the tooltip shows has changed:
_tray.Refresh();

// From the pop-out's deactivation, before it hides:
_tray.NotePopOutDismissed();

// On exit:
_tray.Dispose();
```

The renderer behind `Icon` is the application's: read the slot and the stroke tone off the
request, draw each frame at that size and at whatever other sizes the shell should have, and
encode each as PNG. An application with a file per state returns `TrayIconImage.FromFile` instead
and keeps the file current itself.

## Tests and the harness

`tests/ZeroZero.Tray.Tests` is plain `net10.0` and references no WinUI assembly: a WinUI-coupled
test assembly hangs a runner with no desktop session rather than failing it (issue #13). The
plain half is tested directly. The host's framework-free files — the tooltip discipline, the
click policy, the file cache and the descriptors — are compiled in as linked source and pinned
there: the cut before a suffix, the surrogate guard, the stop after a dropped line, the guard
window and the double-click window, the write-or-skip decision.

The host itself is measured through a child process. Three facts start the interactive harness in
its tray mode and wait for the probe it writes once the icon is created. One of them then reads the
process from outside, through the handle the test holds: the priority class is `Normal` and the
power-throttling state has no execution-speed bit, with the icon created, so the argument the host
passes is proved by the process rather than by the argument. The other two read the probe — that
the icon was rendered at the taskbar's own slot and theme, and that an icon the application wrote
itself is created from its path. All three skip, with the reason, where the Windows App Runtime the
harness is built against is not registered for the user — a runner without it would show the
bootstrapper's dialog and wait — and all three kill a harness that writes no probe within thirty
seconds.

`ZeroZero.Brand.WinUI.TestHarness --tray` puts the icon in the notification area from the rig's
own drawing, tooltip and menu and stays until Exit is chosen; `--file` hands the host a file
instead of frames, `--menu` opens the menu by the tray after two seconds, `--promote` asks the
shell to show the icon in the taskbar proper, and `--probe <path>` writes what the host created
and logs every click beside it until a `.stop` file appears.
