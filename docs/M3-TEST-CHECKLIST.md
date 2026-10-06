# FrameIt M3 Manual Test Checklist (Windows 10/11)

Run these on a real Windows x64 PC after `./build.ps1`. M3 adds a capture delay and editor share. The Linux build can compile and publish the project. It cannot show the tray, the countdown, Credential Manager, or a real SMTP/FTP server.

Use a mailbox and an FTP server you control. Do not point FrameIt at an account you cannot reset.

1. **Delay off**
   - Leave **Capture delay** at 0 (the default). Confirm the tray submenu **Capture delay** has **0 seconds (immediate)** checked.
   - Take a region capture. Confirm there is no countdown and the editor still opens.
   - With timing logs on, confirm a delay of 0 does not add a multi-second gap in `timings.log`.

2. **Countdown and tray badge**
   - Set the delay to 3 in Settings → Capture, save, and confirm the tray check moves to **3 seconds**.
   - Start **Active window**. Confirm a small countdown appears on the monitor under the cursor and counts 3, 2, 1.
   - Confirm the tray icon tooltip reads `FrameIt - 3s`, then `FrameIt - 2s`, then `FrameIt - 1s`, then returns to `FrameIt`.
   - During the count, click another window. Confirm the countdown does not stay in front as the focused window.
   - After it reaches 0, confirm the editor shows that other window, and the countdown itself is not in the shot.

3. **Esc cancels**
   - Set the delay to 5. Start a full-screen capture and press Esc while the countdown is visible, with FrameIt not focused.
   - Confirm the countdown closes, no editor opens, and no new file appears in the capture folder.
   - Press Esc again in another app and confirm Esc works normally once the countdown is gone.

4. **Every capture mode waits**
   - With the delay at 2, run Region, Full screen, Active window, and Fixed-size region.
   - Confirm each one waits, then behaves as in M1 (region overlay, monitor under the cursor, foreground window, fixed rectangle).
   - Set the delay back to 0 from the tray menu. Confirm Settings → Capture shows 0 after you reopen it, and `%APPDATA%\FrameIt\settings.json` contains `"CaptureDelaySeconds": 0`.

5. **Invalid delay**
   - In Settings, enter `-1` and `11`. Confirm FrameIt refuses to save and explains the range is 0–10.
   - Enter `4`, save, and confirm a later capture waits about 4 seconds. The tray list has a matching check.

6. **Secrets stay out of settings.json**
   - Open Credential Manager (Control Panel → Credential Manager → Windows Credentials, or `rundll32.exe keymgr.dll,KRShowKeyMgr`).
   - In Settings → Email, enter an SMTP host, port 587, STARTTLS, a From address, and a default To address. Type a password and save.
   - Confirm a generic credential named `FrameIt:smtp-password` exists, and that `settings.json` contains `Host`, `Port`, `Security`, `Username`, `FromAddress`, and `DefaultToAddress` only. Search the file and confirm it has no password value.
   - Reopen Settings. Confirm the password box is empty and the text says a password is already stored. Save again without typing a password and confirm the credential is still there.
   - Check **Remove saved password**, leave the box blank, save, and confirm that credential is gone.

7. **Share → Email**
   - Save the SMTP password again. Draw an arrow on a capture and choose **Share → Email**.
   - Confirm FrameIt does not ask for a recipient or subject, and the message arrives with the arrow baked into the image.
   - If a crop marquee is still live, sharing applies it, the same way Save does.
   - On a PNG capture, confirm the attachment is a PNG. Save As JPEG, then share again, and confirm the attachment is a JPEG.
   - Clear the SMTP password and share again. Confirm FrameIt explains that the password belongs in Credential Manager.
   - Leave the host blank and share. Confirm FrameIt points you at Settings → Email and does not open a connection.

8. **STARTTLS**
   - Use port 587 and **STARTTLS** against a server you trust. Confirm the message is accepted.
   - FrameIt does not offer implicit TLS. A server that only listens with implicit TLS on port 465 is out of scope for this build.

9. **Share → Upload (FTP)**
   - In Settings → Upload, enter the FTP host, port, remote path, and username. Save the password.
   - Confirm `settings.json` has those fields and no password, and Credential Manager shows `FrameIt:ftp-password`.
   - Choose **Share → Upload (FTP)**. Confirm the file lands in the remote path, passive mode is used, and the image matches the editor (including annotations).
   - Confirm the status line names the host after a successful upload.
   - Enter a bad password in Settings (type a new one so it replaces the stored secret) and confirm the editor shows the FTP failure instead of claiming success.

10. **Share → Upload (SFTP)**
    - In Settings → Upload, fill the SFTP host, port, path, and username. Save a password, then switch authentication to **Private key**, choose a key file, and save a passphrase.
    - Confirm `settings.json` stores `PrivateKeyPath` and does not contain the key text, the password, or the passphrase.
    - Confirm Credential Manager shows `FrameIt:sftp-password` and `FrameIt:sftp-passphrase`.
    - Choose **Share → Upload (SFTP)**. Confirm FrameIt explains that SFTP needs an SSH library such as SSH.NET and that no package was added. Confirm it does not open a network connection for SFTP.
    - Confirm **Share → Upload (FTP)** still works after that message.

11. **Editor behavior you already had**
    - After a cancelled countdown, confirm the previous editor is unchanged when you had dismissed it, and that Save, undo, and Esc-to-close still behave as in M2.
    - Share an image with an unsaved arrow, then press `Ctrl+Z`. Confirm the share did not clear undo.
    - Confirm a second capture during the countdown is ignored until the first wait finishes or is cancelled.
