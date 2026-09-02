# The tray icon container

`ZeroZero.Tray` is the plain half of the tray icon component: the icon file an application
writes for its notification icon, the size the taskbar draws that icon at, and whether the
taskbar it sits on is light or dark. Plain `net10.0`, no XAML, no Windows App SDK, no drawing:
an application renders its icon with whatever it draws with and hands the frames here. The WinUI
host of the icon — lifecycle, listeners, tooltip discipline, click classification, menu refresh —
is a later project under the same key.

The assembly is versioned as `TrayVersion` in `Versions.props` and released under
`tray-v<x.y.z>` tags, with notes under `docs/release-notes/tray/`; [`releasing.md`](releasing.md)
has the procedure. It references `ZeroZero.Win32` for the taskbar's scale, so it releases after
`win32`.

## Requirements

| | |
|---|---|
| SDK | .NET 10 |
| Platform | Windows 10 1809 (build 10.0.17763) or later. The assembly declares itself Windows-only: the taskbar's scale is a window metric and the theme is a registry value. |

## What it contains

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

Not here, by design: what the icon shows, the colours it is drawn in, and when it is redrawn. The
two applications draw unrelated things — a battery arc, a monitor glyph — and each decides for
itself when a state change or a theme change earns a new file.

## Take the reference

Either route in [`consuming.md`](consuming.md). The reference is `ZeroZero.Tray` itself, which
brings `ZeroZero.Win32` with it.

The shape of a renderer on top of it: read the slot size and the stroke tone, draw each frame
with the application's own drawing code at the slot size and at whatever other sizes it wants the
shell to have, encode each as PNG, and hand the list to `IcoFile.Write` for the file the
notify-icon library is pointed at.

```csharp
int slot = TrayIconSlot.PixelsForTaskbar();
StrokeTone tone = TaskbarThemes.StrokeToneFor(TaskbarThemes.Read());

byte[][] frames = [Render(slot, tone), Render(32, tone)];   // the application's own drawing
using var file = File.Create(iconPath);
IcoFile.Write(file, frames);
```

Re-read the theme and re-render when the system theme changes; the reading is not cached here,
because when to refresh is the host's decision and the WinUI host will own the listener.

## Tests

`tests/ZeroZero.Tray.Tests`, plain `net10.0`, Windows only. The container tests write frames
that differ on every axis the directory records — width against height, 256 on either side, three
lengths — and read the file back byte by byte; the slot test reads the taskbar window's DPI
through its own imports and holds the assembly to it; the theme test reads the registry value
through the flat API and holds `Read()` to the documented mapping.
