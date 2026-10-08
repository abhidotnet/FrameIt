# FrameIt Snap MSIX manual test checklist (Windows 10/11 x64)

Run these on a real Windows x64 PC. The Linux build can compile the app and pack the logo files. It does not have `makeappx.exe`, so it does not produce the `.msix`. Build that on Windows:

```powershell
./build.ps1 -MsixSideload
signtool sign /sha1 EACD61BACD1A4D608F325F5F9E39EF8D3A9F9503 /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 .\artifacts\msix\FrameItSnap_0.5.0.0_x64_sideload.msix
```

Trust `dist/FrameIt-Test-Certificate.cer` in Local Machine Root and Trusted Publisher before installing. The Store upload package is `./build.ps1 -Msix`. Its publisher is `CN=3D428300-FD2F-44F4-9A34-B94ED4E2A79A` and the publisher display name is Edhahkirhs. Identity Name is `Edhahkirhs.FrameItSnap`. The sideload package overrides the publisher to `CN=Abhijit Shrikhande (FrameIt Test)`.

1. **Install and sideload**
   - Install `FrameItSnap_0.5.0.0_x64_sideload.msix`.
   - Confirm Start shows **FrameIt Snap** with the teal and gold frame tile, not a generic icon.
   - Confirm the package version is 0.5.0.0 and the publisher is `CN=Abhijit Shrikhande (FrameIt Test)`.
   - Launch it from Start. Confirm the tray icon is the brand graphic.

2. **Tray and hotkeys**
   - Use the tray menu for Region, Full screen, Active window, and Fixed-size region.
   - Press the default hotkeys (`PrintScreen`, `Shift+PrintScreen`, `Alt+PrintScreen`, `Ctrl+Shift+PrintScreen`).
   - Confirm each capture opens in its own tab, the same as the unpackaged app.

3. **Captures land in real Pictures**
   - With auto-save on, take a capture.
   - Confirm the PNG is in the real Pictures folder (`%USERPROFILE%\Pictures\FrameItSnap` unless Settings points somewhere else), and that File Explorer shows it outside the package folder.
   - Confirm **Open captures folder** opens that same folder.

4. **Session restore while packaged**
   - Capture five screens, wait a couple of seconds after an edit, then end `FrameItSnap.exe` from Task Manager.
   - Start FrameIt Snap again from the Start menu.
   - Confirm all five tabs come back, and an unclean exit shows `Restored 5 tabs from your last session`.
   - Confirm session files are under the package's local data, not a second copy that the unpackaged app would see. `%LOCALAPPDATA%\FrameItSnap\sessions` is virtualized for the package.

5. **Start with Windows**
   - Open Settings → Capture. Confirm **Start FrameIt Snap when Windows starts** is off.
   - Turn it on, save, sign out and back in (or restart). Confirm FrameIt Snap starts and the tray is there.
   - Turn it off, save, and confirm the next sign-in does not start FrameIt Snap.
   - Install the portable or Inno build on a machine without the package. Confirm that checkbox is disabled and the hint says it applies to the Store package only.

6. **Credential Manager**
   - In the packaged app, save an SMTP password and an FTP password.
   - Confirm they are in Windows Credential Manager as `FrameItSnap:smtp-password` and `FrameItSnap:ftp-password`, and that `settings.json` still has no password.
   - Share → Email and Share → Upload (FTP) still use those secrets.

7. **Uninstall cleanup**
   - Uninstall FrameIt Snap from Start or Settings → Apps.
   - Confirm the Start entry and the startup task are gone.
   - Confirm PNGs already saved in Pictures are still there.
   - Confirm the package's virtualized settings and session files are removed with the app.

8. **Windows App Certification Kit**
   - Run WACK against `artifacts/msix/FrameItSnap_0.5.0.0_x64.msix` (the Store build, after Partner Center identity is filled in) and confirm it passes before upload.
