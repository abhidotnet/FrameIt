# FrameIt

FrameIt is a PicPick-style Windows screen capture utility. It runs from the system tray, captures from the keyboard, and opens the shot in an editor for crop, annotation, redaction, and save.

> Milestones covered here: **M1 (Capture)**, **M2 (Editor)**, and **M3 (Delay and share)**.

## Tech stack

- C# / .NET 8
- WPF desktop app (`net8.0-windows10.0.19041.0`), with WinForms used for the tray icon, folder picker, and custom color dialog
- Windows SDK projection available (Windows.Graphics.Capture support detection included)
- GDI `BitBlt` capture path

No external NuGet packages. `nuget.config` only references nuget.org, and `build.ps1` keeps `DOTNET_CLI_TELEMETRY_OPTOUT=1`.

## Prerequisites

- Windows 10/11 x64
- .NET 8 SDK

> This repository is built from a Linux environment (`dotnet build` / `dotnet publish` with `EnableWindowsTargeting=true`). Capture, hotkeys, DPI, the editor, file dialogs, the capture countdown, Credential Manager, SMTP, and FTP still have to be validated on a real Windows PC. See `docs/M3-TEST-CHECKLIST.md`.

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
- `Shift+PrintScreen` → Full screen (every monitor, one image)
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

Settings added in M3:

- `CaptureDelaySeconds` (default **0**, range 0–10). FrameIt waits this many seconds before every capture mode.
- `Smtp`: `Host`, `Port` (default 587), `Security` (`None` or `StartTls`), `Username`, `FromAddress`, `DefaultToAddress`.
- `Ftp`: `Host`, `Port` (default 21), `RemotePath` (default `/`), `Username`.
- `Sftp`: `Host`, `Port` (default 22), `RemotePath` (default `/`), `Username`, `AuthMode` (`Password` or `PrivateKey`), `PrivateKeyPath`.

`settings.json` has no password or passphrase fields. Those values are written only through the Windows Credential Manager APIs (`CredWrite` / `CredRead` / `CredDelete` in `advapi32`). Each secret is a generic credential for the current user on this computer (`CRED_PERSIST_LOCAL_MACHINE`):

- `FrameIt:smtp-password`
- `FrameIt:ftp-password`
- `FrameIt:sftp-password`
- `FrameIt:sftp-passphrase`

The private-key field is a file path. The key file itself stays where you put it.

## M1 capture

- Single-instance tray app. The menu is: Region, Full screen, Active window, Fixed-size region, Open captures folder, Settings, Exit.
- Global hotkeys, persisted in JSON.
- Region overlay with a magnifier loupe and basic edge snapping. The loupe is a preview only. The saved region is a 1:1 crop of device pixels, including on mixed-DPI monitors. Edge snapping moves the rectangle onto a window edge and does not scale those pixels.
- Full screen captures every monitor into one image the size of the virtual screen, in device pixels. A monitor to the left of the primary (negative coordinates) stays on the left. Region, active window, and fixed-size capture stay on one area.
- Active window and fixed-size region capture.
- Per-monitor DPI (`PerMonitorV2`).
- After capture: copy the original image to the clipboard, optionally auto-save PNG, open the editor.
- Optional local timing logs for startup and capture duration.

## M2 editor

The post-capture window is an editor. The toolbar is a light ribbon: icon and short label, in groups (Clipboard, Image, Tools, Size, Colors, File). Annotations and redactions stay editable until you save or copy; the file and the clipboard image are flattened pixels. There is no separate project file, so opening that PNG or JPEG later (in Paint, for example) shows the flattened result. Saving does not clear the editor, so you can keep moving shapes and save again.

- **Save / Save As.** `Microsoft.Win32.SaveFileDialog`, PNG or JPEG. JPEG uses the quality setting. `Ctrl+S` overwrites the current path.
- **Edits.** Crop (marquee stays on the photo, bright area is kept, Enter or double-click applies), resize (pixels or percent, aspect lock), rotate 90° left/right, flip horizontal/vertical, brightness/contrast. Brightness and contrast preview live and become one undo step when you apply.
- **Annotations.** Arrow, line, rectangle, ellipse, pen, highlighter, text, and auto-numbered steps. Preset colors plus a custom color, with thickness and font size. Select, drag to move, `Delete` to remove. Double-click text to edit it. New steps use the next number; deleting one does not renumber the rest.
- **Redaction.** Blur or pixelate a dragged rectangle. Strength is 1 (subtle) through 20 (heavy), default 8. Redactions can be selected, moved, and deleted until you save or copy.
- **History.** Undo/redo covers edits, annotations, and redactions (up to 40 steps). Pixel edits keep a full image copy per step.

A new capture asks to close the current editor first when that editor has unsaved work.

## M3 delay and share

### Capture delay

Every capture mode waits `CaptureDelaySeconds` before it takes the shot, so you can switch to the window you want. The wait happens after FrameIt closes the current editor (and after any save prompt) and before the region overlay, full-screen grab, active-window grab, or fixed-size grab.

While the timer runs, a small countdown sits at the top of the monitor under the cursor, and the tray icon text shows the remaining seconds (`FrameIt - 3s`). The countdown does not take focus. Esc cancels the pending capture from any window. A delay of 0 starts the capture immediately, with no overlay.

The same 0–10 second choices are on the tray menu under **Capture delay**, and in Settings on the Capture tab.

### Share from the editor

**Share** on the editor toolbar sends the flattened image (annotations and redactions included), the same pixels Save would write:

- **Email** sends it through the SMTP server in Settings. The message goes to the default To address. FrameIt does not ask for a recipient or subject. If the current file is `.jpg` or `.jpeg`, the attachment is a JPEG at the current JPEG quality. Otherwise it is a PNG. The SMTP username defaults to the From address when the username box is empty. Security **STARTTLS** maps to `SmtpClient.EnableSsl` (usually port 587). Implicit TLS on port 465 is not available from the built-in client.
- **Upload (FTP)** stores that file on the configured host with the built-in `FtpWebRequest` client (passive mode, binary). The remote name matches the capture file name, or `capture-YYYYMMDD-HHmmss.png` when the image has not been saved yet. The client connects only to the host you entered.
- **Upload (SFTP)** does not transfer a file in this build. SSH file transfer needs a library such as SSH.NET, and this milestone does not add a NuGet package. The menu explains that, FTP upload still works, and the SFTP settings plus Credential Manager secrets are saved for a later build. `ISftpUploader` is the seam for that build.

X and Instagram are not part of M3.

## Manual test checklists

- M1: `docs/M1-TEST-CHECKLIST.md`
- M2: `docs/M2-TEST-CHECKLIST.md`
- M3: `docs/M3-TEST-CHECKLIST.md`

## Notes and limitations

- Capture still uses the GDI `BitBlt` path. `Windows.Graphics.Capture` is only probed for the timing log.
- Full screen mode is the whole virtual desktop. Gaps between monitors, if the layout is not a solid rectangle, are black. Each monitor's pixels are copied 1:1; they are not scaled to a common DPI.
- The editor draws annotations with WPF on screen and with GDI+ when flattening. Text position matches; glyph rasterization can differ by a pixel.
- Resize uses high-quality bicubic sampling.
- Runtime behavior (tray, hotkeys, DPI, editor tools, save dialogs, JPEG output, the countdown, Esc cancel, Credential Manager, SMTP, and FTP) needs owner validation on Windows 10/11. It cannot be launched in the Linux build environment.
- SMTP security in this build is STARTTLS via `System.Net.Mail.SmtpClient`. Port 465 implicit TLS is not offered.
- SFTP upload waits on owner approval of an SSH package. The interface, settings, and credential slots are in place. FTP upload uses the BCL client.
