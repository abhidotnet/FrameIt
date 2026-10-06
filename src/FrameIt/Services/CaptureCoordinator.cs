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
    private readonly Func<AppSettings> _getSettings;
    private readonly Action _persistSettings;
    private EditorWindow? _editor;

    public CaptureCoordinator(
        CaptureService captureService,
        TimingLogger timingLogger,
        WindowEdgeSnapService edgeSnapService,
        Func<AppSettings> getSettings,
        Action persistSettings)
    {
        _captureService = captureService;
        _timingLogger = timingLogger;
        _edgeSnapService = edgeSnapService;
        _getSettings = getSettings;
        _persistSettings = persistSettings;
    }

    public async Task CaptureAsync(CaptureMode mode, AppSettings settings)
    {
        var totalTimer = Stopwatch.StartNew();
        if (!TryCloseEditor())
        {
            return;
        }

        var bounds = await ResolveBoundsAsync(mode, settings);
        if (!bounds.HasValue)
        {
            return;
        }

        using var bitmap = _captureService.CaptureRectangle(bounds.Value);
        var captureElapsed = totalTimer.Elapsed;

        string? filePath = null;
        if (settings.AutoSaveCaptures)
        {
            Directory.CreateDirectory(settings.CaptureFolder);
            filePath = Path.Combine(
                settings.CaptureFolder,
                $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            bitmap.Save(filePath, ImageFormat.Png);
        }

        var source = BitmapInterop.ToBitmapSource(bitmap);
        BitmapInterop.TrySetClipboard(source);

        var editor = new EditorWindow(bitmap, filePath, _getSettings, _persistSettings);
        editor.Closed += (_, _) =>
        {
            if (ReferenceEquals(_editor, editor))
            {
                _editor = null;
            }
        };
        _editor = editor;
        editor.Show();
        editor.Activate();

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

    private bool TryCloseEditor()
    {
        if (_editor is null)
        {
            return true;
        }

        var editor = _editor;
        editor.Close();
        return !editor.IsVisible;
    }
}
