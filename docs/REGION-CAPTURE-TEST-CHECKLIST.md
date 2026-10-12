# Region capture pixel checklist (Windows 10/11)

Run this on a real Windows x64 PC after `./build.ps1`. The Linux build can compile FrameIt Snap. It cannot show the overlay, a monitor, or a PNG opened in Paint.

The bug this checks: a region capture looked zoomed, soft, or blocky, while full screen of the same desk looked right. The saved region must be the physical pixels inside the rectangle, copied from the same stitched virtual-screen image full screen uses. It must not be scaled to one monitor's DPI, and it must not be a resampled blit of the whole desktop.

Use three monitors when you can. Windows should report at least two 1920×1080 screens, one at X=0 and one at X=1920. Put one monitor to the left of the primary so its X is negative. Set them to different scales at the same time if the hardware allows it (100% and 150% is the important pair; also try 125% and 175%).

## How to read the file

After a capture, FrameIt Snap writes `capture-YYYYMMDD-HHmmss.png` in the capture folder (Settings → Capture, or tray **Open captures folder**).

In PowerShell, with the path of that PNG:

```powershell
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile("C:\path\capture.png")
"{0}x{1} dpi={2}" -f $img.Width, $img.Height, $img.HorizontalResolution
$img.Dispose()
```

The width and height must equal the selection label (`width x height` while you drag). `dpi` must be `96`. Paste into Paint and choose **Image → Attributes**. Paint must show the same width and height.

Open the PNG in Paint at **100%** (View → Zoom → 100%, or Ctrl+0 in some builds). One image pixel is one screen pixel of the capture. Fine text and a 1px checkerboard must not be blurred or doubled.

## Setup

1. Build with `./build.ps1` and start `artifacts/framework-dependent/FrameItSnap.exe` (or the portable exe). Confirm the tray icon is this build, not an older install.
2. Turn **Capture delay** off (0).
3. On each monitor, open a window with small text (Notepad at 10pt, or a browser page) and a 1-pixel checkerboard or a 1px grid. A full-screen window of that pattern on each display makes the comparison obvious.
4. Note each monitor's scale in Settings → System → Display, and its position (left of primary, primary, right).

## 1. Full screen baseline

1. Tray **Full screen** (or Shift+PrintScreen).
2. Confirm one image contains every monitor, in the same arrangement as Windows. The monitor left of the primary is on the left of the image, not wrapped to the right.
3. Run the PowerShell snippet. The PNG width and height are the virtual screen in physical pixels (for two 1920×1080 monitors side by side, 3840×1080, plus any extra height or a third monitor). DPI is 96.
4. At 100% in Paint, text on each monitor matches that monitor. A 150% monitor is not stretched to match a 100% monitor, and it is not blurry relative to the 100% one.
5. Leave this PNG open. Later steps crop from it.

## 2. Region on each monitor at 100%

On a monitor set to 100%:

1. Tray **Region** (or PrintScreen). Drag a tight rectangle around a few words of 1px text, or around a known control. Read the label before you release. It is `width x height` in physical pixels.
2. Confirm the PNG is exactly that wide and tall, DPI 96.
3. In Paint at 100%, the words match the screen pixel for pixel. Edges are not doubled, and the image is not a magnified crop of a smaller area.
4. In the FrameIt Snap editor, the picture is the same size on screen as the area you selected (100% zoom, one image pixel per screen pixel). It is not drawn 1.5× larger and blocky. There is no zoom control; the default view is that size. Scroll if the window is smaller than the image.
5. The loupe's magnified patch is not in the PNG.

Repeat on every monitor that is at 100%.

## 3. Region on each monitor at 150%, 125%, and 175%

Change one monitor to 150% (sign out if Windows asks). Repeat section 2 on that monitor.

Then, if you can, set a monitor to 125% and another pass to 175%. A 1920×1080 monitor at 175% is not an even DIP size (1920 × 96 / 168). The PNG of a full-monitor region must still be 1920×1080, not 1097×617.

What you should see:

- The label and the PNG agree. A region that looks like a 200×80 DIP box on a 150% monitor is a 300×120 image, not 200×80.
- Text is sharp at 100% in Paint. It is not a scaled-up screenshot of fewer pixels, and it is not a scaled-down screenshot that was stretched back.
- The editor shows that image at the same on-screen size as the original rectangle.

## 4. Compare with a crop of the full-screen capture

1. On the 150% monitor, region-capture a rectangle whose top-left you can find again (the corner of a window is enough). Note the label size.
2. Take a full screen capture without moving that window.
3. In Paint, crop the full-screen PNG to the same physical rectangle (same width, height, and position on that monitor).
4. The region PNG and the crop must be the same width and height. Placed on top of each other at 100%, the text and the checkerboard match. No extra blur on the region file.

Do this once on the 100% monitor and once on the monitor with negative X.

## 5. Selection that crosses two monitors

Set the two monitors to different scales (100% and 150%).

1. Drag a region that starts on the first monitor and ends on the second. The label width is the physical pixel width (pixels on the 100% side plus pixels on the 150% side). It is not that width multiplied by 1.5, and it is not the DIP width.
2. The PNG has that exact size, DPI 96.
3. At the boundary, each side matches its own monitor. One side is not stretched or pixelated to match the other. Compare with a crop of a full-screen capture of the same span.

## 6. Monitor with negative coordinates

1. Region-capture on the monitor that sits left of the primary.
2. The image shows that monitor's content, not a slice of the primary, and not a black or shifted band.
3. Dimensions match the label. A full-screen crop of that same area matches.

## 7. Active window

On each monitor, at 100% and at 150%:

1. Focus a normal window (not FrameIt Snap) that is entirely on that monitor. Tray **Active window** (or Alt+PrintScreen).
2. The PNG is the window's physical pixel size, DPI 96. Text matches a region capture of that same window, and it matches a crop of the full-screen image.
3. Drag a window so it straddles two monitors with different scales. Capture it. The image width is the on-screen physical width. Each half matches that monitor. It is not one blurry scale applied to the whole window.

## 8. Fixed-size region

1. Set a fixed size you can recognize (Settings → Capture), for example 400×200.
2. On the 150% monitor, place the fixed rectangle over the checkerboard.
3. The PNG is 400×200 (or smaller only if the rectangle was clamped at the edge of the virtual screen), DPI 96. It is not 400×200 DIPs scaled by 1.5 into a 600×300 image, and it is not a blurry 400×200 downscale of a larger area.

## 9. Clipboard

1. After a 150% region capture, paste into Paint.
2. Image → Attributes shows the same width and height as the PNG.
3. The PowerShell snippet on the saved PNG still reports DPI 96. Word or PowerPoint must not open the paste as a different pixel size than Paint.

## 10. Editor stays 1:1 when you move it

1. Capture a region on the 150% monitor.
2. Move the editor window onto the 100% monitor, then back.
3. On each monitor the picture is one image pixel per screen pixel of that monitor. It does not stay at the previous monitor's zoom, and it does not switch to a stretched Fill of the window.
4. Draw a 1px rectangle annotation on the checkerboard. It sits on the pixel you clicked. Undo still removes it.

## 11. Tab × closes the tab

With two captures open, click the × on one tab. That tab closes the same way as right-click → **Close** (Save, Discard, or Cancel when the tab is dirty). Dragging a tab by its title still reorders it.

## 12. What is still out of scope here

- Capture delay, email, and FTP are covered by `docs/M3-TEST-CHECKLIST.md`.
- The translucent selection overlay can look slightly soft because it is a layered window. Judge the PNG and the editor, not the dimmed preview alone. The loupe is a magnified preview and must not appear in the file.
