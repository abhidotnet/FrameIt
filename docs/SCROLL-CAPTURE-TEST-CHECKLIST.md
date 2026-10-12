# Scrolling capture checklist (Windows 10/11)

Run this on a real Windows x64 PC after `./build.ps1`. The Linux build compiles FrameIt Snap and runs `tests/ScrollStitchCheck`, which checks overlap, sticky header and footer, and the 20,000 px cap on synthetic rows. It cannot move a window, send `WM_MOUSEWHEEL`, or show the picker.

Each frame is the client area only, cropped with the same physical-pixel path as region capture (`CapturePhysicalRegion`). The stitch keeps a sticky header and footer once. The saved PNG is 96 DPI.

## Setup

1. Build with `./build.ps1` and start `artifacts/framework-dependent/FrameItSnap.exe`. Confirm the tray icon is this build.
2. Set **Capture delay** to 0 unless a step says otherwise.
3. Use two monitors when you can, at different scales (100% and 150%, and 125% or 175% if the hardware allows it). Put one monitor to the left of the primary so its X is negative.
4. Open a long page in each app below. The page must actually scroll. A short page should stop after one frame.

Start scrolling capture from each of these:

- Tray **Scrolling capture**
- Editor ribbon **Scroll** (open the editor from a previous capture first)
- The hotkey in Settings → Capture. The default is `Ctrl+Shift+S` when no other capture mode already uses that binding. If registration fails, the tray balloon names the conflict. Change the binding and try again.

`Ctrl+Shift+S` is also the editor **Save As** shortcut. While FrameIt Snap owns that global hotkey, the editor does not see it. The **Save as** button still works. To use Save As from the keyboard, set scrolling capture to another binding.

## How to read the file

Auto-save writes `capture-YYYYMMDD-HHmmss.png` in the capture folder.

```powershell
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile("C:\path\capture.png")
"{0}x{1} dpi={2}" -f $img.Width, $img.Height, $img.HorizontalResolution
$img.Dispose()
```

Width is the client width in physical pixels, not the window frame and not a DIP size. Height is the stitched page, at most 20,000 px. DPI is 96. In Paint at 100%, text should be as sharp as a region capture of the same window.

The image opens as a **new editor tab**. The tooltip source is **Scrolling**. It does not replace the tab you were editing.

## Picker

1. Start scrolling capture. A dim overlay follows the cursor. The bright rectangle is the client area that will be captured, not the title bar or resize border.
2. Hover a window, a list, and a page. The rectangle should jump to the scrollable child when Windows reports one (Explorer, many Win32 lists) and to the page surface in Chrome or Edge (the render widget).
3. Right-click or press Esc. Capture stops and no new tab appears. The editor, if it was open, comes back.
4. Start again and click the highlighted area.

## Chrome and Edge

On each browser, open a long article (a page you can scroll for several screens).

1. Hover the page, not the tab strip. Click the page.
2. A small **Scrolling capture** panel appears, with a frame count, a height, and **Stop**. It should sit outside the page when another monitor or a free corner is available.
3. The page scrolls by itself. The panel's height increases.
4. When the page cannot scroll further, capture stops. You do not have to press Stop.
5. The PNG width matches the page viewport (client area), not the whole window including the tab strip. A sticky toolbar or pinned header appears once at the top. A sticky footer or cookie bar appears once at the bottom. The article text is not repeated and has no missing band where two frames meet.
6. Repeat on the other monitor, including the one with negative X, at that monitor's scale. The PNG width is the physical client width. A 150% monitor is not a magnified or shrunken copy of the 100% one.
7. Press Esc during a long page. The panel closes. The tab contains the rows captured before Esc, not an empty image.
8. Repeat and click **Stop**. Same result as Esc.

If a site animates the scroll, the stitch can stop early or show a torn seam. Try again with the page idle. Smooth-scroll sites are the ones to note.

## Explorer

1. Open a folder with enough files to scroll (Details view).
2. Hover the file list, not the ribbon. Click it.
3. The list scrolls. Column headers appear once. The status bar, if it was inside the client area and did not scroll, appears once at the bottom.
4. The PNG is the list's client width. Scrollbars are outside the client area and should not be a column of the image.
5. Repeat on the second monitor.

## Word

1. Open a multi-page document. Use Print Layout.
2. Hover the page, click it.
3. Capture runs until the last page. Page content is continuous. The ribbon is not part of the client area you clicked when Word's document window is the scroll target.
4. If Word scrolls with Page Down instead of the wheel, the result should still be one image. If neither message moves the document, capture stops after the first frame. Note which build of Word did that.

## Long chat window

Use a desktop chat with a long history (Teams, Discord, or a browser chat). The composer and the title should stay put while messages scroll.

1. Click the message list.
2. The stitched image includes the pinned header once and the composer once, not once per frame.
3. Messages are in order, without a repeated block the size of the viewport.
4. Stop with the button before the top of the history if the history is huge. The tab still opens.

## Height cap

The stitch stops at 20,000 px. A page longer than that ends with the panel reading **height cap**. The PNG height is 20,000 or a few rows under it if the last step did not fit. The top of the page is what you see, not the bottom.

## Mixed DPI

With one monitor at 100% and one at 150% (or 125% / 175%):

1. Scroll-capture a window that is entirely on the 150% monitor. Compare the PNG at 100% in Paint with a region capture of the same client area. The top of the scroll image matches that region capture in width and sharpness.
2. Move the same window to the 100% monitor and repeat.
3. A window that straddles the two monitors is still cropped from the per-monitor virtual-screen stitch, the same path region capture uses. The seam at the monitor boundary should match a full-screen capture of that same rectangle, not a single resampled blit.

## Delay

Set capture delay to 3 seconds. Start scrolling capture, switch to the target app during the countdown, then pick it when the overlay appears. Esc during the countdown cancels before the overlay. The delay does not appear inside the stitched PNG.
