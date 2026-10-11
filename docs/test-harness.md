# The test harness

`src/ZeroZero.Brand.WinUI.TestHarness` is an unpackaged WinUI 3 exe that puts each shared UI
component on screen from fabricated data, so a change is looked at and measured without building a
consuming application. It is not packed or released. Run it with
`dotnet run --project src/ZeroZero.Brand.WinUI.TestHarness -- <flags>`.

One scenario per run. Mode flags are not combined: the first present in the order `--tray`,
`--native`, `--update`, `--palette`, `--rows`, `--titlebar`, `--settings`, `--prompt`, `--mqtt`
wins, and none opens the About popup. The About popup dismisses itself on focus loss, so a second
window in the same run would close it unseen. The exe exits when its last window closes. Flags are
read from the process command line and compared ordinally.

## Modes

| Flag | Opens |
|---|---|
| (none) | The `BrandAboutWindow` popup, titled "Window Mode". |
| `--hosted` | `BrandAboutControl` in a plain window with ordinary chrome and no update button, titled "Hosted Control Demo". |
| `--opener` | A window with one button that opens the popup, the only way to reproduce a double-click on what opens it. |
| `--mqtt` | The MQTT settings panel: eight windows (light and dark; both groups closed, Broker open, publish list open, a staged edit). |
| `--palette` | The brand resource dictionary, one window per theme. |
| `--rows` | The settings-row vocabulary, one window per theme at page width and one per theme narrow. |
| `--titlebar` | Five Mica windows with the system title bar in each treatment. |
| `--settings` | The settings window shell, one per theme, with four fabricated sections. |
| `--prompt` | The text prompt, one per theme. |
| `--native` | No XAML window: the Win32 task dialog, then a message box naming the button pressed. |
| `--update` | The shared update window and its flows. |
| `--tray` | No window: the tray host's icon in the notification area. |

## Options

`--anchor` adds a small pure-white window a capture can be checked against; About, `--update` and
`--tray` take it. `--probe <path>` writes the numbers a run measured, as described under probe
files.

| Mode | Option | Effect |
|---|---|---|
| About | `--theme Light\|Dark` | Pins the theme. |
| About | `--expand`, `--news` | Opens the libraries list, or the release notes, once the window has settled. |
| About | `--notes <url>` | Points the release notes at another address; an unreachable one shows the failure path. |
| About | `--description <text>`, `--description-file <path>` | Replaces the sample description; the file form is for text with spaces. |
| About | `--probe <path>` | Writes the root's sizing numbers and every named element's position, measured at the width the root is laid out at. `<path>.early.txt` holds the same 250 ms after launch; with `--news`, `<path>.timing.txt` holds every change of the notes text against the milliseconds since the press. |
| `--opener` | `--probe <path>` | Logs every activation change of the opened popup against the milliseconds since the button was pressed. |
| `--mqtt` | `--brand` | Two windows with an extreme studio palette declared where the consumption guide says to declare it. |
| `--mqtt` | `--controls` | Adds rival overrides at the shared-brush and control-key layers, so one capture names the layer a control follows. |
| `--mqtt` | `--mica` | No page background and a Mica backdrop. |
| `--mqtt` | `--error` | An out-of-range port, so the validation tier is on screen. |
| `--mqtt` | `--dialogue "<window title>"` | Opens the device-id dialogue on that one window and opens no other. |
| `--mqtt` | `--info "<window title>"` | The same with the first info bubble's flyout. |
| `--mqtt`, `--palette`, `--rows` | `--probe <path>` | Writes the colour and face that reached each element. |
| `--settings` | `--only Light\|Dark` | Opens one window. |
| `--settings` | `--fit` | Fits the window to its pages. |
| `--settings` | `--rect X,Y,W,H` | Seeds the rectangle store, so the clamp can be seen. |
| `--settings` | `--navigate a,b,c` | Walks the sections by tag. |
| `--settings` | `--rebuild`, `--maximise` | Takes those steps, 800 ms apart, after any navigation. |
| `--settings` | `--close-after <ms>` | Closes the window after that long. |
| `--settings` | `--tint <#RRGGBB>` | Tints the backdrop with that colour instead of the one Windows takes from the wallpaper. |
| `--prompt` | `--confirm "<text>"` or `--cancel` | Opens one prompt, answers it through its own field and button once settled, writes what its task resolved with to `text-prompt-result.txt` in the temp folder (`<null>` for a cancel) and exits. A button that is not found is written to the same file instead. |
| `--prompt` | `--type "<text>"` | Edits the field before either answer. |
| `--native` | `--links` | Renders the buttons as command links. |
| `--native` | `--stock` | Adds the system's Cancel button and sizes the dialog to its content. |
| `--update` | `--stage <name>` | One stage in both themes side by side: `question` (default), `download`, `verifying`, `refusal`, `failure`, `uptodate`, `check-failed`. One stage per run, because the window shows one at a time. |
| `--update` | `--silent <path>` | Runs the trigger that shows nothing over a service with a release to offer and writes what it answered, so the windows it did not open can be counted from outside. |
| `--update` | `--over-about update\|plain --probe <path>` | Opens the About popup, then a window on top of it, and records whether the one beneath dismissed itself. `plain` is the control: an ordinary window the update window's count does not include. |
| `--update` | `--cancel person\|caller --probe <path>` | Downloads from a loopback server and stops it part-way, through the window's own button or through the caller's token; records the answer and what was left on disk. A button that is not found is recorded as a `miss` row. |
| `--tray` | `--file` | Hands the host a file the rig wrote once instead of frames per render. |
| `--tray` | `--menu` | Opens the menu by the tray two seconds after launch. |
| `--tray` | `--menu-theme Light\|Dark` | Pins the menu's theme instead of following the taskbar's. |
| `--tray` | `--place NotificationArea\|Overflow` | Asks the shell to draw the icon there; the setting is put back on exit. |
| `--tray` | `--restore-after <ms>` | Puts the placement back after that long, with the icon still up. |
| `--tray` | `--icon-id <guid>` | Gives the icon another identity, so a registration the shell has never seen can be measured. |
| `--tray` | `--probe <path>` | Writes what the host created, logs every click to `<path>.clicks`, and exits once `<path>.stop` exists. |

## Probe files

- A probe is written to `<path>.tmp` and moved to `<path>`, then an empty `<path>.done` marks the
  move complete. A reader waits for the marker: the probe path is bound while the move is in
  flight, and an open in that window fails with a sharing violation.
- An exception while measuring is written to `<path>.error.txt`. The marker is still written, so a
  reader never waits on a run that failed.
- Rows are tab-separated, one fact per row, so two runs compare with a text diff.
- Logs appended while a run goes (`<path>.timing.txt`, `<path>.clicks`, the `--opener` log) have no
  marker.

## Other files a run writes to the temp folder

| File | Written by |
|---|---|
| `settings-shell-log.txt` | `--settings`: every hook, build, load and save, with a timestamp. |
| `text-prompt-result.txt` | `--prompt --confirm` and `--prompt --cancel`. |
| `mqtt-harness-unhandled.txt` | Any run: an exception that reached the application. |
| `mqtt-harness-error.txt` | `--mqtt`: a panel window that failed to construct. |
| `mqtt-harness-dialogue.txt`, `mqtt-harness-info.txt` | `--mqtt --dialogue` and `--info`: the control to press or open was not found. |
| `tray-harness-failed.txt` | `--tray`: a failure the host reported. |

## Used by

- `scripts/` runs the harness once per surface and stage and captures screenshots.
- `tests/ZeroZero.Tray.Tests` starts `--tray` and `--tray --file` as a child process and measures
  it from outside.
