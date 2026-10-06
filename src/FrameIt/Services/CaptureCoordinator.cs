using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FrameIt.Models;
using FrameIt.UI;

namespace FrameIt.Services;

public sealed class CaptureCoordinator
{
    private readonly SettingsService _settingsService;
    private readonly CaptureService _captureService;
    private readonly TimingLogger _timingLogger;
    private readonly WindowEdgeSnapService _edgeSnapService;
    private ViewerWindow? _viewer;

    public CaptureCoordinator(
        SettingsService settingsService,
        CaptureService captureService,
        TimingLogger timingLogger,
        WindowEdgeSnapService edgeSnapService)
    {
        _settingsService = settingsService;
        _captureService = captureService;
        _timingLogger = timingLogger;
        _edgeSnapService = edgeSnapService;
    }

    public async Task CaptureAsync(CaptureMode mode, AppSettings settings)
    {
        var totalTimer = Stopwatch.StartNew();
        var bounds = await ResolveBoundsAsync(mode, settings);
        if (!bounds.HasValue)
        {
            return;
        }

        using var bitmap = _captureService.CaptureRectangle(bounds.Value);
        var captureElapsed = totalTimer.Elapsed;

        Directory.CreateDirectory(settings.CaptureFolder);
        var filePath = Path.Combine(
            settings.CaptureFolder,
            $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        bitmap.Save(filePath, ImageFormat.Png);

        var source = BitmapInterop.ToBitmapSource(bitmap);
        SetClipboardImage(source);

        _viewer?.Close();
        _viewer = new ViewerWindow(filePath);
        _viewer.Show();
        _viewer.Activate();

        if (settings.EnableTimingLogs)
        {
            var provider = _captureService.IsWindowsGraphicsCaptureSupported()
                ? "gdi-fallback-wgc-available"
                : "gdi-fallback";
            _timingLogger.LogCapture(mode.ToString(), captureElapsed, totalTimer.Elapsed, provider);
        }
    }

    private async Task<Rectangle?> ResolveBoundsAsync(CaptureMode mode, AppSettings settings)
    {
        await Task.Yield();

        return mode switch
        {
            CaptureMode.Region => RegionSelectionWindow.SelectRegion(_edgeSnapService, FrameIt.UI.SelectionMode.Freeform, settings),
            CaptureMode.FixedRegion => RegionSelectionWindow.SelectRegion(_edgeSnapService, FrameIt.UI.SelectionMode.FixedSize, settings),
            CaptureMode.ActiveWindow => _captureService.GetActiveWindowBounds(),
            CaptureMode.FullScreen => _captureService.GetMonitorUnderCursorBounds(),
            _ => null
        };
    }

    private static void SetClipboardImage(System.Windows.Media.Imaging.BitmapSource source)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(source);
                return;
            }
            catch
            {
                Thread.Sleep(30 * (attempt + 1));
            }
        }
    }
}
