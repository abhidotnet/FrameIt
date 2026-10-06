using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FrameIt.Models;
using FrameIt.UI;

namespace FrameIt.Services;

public sealed class CaptureCoordinator
{
    private readonly CaptureService _captureService;
    private readonly TimingLogger _timingLogger;
    private readonly WindowEdgeSnapService _edgeSnapService;
    private readonly Action<string?> _setBadge;
    private readonly Func<EditorWindow?> _peekEditor;
    private readonly Func<EditorWindow> _ensureEditor;

    public CaptureCoordinator(
        CaptureService captureService,
        TimingLogger timingLogger,
        WindowEdgeSnapService edgeSnapService,
        Action<string?> setBadge,
        Func<EditorWindow?> peekEditor,
        Func<EditorWindow> ensureEditor)
    {
        _captureService = captureService;
        _timingLogger = timingLogger;
        _edgeSnapService = edgeSnapService;
        _setBadge = setBadge;
        _peekEditor = peekEditor;
        _ensureEditor = ensureEditor;
    }

    public async Task CaptureAsync(CaptureMode mode, AppSettings settings)
    {
        var totalTimer = Stopwatch.StartNew();
        var existing = _peekEditor();
        var wasVisible = existing is { IsVisible: true };
        if (wasVisible)
        {
            existing!.HideForCapture();
        }

        // Hide the editor so it is not part of the shot and the countdown is not covered by it.
        // The countdown stays non-activating, then closes before the capture reads the foreground window.
        var settled = false;
        try
        {
            var delayWatch = Stopwatch.StartNew();
            if (!await CaptureDelay.WaitAsync(settings.CaptureDelaySeconds, _setBadge))
            {
                ReopenEditor(existing, wasVisible);
                settled = true;
                return;
            }

            delayWatch.Stop();
            var delayElapsed = delayWatch.Elapsed;

            Bitmap? bitmap;
            if (mode is CaptureMode.Region or CaptureMode.FixedRegion)
            {
                var selectionMode = mode == CaptureMode.FixedRegion
                    ? FrameIt.UI.SelectionMode.FixedSize
                    : FrameIt.UI.SelectionMode.Freeform;
                bitmap = RegionSelectionWindow.CaptureRegion(_edgeSnapService, selectionMode, settings);
            }
            else if (mode == CaptureMode.FullScreen)
            {
                bitmap = _captureService.CaptureVirtualScreen();
            }
            else
            {
                var bounds = await ResolveBoundsAsync(mode, settings);
                bitmap = bounds.HasValue ? _captureService.CaptureRectangle(bounds.Value) : null;
            }

            if (bitmap is null)
            {
                ReopenEditor(existing, wasVisible);
                settled = true;
                return;
            }

            using (bitmap)
            {
                var captureElapsed = SubtractDelay(totalTimer.Elapsed, delayElapsed);

                string? filePath = null;
                if (settings.AutoSaveCaptures)
                {
                    Directory.CreateDirectory(settings.CaptureFolder);
                    filePath = NextCapturePath(settings.CaptureFolder);
                    bitmap.Save(filePath, ImageFormat.Png);
                }

                var source = BitmapInterop.ToBitmapSource(bitmap);
                BitmapInterop.TrySetClipboard(source);

                var editor = _ensureEditor();
                editor.AddCapture(bitmap, filePath, mode);
                editor.Show();
                editor.Activate();
                settled = true;

                if (settings.EnableTimingLogs)
                {
                    var provider = _captureService.IsWindowsGraphicsCaptureSupported()
                        ? "gdi-fallback-wgc-available"
                        : "gdi-fallback";
                    _timingLogger.LogCapture(mode.ToString(), captureElapsed, SubtractDelay(totalTimer.Elapsed, delayElapsed), provider);
                }
            }
        }
        finally
        {
            if (!settled)
            {
                ReopenEditor(existing, wasVisible);
            }
        }
    }

    private async Task<Rectangle?> ResolveBoundsAsync(CaptureMode mode, AppSettings settings)
    {
        await Task.Yield();

        return mode switch
        {
            CaptureMode.ActiveWindow => _captureService.GetActiveWindowBounds(),
            _ => null
        };
    }

    private static TimeSpan SubtractDelay(TimeSpan elapsed, TimeSpan delay)
    {
        var adjusted = elapsed - delay;
        return adjusted < TimeSpan.Zero ? TimeSpan.Zero : adjusted;
    }

    private static void ReopenEditor(EditorWindow? editor, bool wasVisible)
    {
        if (!wasVisible || editor is not { HasTabs: true })
        {
            return;
        }

        editor.Show();
        editor.Activate();
    }

    private static string NextCapturePath(string folder)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(folder, "capture-" + stamp + ".png");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 2; index < 1000; index++)
        {
            candidate = Path.Combine(folder, "capture-" + stamp + "-" + index + ".png");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, "capture-" + stamp + "-" + Guid.NewGuid().ToString("N") + ".png");
    }
}
