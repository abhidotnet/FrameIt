# FrameIt

FrameIt is a PicPick-style Windows screen capture utility focused on fast keyboard-driven capture from the system tray.

> Milestone covered here: **M1 (Capture)**.

## Tech stack

- C# / .NET 8
- WPF desktop app (`net8.0-windows10.0.19041.0`)
- Windows SDK projection available (Windows.Graphics.Capture support detection included)
- GDI `BitBlt` capture fallback path for capture execution

No external NuGet dependencies were added for M1.

## Prerequisites

- Windows 10/11 x64
- .NET 8 SDK

> This repository was built and assembled from a Linux cloud environment and must be fully validated on a real Windows machine.

## Build

Run from repository root:

```powershell
./build.ps1
```

The script:

1. Restores and builds `FrameIt.sln` in Release.
2. Publishes **self-contained single-file win-x64** output to `artifacts/portable/`.
3. Publishes **framework-dependent win-x64** output to `artifacts/framework-dependent/`.

## Hotkeys (default)

- `PrintScreen` → Region capture
- `Shift+PrintScreen` → Full screen (monitor under cursor)
- `Alt+PrintScreen` → Active window
- `Ctrl+Shift+PrintScreen` → Fixed-size region (default 800x600)

If hotkey registration fails (for example when Windows Snipping Tool owns PrintScreen), FrameIt shows a tray notification describing how to disable:

`Use the Print screen key to open screen capture`  
in Windows Settings → Accessibility → Keyboard.

## Settings location

- `%APPDATA%\FrameIt\settings.json`
- Timing log file (when enabled): `%APPDATA%\FrameIt\timings.log`

Default capture folder:

- `%USERPROFILE%\Pictures\FrameIt`

## M1 feature checklist

### Done in this milestone

- [x] Single-instance tray app with context menu:
  - Region
  - Full screen
  - Active window
  - Fixed-size region
  - Open captures folder
  - Settings
  - Exit
- [x] Global hotkeys (configurable, persisted to JSON)
- [x] Region overlay capture with magnifier loupe and basic edge snapping
- [x] Full screen capture (monitor under cursor)
- [x] Active window capture
- [x] Fixed-size region capture (configurable width/height)
- [x] Per-monitor DPI manifest settings (`PerMonitorV2`)
- [x] Post-capture workflow:
  - copy to clipboard
  - auto-save PNG (`capture-YYYYMMDD-HHmmss.png`)
  - open minimal viewer window
  - `Esc` closes viewer
- [x] Optional local timing logs for startup and capture durations

### Planned next milestones

- [ ] **M2**: editing/annotation window
- [ ] **M3**: share targets/integrations (no network code in M1)
- [ ] **M4**: packaging/polish/hardening

## Notes and limitations

- M1 currently executes capture via the GDI fallback path.
- Full screen mode captures the **monitor under the cursor** (not all monitors at once).
- Runtime behavior (tray UX, hotkey conflicts, DPI correctness, Windows.Graphics.Capture availability) still needs owner validation on Windows 10/11 hardware.

## Manual test checklist

See `docs/M1-TEST-CHECKLIST.md`.
