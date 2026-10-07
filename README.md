# FrameIt

FrameIt is a PicPick-style Windows screen capture utility. It runs from the system tray, captures from the keyboard, and opens the shot in an editor for crop, annotation, redaction, and save.

> Milestones covered here: **M1 (Capture)**, **M2 (Editor)**, **M3 (Delay and share)**, and **M4 (Tabs and session recovery)**.

## Tech stack

- C# / .NET 8
- WPF desktop app (`net8.0-windows10.0.19041.0`), with WinForms used for the tray icon, folder picker, and custom color dialog
- Windows SDK projection available (Windows.Graphics.Capture support detection included)
- GDI `BitBlt` capture path

No external NuGet packages. `nuget.config` only references nuget.org, and `build.ps1` keeps `DOTNET_CLI_TELEMETRY_OPTOUT=1`.

## Prerequisites

- Windows 10/11 x64
- To **run** a downloaded build: [.NET 8 Desktop Runtime, Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0). On that page choose **Desktop Runtime**, not the SDK, and the **x64** installer. The ASP.NET runtime and the SDK alone are not enough.
- To **build** from source: .NET 8 SDK

> This repository is built from a Linux environment (`dotnet build` / `dotnet publish` with `EnableWindowsTargeting=true`). Capture, hotkeys, DPI, the editor, tabs, session restore, file dialogs, the capture countdown, Credential Manager, SMTP, and FTP still have to be validated on a real Windows PC. See `docs/M4-TEST-CHECKLIST.md`.

## Download FrameIt Beta

Both downloads are **framework-dependent** win-x64 builds. They are smaller than a self-contained app and do not include the .NET runtime. If the runtime is missing, Windows reports that when you start `FrameIt.exe`. Install the [.NET 8 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), then start FrameIt again.

The tracked files are under `dist/`. `artifacts/` is local build scratch and is not the download. The self-contained publish stays in `artifacts/portable/` and is not committed.

### Installer

`dist/installer/FrameIt-Beta-Setup.exe` is the FrameIt Beta setup program. It installs the same files as `dist/portable/`.

Run `FrameIt-Beta-Setup.exe`. It installs per user, adds a Start menu shortcut named **FrameIt Beta**, and can add a desktop shortcut. Shortcuts use the icon embedded in `FrameIt.exe` (the teal and gold frame). The installer file itself uses `src/FrameIt/Assets/FrameIt.ico`. If the .NET 8 Desktop Runtime is not installed, the wizard offers to open the download page and still lets you finish setup.

Uninstall **FrameIt Beta** from Windows Settings → Apps.

To rebuild the Setup exe, install [Inno Setup 6](https://jrsoftware.org/isinfo.php) and from the repository root run:

```powershell
./build.ps1 -Installer
```

That refreshes `dist/portable/` and compiles `installer/FrameIt-Beta.iss` to `dist/installer/FrameIt-Beta-Setup.exe`. The script looks for `ISCC.exe` on `PATH` and under `Program Files\Inno Setup 6`. The copy in this repo was compiled with Inno Setup 6.7.3 and then test-signed (see below). The installer was not launched on Windows from this environment. `build.ps1` re-publishes `dist/portable/`, which replaces the signed files with unsigned ones, so sign again after a rebuild.

### Portable build

`dist/portable/` is a framework-dependent folder you can copy anywhere (a USB stick, `Downloads`, a tools directory). Keep every file in that folder together. `FrameIt.exe` needs the DLLs beside it.

1. Install the [.NET 8 Desktop Runtime (Windows x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if it is not already installed.
2. Copy the whole `dist/portable/` folder.
3. Run `FrameIt.exe`.

There is no installer and no Start menu shortcut. Exit from the tray icon. Settings still go to `%APPDATA%\FrameIt`, and session recovery still goes to `%LOCALAPPDATA%\FrameIt\sessions`.

### Test signing (beta)

The beta binaries (`dist/portable/FrameIt.exe`, `dist/portable/FrameIt.dll`, and `dist/installer/FrameIt-Beta-Setup.exe`) are signed with a self-signed test certificate, `dist/FrameIt-Test-Certificate.cer` (subject `CN=Abhijit Shrikhande (FrameIt Test)`). The Microsoft DLLs keep their own Microsoft signatures.

On a test PC:

1. Before extracting, unblock the downloaded zip (or the files) so Windows drops the "downloaded from the internet" mark:

   ```powershell
   Unblock-File -Path .\FrameIt.zip
   # or, for files already extracted:
   Get-ChildItem -Recurse | Unblock-File
   ```

2. Trust the test certificate by running these in an **admin** PowerShell from the repository root:

   ```powershell
   Import-Certificate -FilePath .\dist\FrameIt-Test-Certificate.cer -CertStoreLocation Cert:\LocalMachine\Root
   Import-Certificate -FilePath .\dist\FrameIt-Test-Certificate.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
   ```

3. Check a file with `Get-AuthenticodeSignature .\dist\installer\FrameIt-Beta-Setup.exe`. It should report `Valid`.

This only helps on PCs where you import the certificate yourself. It does **not** remove SmartScreen or "unknown publisher" warnings for anyone else. That needs a publicly trusted code-signing certificate, such as Azure Trusted Signing.

## Build

Run from the repository root:

```powershell
./build.ps1
```

The script:

1. Restores and builds `FrameIt.sln` in Release.
2. Publishes a **self-contained single-file win-x64** build to `artifacts/portable/` (local scratch, not committed).
3. Publishes a **framework-dependent win-x64** build to `artifacts/framework-dependent/`.
4. Copies that framework-dependent app, without PDB files, to `dist/portable/`.
5. With `-Installer` on Windows, and Inno Setup 6 installed, compiles `dist/installer/FrameIt-Beta-Setup.exe`.

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
- `Ctrl+Tab` / `Ctrl+Shift+Tab` → Next / previous tab
- `Ctrl+W` → Close the current tab. A tab with unsaved changes asks Save, Discard, or Cancel.
- `Esc` → Cancel the in-progress draw or crop. If nothing is in progress, the editor hides and the tabs stay in the session.

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

- Single-instance tray app. The menu is: Region, Full screen, Active window, Fixed-size region, Capture delay, Open editor, Open captures folder, Settings, Exit.
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
- **Annotations.** Arrow, line, rectangle, ellipse, pen, highlighter, text, and auto-numbered steps. Color 1 is the drawing color. Color 2 is a second well you can switch to. The palette, the eyedropper, and More set the active well. Thickness and font size are in the Size group. Select, drag to move, `Delete` to remove. Double-click text to edit it. New steps use the next number; deleting one does not renumber the rest.
- **Redaction.** Blur or pixelate a dragged rectangle. Strength is 1 (subtle) through 20 (heavy), default 8. Redactions can be selected, moved, and deleted until you save or copy.
- **History.** Undo/redo covers edits, annotations, and redactions (up to 40 steps). Pixel edits keep a full image copy per step.

A new capture opens in its own tab. It does not replace the tab you are editing.

## M3 delay and share

### Capture delay

Every capture mode waits `CaptureDelaySeconds` before it takes the shot, so you can switch to the window you want. The wait happens after FrameIt hides the editor (so the editor is not in the shot) and before the region overlay, full-screen grab, active-window grab, or fixed-size grab. Hiding does not close tabs.

While the timer runs, a small countdown sits at the top of the monitor under the cursor, and the tray icon text shows the remaining seconds (`FrameIt - 3s`). The countdown does not take focus. Esc cancels the pending capture from any window. A delay of 0 starts the capture immediately, with no overlay.

The same 0–10 second choices are on the tray menu under **Capture delay**, and in Settings on the Capture tab.

### Share from the editor

**Share** on the editor toolbar sends the flattened image (annotations and redactions included), the same pixels Save would write:

- **Email** sends it through the SMTP server in Settings. The message goes to the default To address. FrameIt does not ask for a recipient or subject. If the current file is `.jpg` or `.jpeg`, the attachment is a JPEG at the current JPEG quality. Otherwise it is a PNG. The SMTP username defaults to the From address when the username box is empty. Security **STARTTLS** maps to `SmtpClient.EnableSsl` (usually port 587). Implicit TLS on port 465 is not available from the built-in client.
- **Upload (FTP)** stores that file on the configured host with the built-in `FtpWebRequest` client (passive mode, binary). The remote name matches the capture file name, or `capture-YYYYMMDD-HHmmss.png` when the image has not been saved yet. The client connects only to the host you entered.
- **Upload (SFTP)** does not transfer a file in this build. SSH file transfer needs a library such as SSH.NET, and this milestone does not add a NuGet package. The menu explains that, FTP upload still works, and the SFTP settings plus Credential Manager secrets are saved for a later build. `ISftpUploader` is the seam for that build.

X and Instagram are not part of M3.

## M4 tabs and session recovery

Each capture is its own tab. The tab shows a thumbnail and a short name (`Capture 1`, `Capture 2`, …) plus an orange dot while that tab has unsaved edits or a crop marquee that is not applied yet. The tooltip shows the local time and the capture source (region, window, full screen, or fixed).

- Click a tab to switch. Drag a tab to reorder it. Middle-click closes it.
- Right-click: **Close**, **Close Others**, **Close All**, **Duplicate Tab**.
- `Ctrl+Tab` and `Ctrl+Shift+Tab` move between tabs. `Ctrl+W` closes the current tab.
- Closing a dirty tab asks **Save**, **Discard**, or **Cancel**. Save writes the capture file (Save As if it does not have one yet). Discard drops unsaved edits. Neither deletes files that are already in your capture folder.
- The window X button and `Esc` hide the editor. The tabs stay open. **Open editor** on the tray shows them again. **Exit** on the tray leaves the session in place for the next start.

Session files are not your captures. Auto-save and Save As still write PNG or JPEG files in the capture folder or the folder you pick. Recovery files live only here:

`%LOCALAPPDATA%\FrameIt\sessions`

That folder holds `session.json` (tab order and the active tab), `session.lock` while FrameIt is running, and one folder per tab with the editor image, a thumbnail, and metadata (name, time, capture source, unsaved flag, annotations, and redactions). FrameIt writes the tab when the capture is taken, and again about two seconds after an edit.

On startup FrameIt opens those tabs again. It does not ask whether to restore. If the previous process did not exit cleanly (`session.lock` still present, or the session was left dirty), the editor shows one line: `Restored N tabs from your last session`. A normal Exit restores the same tabs with no line.

FrameIt keeps recovery files for **30 days or 200 MB, whichever comes first**. Files for tabs you still have open are kept. Closed tabs are removed when you close them. Anything left behind is deleted once it is older than 30 days, and sooner if the folder would pass 200 MB. **Settings → Capture → Clear session data** deletes the recovery files. It does not delete the capture folder. Tabs that are open are written again afterward.

Only the active tab is fully decoded. Other tabs keep a small thumbnail until you switch to them, so a long session does not keep every screenshot and its undo stack in memory at once. FrameIt renders that thumbnail when the capture is taken and again after each edit, then reuses it until the next edit. Switching away drops that tab's undo stack; the image and the annotations are what come back.

## Manual test checklists

- M1: `docs/M1-TEST-CHECKLIST.md`
- M2: `docs/M2-TEST-CHECKLIST.md`
- M3: `docs/M3-TEST-CHECKLIST.md`
- M4: `docs/M4-TEST-CHECKLIST.md`

## Notes and limitations

- Capture still uses the GDI `BitBlt` path. `Windows.Graphics.Capture` is only probed for the timing log.
- Full screen mode is the whole virtual desktop. Gaps between monitors, if the layout is not a solid rectangle, are black. Each monitor's pixels are copied 1:1; they are not scaled to a common DPI.
- The editor draws annotations with WPF on screen and with GDI+ when flattening. Text position matches; glyph rasterization can differ by a pixel.
- Resize uses high-quality bicubic sampling.
- The exe icon is `src/FrameIt/Assets/FrameIt.ico` (16 through 256). The tray loads that same ICO. Window title bars use `FrameIt-256.png` from the same folder. Tray and Alt+Tab appearance still need a Windows check; this environment cannot show them.
- Runtime behavior (tray, hotkeys, DPI, editor tools, tabs, session restore after a killed process, save dialogs, JPEG output, the countdown, Esc cancel, Credential Manager, SMTP, and FTP) needs owner validation on Windows 10/11. It cannot be launched in the Linux build environment.
- An unapplied crop blocks tab switching until you press Enter or Esc. A new capture applies that crop on the current tab so the new shot can open.
- Undo and redo apply to the tab you are on. Leaving the tab keeps the pixels and the annotations, and starts a fresh undo stack when you come back.
- SMTP security in this build is STARTTLS via `System.Net.Mail.SmtpClient`. Port 465 implicit TLS is not offered.
- SFTP upload waits on owner approval of an SSH package. The interface, settings, and credential slots are in place. FTP upload uses the BCL client.
