namespace FrameItSnap.Models;

public sealed class AppSettings
{
    public string CaptureFolder { get; set; } = string.Empty;

    public int FixedRegionWidth { get; set; } = 800;

    public int FixedRegionHeight { get; set; } = 600;

    public bool EnableTimingLogs { get; set; }

    public bool AutoSaveCaptures { get; set; } = true;

    public string LastSaveFolder { get; set; } = string.Empty;

    public int JpegQuality { get; set; } = 90;

    public int CaptureDelaySeconds { get; set; }

    public SmtpSettings Smtp { get; set; } = new();

    public FtpSettings Ftp { get; set; } = new();

    public SftpSettings Sftp { get; set; } = new();

    public Dictionary<CaptureMode, string> Hotkeys { get; set; } = new()
    {
        [CaptureMode.Region] = "PrintScreen",
        [CaptureMode.FullScreen] = "Shift+PrintScreen",
        [CaptureMode.ActiveWindow] = "Alt+PrintScreen",
        [CaptureMode.FixedRegion] = "Ctrl+Shift+PrintScreen"
    };
}
