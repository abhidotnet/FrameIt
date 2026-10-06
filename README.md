# FrameIt

FrameIt is a PicPick-style Windows screen capture utility. It runs from the system tray, captures from the keyboard, and opens the shot in an editor for crop, annotation, redaction, and save.

> Milestones covered here: **M1 (Capture)** and **M2 (Editor)**.

## Tech stack

- C# / .NET 8
- WPF desktop app (`net8.0-windows10.0.19041.0`), with WinForms used for the tray icon, folder picker, and custom color dialog
- Windows SDK projection available (Windows.Graphics.Capture support detection included)
- GDI `BitBlt` capture path

No external NuGet packages. `nuget.config` only references nuget.org, and `build.ps1` keeps `DOTNET_CLI_TELEMETRY_OPTOUT=1`.

## Prerequisites

- Windows 10/11 x64
- .NET 8 SDK

> This repository is built from a Linux environment (`dotnet build` / `dotnet publish` with `EnableWindowsTargeting=true`). Capture, hotkeys, DPI, the editor, and file dialogs still have to be validated on a real Windows PC. See `docs/M2-TEST-CHECKLIST.md`.

## Build

Run from the repository root:

```powershell
./build.ps1
```

The script:

1. Restores and builds `FrameIt.sln` in Release.
2. Publishes a **self-contained single-file win-x64** build to `artifacts/portable/`.
3. Publishes a **framework-dependent win-x64** build to `artifacts/framework-dependent/`.

## Hotkeys (default)

- `PrintScreen` → Region capture
- `Shift+PrintScreen` → Full screen (monitor under cursor)
- `Alt+PrintScreen` → Active window
- `Ctrl+Shift+PrintScreen` → Fixed-size region (default 800×600)

Editor shortcuts, after a capture:

- `Ctrl+S` → Save (overwrite the current file, or Save As when there is no file yet)
- `Ctrl+Shift+S` → Save As
- `Ctrl+Z` / `Ctrl+Y` → Undo / Redo
- `Ctrl+C` → Copy the edited, flattened image
- `Delete` → Remove the selected annotation or redaction
- `Esc` → Cancel the in-progress draw, or close. If there are unsaved changes, FrameIt asks before closing.

If hotkey registration fails (for example when Windows Snipping Tool owns PrintScreen), FrameIt shows a tray notification describing how to disable:

`Use the Print screen key to open screen capture`  
in Windows Settings → Accessibility → Keyboard.

## Settings location

- `%APPDATA%\FrameIt\settings.json`
- Timing log file (when enabled): `%APPDATA%\FrameIt\timings.log`

Default capture folder:

- `%USERPROFILE%\Pictures\FrameIt`

Settings added in M2:

- `AutoSaveCaptures` (default **on**). When on, each capture is still written as `capture-YYYYMMDD-HHmmss.png` in the capture folder, and the editor treats that file as the current file.
- `JpegQuality` (default **90**, range 1–100). Used for JPEG saves. The same value is editable in Settings and in the editor status bar.
- `LastSaveFolder`. Updated after a successful Save or Save As, and used as the initial folder in the save dialog.

`LastSaveFolder` is the last folder you saved into. **Open captures folder** on the tray still opens `CaptureFolder`.

## M1 capture

- Single-instance tray app. The menu is: Region, Full screen, Active window, Fixed-size region, Open captures folder, Settings, Exit.
- Global hotkeys, persisted in JSON.
- Region overlay with a magnifier loupe and basic edge snapping.
- Full screen captures the monitor under the cursor.
- Active window and fixed-size region capture.
- Per-monitor DPI (`PerMonitorV2`).
- After capture: copy the original image to the clipboard, optionally auto-save PNG, open the editor.
- Optional local timing logs for startup and capture duration.

## M2 editor

The post-capture window is an editor. Annotations and redactions stay editable until you save or copy; the file and the clipboard image are flattened pixels. There is no separate project file, so opening that PNG or JPEG later (in Paint, for example) shows the flattened result. Saving does not clear the editor, so you can keep moving shapes and save again.

- **Save / Save As.** `Microsoft.Win32.SaveFileDialog`, PNG or JPEG. JPEG uses the quality setting. `Ctrl+S` overwrites the current path.
- **Edits.** Crop, resize (pixels or percent, aspect lock), rotate 90° left/right, flip horizontal/vertical, brightness/contrast. Brightness and contrast preview live and become one undo step when you apply.
- **Annotations.** Arrow, line, rectangle, ellipse, pen, highlighter, text, and auto-numbered steps. Preset colors plus a custom color, with thickness and font size. Select, drag to move, `Delete` to remove. Double-click text to edit it. New steps use the next number; deleting one does not renumber the rest.
- **Redaction.** Blur or pixelate a dragged rectangle. Strength is 1 (subtle) through 20 (heavy), default 8. Redactions can be selected, moved, and deleted until you save or copy.
- **History.** Undo/redo covers edits, annotations, and redactions (up to 40 steps). Pixel edits keep a full image copy per step.

A new capture asks to close the current editor first when that editor has unsaved work.

## Manual test checklists

- M1: `docs/M1-TEST-CHECKLIST.md`
- M2: `docs/M2-TEST-CHECKLIST.md`

## Notes and limitations

- Capture still uses the GDI `BitBlt` path. `Windows.Graphics.Capture` is only probed for the timing log.
- Full screen mode captures the monitor under the cursor, not every monitor at once.
- The editor draws annotations with WPF on screen and with GDI+ when flattening. Text position matches; glyph rasterization can differ by a pixel.
- Resize uses high-quality bicubic sampling.
- Runtime behavior (tray, hotkeys, DPI, editor tools, save dialogs, JPEG output) needs owner validation on Windows 10/11. It cannot be launched in the Linux build environment.
