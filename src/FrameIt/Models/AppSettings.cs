namespace FrameIt.Models;

public sealed class AppSettings
{
    public string CaptureFolder { get; set; } = string.Empty;

    public int FixedRegionWidth { get; set; } = 800;

    public int FixedRegionHeight { get; set; } = 600;

    public bool EnableTimingLogs { get; set; }

    public Dictionary<CaptureMode, string> Hotkeys { get; set; } = new()
    {
        [CaptureMode.Region] = "PrintScreen",
        [CaptureMode.FullScreen] = "Shift+PrintScreen",
        [CaptureMode.ActiveWindow] = "Alt+PrintScreen",
        [CaptureMode.FixedRegion] = "Ctrl+Shift+PrintScreen"
    };
}
