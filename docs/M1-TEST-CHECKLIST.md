# FrameIt M1 Manual Test Checklist (Windows 10/11)

Run these checks on a real Windows PC (x64), ideally with two monitors using different DPI scaling levels.

1. **Tray startup**
   - Launch FrameIt.
   - Confirm only one instance starts.
   - Confirm tray icon appears and right-click menu shows:
     Region, Full screen, Active window, Fixed-size region, Open captures folder, Settings, Exit.

2. **Region capture hotkey**
   - Press `PrintScreen`.
   - Confirm region overlay appears.
   - Drag to select an area.
   - Confirm viewer opens with captured image.

3. **Magnifier and edge snapping**
   - In region mode, move cursor near window edges.
   - Confirm selection edge snaps near window boundaries.
   - Confirm magnifier loupe tracks near cursor and shows zoomed pixels.
   - Confirm the saved PNG does not contain that zoomed loupe. The magnification is preview-only.

4. **Full-screen hotkey**
   - Press `Shift+PrintScreen`.
   - Confirm the monitor currently under cursor is captured.
   - Confirm image is shown in viewer and saved.

5. **Active window hotkey**
   - Focus a non-FrameIt window.
   - Press `Alt+PrintScreen`.
   - Confirm only the active window area is captured.

6. **Fixed-size region hotkey**
   - Press `Ctrl+Shift+PrintScreen`.
   - Click target area.
   - Confirm captured image uses configured fixed dimensions (default 800x600 unless changed).

7. **Clipboard + autosave naming**
   - After each capture, paste into Paint/PowerPoint/Word.
   - Confirm pasted image matches capture.
   - Confirm files are saved under capture folder with format:
     `capture-YYYYMMDD-HHmmss.png`.

8. **Settings persistence + hotkey conflict messaging**
   - Open Settings from tray.
   - Change folder, fixed size, and one hotkey; save.
   - Restart app; confirm settings persist in `%APPDATA%\FrameIt\settings.json`.
   - If PrintScreen registration fails, confirm tray notification explains Windows setting to disable Snipping Tool takeover.

9. **Multi-monitor + mixed DPI pixel correctness**
   - Set monitors to different scale values (for example 100% and 150%).
   - On the 150% monitor, region-capture a window or control whose size you know in device pixels (the selection label shows `width x height`).
   - Confirm the PNG is that many pixels, not the DIP size and not a magnified crop. At 100% zoom in Paint, text and edges match the screen 1:1.
   - Repeat on the 100% monitor, and once with a selection that crosses both monitors.
   - Snap to a window edge and confirm the image still lines up 1:1. Snapping must not zoom or stretch the pixels.
   - Full-screen capture each monitor and confirm the PNG matches that monitor's device-pixel size.

10. **Viewer close + timing goals**
    - Confirm `Esc` closes viewer window.
    - Enable timing logs in Settings.
    - Relaunch app and perform capture.
    - Inspect `%APPDATA%\FrameIt\timings.log` and verify startup and capture timing entries are recorded for measurement of:
      - cold start target < 2 s
      - capture-to-viewer target < 1 s
