# FrameIt Snap Privacy Policy

Effective date: October 13, 2026

Publisher: Ednahkirhs (Abhijit Shrikhande)

FrameIt Snap is a Windows screen capture and image editing app. This policy explains what the app does with your data.

## Summary

- FrameIt Snap does not collect personal data and does not send any data to the developer.
- There is no telemetry, analytics, advertising, crash reporting, or automatic update check.
- Your captures, settings, and session files stay on your device.
- The app connects to the network only when you choose to email or upload a capture, and only to the server you configured.

## Data stored on your device

- **Captures.** Saved to your capture folder, by default `%USERPROFILE%\Pictures\FrameItSnap`, or wherever you choose to save.
- **Settings.** `%APPDATA%\FrameItSnap\settings.json`. This holds your preferences, hotkeys, and the email and upload server details you enter (host, port, user name, addresses, remote path). It never contains passwords.
- **Session recovery.** Open editor tabs are saved under `%LOCALAPPDATA%\FrameItSnap\sessions` so they can be restored after the app restarts. These files are cleaned up automatically, and files older than 30 days are removed.
- **Timing log.** `%APPDATA%\FrameItSnap\timings.log` is written only if you turn on timing logs in Settings. It records startup and capture timings and contains no image content.
- **Temporary files.** When you email or upload a capture, a temporary copy is written to `%TEMP%\FrameItSnap` and deleted after sending.

In the Microsoft Store version, Windows keeps the settings and session files in the app's private storage and removes them when you uninstall the app. Captures in your Pictures folder are not removed.

## Passwords

SMTP, FTP, and SFTP passwords and the SFTP key passphrase are stored in Windows Credential Manager for your Windows account on this computer. Windows protects them. They are never written to the settings file or sent to the developer. You can remove them in the app's Settings or in Windows Credential Manager.

## Email and upload

FrameIt Snap sends a capture only when you choose **Share** → Email or Upload. The image goes directly from your computer to the SMTP or FTP server you configured, using the credentials you saved. Nothing passes through the developer's servers. If you turn off STARTTLS for email, or use plain FTP, the data and password are sent unencrypted. Choose secure settings with your server where possible. SFTP upload is not available in this version.

## Clipboard

Each new capture, and any image you copy from the editor, is placed on the Windows clipboard so you can paste it into other apps. FrameIt Snap does not read or monitor your clipboard.

## Sharing with third parties

FrameIt Snap does not sell, share, or transfer your data to anyone. The only data that leaves your device is what you choose to send to your own email or upload server.

## Children

FrameIt Snap does not knowingly collect any information from anyone, including children under 13.

## Changes to this policy

If this policy changes, the updated version will be posted in this file with a new effective date.

## Contact

Questions or concerns: https://github.com/abhidotnet/FrameIt/issues
