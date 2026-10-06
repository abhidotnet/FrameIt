# FrameIt M2 Manual Test Checklist (Windows 10/11)

Run these on a real Windows x64 PC after `./build.ps1`. Use a capture that includes both sharp UI edges and a photo-like area so blur, pixelate, and JPEG quality are easy to judge.

1. **Editor opens from capture**
   - Launch FrameIt and confirm the tray menu still has Region, Full screen, Active window, Fixed-size region, Open captures folder, Settings, and Exit.
   - With auto-save left on (the default), take a region capture.
   - Confirm the editor opens, the status bar shows that PNG path, and a new `capture-YYYYMMDD-HHmmss.png` appears in the capture folder.
   - Paste into Paint and confirm the clipboard received the original capture.
   - With timing logs enabled, confirm `timings.log` still records startup and that the capture total (through the editor opening) is in the same ballpark as M1: cold start under 2 seconds, capture-to-editor under 1 second.

2. **Open captures folder**
   - Choose **Open captures folder** from the tray.
   - Confirm Explorer opens `%USERPROFILE%\Pictures\FrameIt` (or the folder currently set in Settings).

3. **Auto-save off**
   - In Settings, turn off **Auto-save captures as PNG**, save, and capture again.
   - Confirm no new file is written.
   - Press `Esc`. Confirm FrameIt asks before closing. Choose **No** and confirm nothing was saved.
   - Capture again, press `Esc`, choose **Cancel**, and confirm the editor stays open.

4. **Save and Save As**
   - On an auto-saved capture, make a visible edit (draw an arrow) and press `Ctrl+S`.
   - Reopen that PNG in Paint and confirm the arrow is baked into the pixels.
   - Press `Ctrl+Shift+S`, choose a different folder, and save a JPEG.
   - Confirm the JPEG quality box defaults to 90, the file opens, and `%APPDATA%\FrameIt\settings.json` now has that folder in `LastSaveFolder`.
   - Restart FrameIt, Save As again, and confirm the dialog starts in that same folder.
   - Set JPEG quality to 40, save another JPEG, and confirm it looks more compressed than the quality-90 file.
   - On a capture that was not auto-saved, press `Ctrl+S` and confirm it opens Save As instead of failing.

5. **Crop, resize, rotate, flip**
   - With the photo smaller than the editor, drag a crop on the image and continue into the dark margin. The marquee stays on the photo. The bright area is what will remain; the rest is dimmed.
   - Drag an edge or corner to resize, and drag inside the bright area to move it. The rectangle cannot leave the image.
   - Press `Enter` or double-click the bright area. Confirm the image shrinks to that region and `Ctrl+Z` restores it.
   - Repeat on a 150% DPI monitor if you have one. The kept pixels should match the bright area, not a shifted copy.
   - Resize to 50% with aspect lock on, then to an exact pixel size with aspect lock off. Confirm the dimensions in the status bar match.
   - Rotate left, rotate right, flip horizontal, and flip vertical. Confirm each one is a single undo step.

6. **Brightness and contrast**
   - Open **Brightness**, move both sliders, and confirm the image previews before you apply.
   - Choose **Cancel** and confirm the image returns to the previous look.
   - Apply a change and confirm one `Ctrl+Z` removes the whole adjustment.

7. **Annotations**
   - Draw an arrow, line, rectangle, ellipse, pen stroke, and highlighter stroke.
   - Place text and two step markers. Confirm the steps are numbered 1 then 2.
   - Change the preset color, thickness, and font size, then use **Custom** for a color that is not in the row.
   - Select each item, drag it, and press `Delete`. Confirm `Ctrl+Z` brings it back.
   - Double-click a text item and change the words.
   - Before saving, confirm the items can still be moved (they are not stuck as pixels yet).

8. **Redaction**
   - Drag a **Blur** rectangle over text and raise Strength until the text is unreadable.
   - Drag a **Pixelate** rectangle and lower Strength until the blocks get smaller.
   - Select each redaction, move it, and delete it.
   - Save, reopen the file in Paint, and confirm the blur or pixelate is part of the image, not a separate layer.

9. **Undo, redo, and copy**
   - Perform a mix of a crop, an arrow, and a blur.
   - Press `Ctrl+Z` until the image is back to the original capture, then `Ctrl+Y` until the edits return.
   - Press `Ctrl+C` and paste into Paint. Confirm the paste matches the editor, including the arrow and the redaction.
   - Confirm the arrow in the editor can still be moved after the copy.

10. **Unsaved close, and a second capture**
    - Make an edit and press `Esc`. Confirm Yes saves, No discards, and Cancel leaves the editor open.
    - With unsaved work on screen, start another capture from the tray. Confirm FrameIt asks about the current image before the new editor replaces it.
    - Choose Cancel and confirm the original editor and its edits are still there.
