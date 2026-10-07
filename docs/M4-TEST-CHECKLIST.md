# FrameIt M4 Manual Test Checklist (Windows 10/11)

Run these on a real Windows x64 PC after `./build.ps1`. M4 adds multi-tab editing and session recovery. The Linux build can compile and publish the project, and it can check the retention rules on file lists. It cannot show the tab bar, kill the process, or restore a window.

Session files are under `%LOCALAPPDATA%\FrameIt\sessions`. They are not the capture folder and not the folder you pick in Save As.

1. **Each capture is its own tab**
   - Take three captures in a row (region, window, full screen) without closing the editor.
   - Confirm the editor stays open and you now have three tabs. The earlier images are unchanged.
   - Confirm each tab is named `Capture 1`, `Capture 2`, `Capture 3`, shows a thumbnail, and has no orange dot when auto-save is on and you have not edited.
   - Hover a tab. Confirm the tooltip shows the local time and the source (region, window, full screen, or fixed).
   - With auto-save off, take one more capture. Confirm that tab's orange dot is visible and the status line says it is not saved yet.

2. **Switch, reorder, and keys**
   - Click each tab. Confirm the image, the status path, and the window title follow the tab you clicked.
   - Press `Ctrl+Tab` and `Ctrl+Shift+Tab`. Confirm the tabs cycle forward and backward.
   - Drag a tab to a new position. Confirm the order stays after you restart FrameIt from the tray **Exit** and launch it again.
   - Right-click a tab. Confirm the menu is **Close**, **Close Others**, **Close All**, and **Duplicate Tab**.

3. **Close a dirty tab**
   - Draw an arrow on a tab so the orange dot appears.
   - Press `Ctrl+W`, or middle-click the tab.
   - Confirm the choices are **Save**, **Discard**, and **Cancel**.
   - Choose **Cancel**. Confirm the tab is still there and the arrow is still there.
   - Close it again and choose **Discard**. Confirm that tab is gone, the capture file already in the capture folder is still there, and the session folder no longer has that tab's files.
   - On another dirty tab, choose **Save**. Confirm the file on disk includes the arrow and the tab closes.
   - **Close Others** leaves the tab you right-clicked. **Close All** asks again for each dirty tab and hides the editor when the last tab closes. **Duplicate Tab** adds a new unsaved tab with the same pixels. It does not point Save at the original file.

4. **Hide is not close**
   - With two tabs open, press `Esc` (when you are not drawing) and confirm the editor hides and FrameIt stays in the tray.
   - Choose **Open editor**. Confirm both tabs are back, including an unsaved arrow.
   - Click the window X. Confirm the same hide behavior. The next **Open editor** still has the tabs.
   - Start a capture while the editor is visible. Confirm the editor is not in the screenshot, and after the shot the previous tabs are still there beside the new one.

5. **Kill the process and restore five tabs**
   - Capture five screens. Leave them all open. Edit one tab (an arrow is enough) and wait at least two seconds so the edit can be written.
   - Kill `FrameIt.exe` from Task Manager (End task). Do not use tray **Exit**.
   - Start FrameIt again.
   - Confirm all five tabs are back, in the same order, with the same images. The edited tab still shows the arrow.
   - Confirm the editor shows one line, and only one line: `Restored 5 tabs from your last session`. It must not ask whether you want to restore.
   - Click that line. Confirm it goes away. The tabs stay.

6. **Clean exit stays quiet**
   - With those tabs still open, choose tray **Exit**.
   - Start FrameIt again.
   - Confirm the tabs are restored and the "Restored N tabs" line is **not** shown.
   - Confirm `%LOCALAPPDATA%\FrameIt\sessions\session.lock` is absent while FrameIt is not running, and present while it is running.

7. **Session folder is separate, and retention has a bound**
   - Confirm `session.json` lists tab order and the active tab, and each open tab has its own folder with an image, a thumbnail, and metadata that includes the capture source (`region`, `window`, `fullscreen`, or `fixed`).
   - Save As into a different folder. Confirm that folder gets the PNG or JPEG you asked for, and the session folder is unchanged aside from the recovery copy.
   - Confirm **Open captures folder** still opens the capture folder, not the session folder.
   - In Settings → Capture, read the session path. Choose **Clear session data**, confirm, and check that capture-folder files are still there. Reopen the editor and confirm the tabs you had open are still open.
   - To check retention on a machine you can spare: quit FrameIt, set the clock or the file dates so an unreferenced file under the session folder is older than 30 days, start FrameIt, and confirm that file is gone. Separately, if leftover files would push the folder past 200 MB, confirm the oldest leftovers go first. Tabs you still have open must remain even if they are large.

8. **Memory shape**
   - Open about 20 captures. Switch between the first and the last.
   - In Task Manager, confirm the working set does not grow by a full image decode on every switch back to a tab you already left (the inactive tabs should stay on disk plus a small thumbnail). This is a spot check, not a profiler run.
   - Edit the active tab, leave it, and come back. Confirm the pixels and annotations are there. Undo on that tab can be empty after the switch; that is expected.

9. **What you already had**
   - Save, Save As, undo on the active tab, copy, share, the capture delay, and Esc during a countdown still behave as in M3.
   - A second capture started while a countdown is running is ignored.
   - With a crop marquee on screen, `Ctrl+Tab` does not switch. Enter applies the crop; Esc cancels it. A new capture applies the crop on the current tab and then opens the new shot.

10. **Brand icon**
    - Confirm the exe, the tray icon, and the editor title bar / Alt+Tab show the FrameIt brand graphic (nested teal and gold frames), not the generic Windows application icon.

11. **FrameIt Beta package**
    - Copy `dist/portable/` to a Windows x64 PC that has the .NET 8 Desktop Runtime installed. Run `FrameIt.exe` from that folder and confirm the tray and editor open.
    - On a PC without that runtime, confirm Windows asks for the .NET 8 Desktop Runtime. Install the x64 Desktop Runtime from https://dotnet.microsoft.com/en-us/download/dotnet/8.0 and run `FrameIt.exe` again.
    - Run `dist/installer/FrameIt-Beta-Setup.exe`. Confirm it installs **FrameIt Beta**, the shortcut uses the FrameIt icon, and the app starts after the .NET 8 Desktop Runtime is present. To rebuild that Setup exe, install Inno Setup 6 and run `./build.ps1 -Installer`.
