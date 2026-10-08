using System.IO;
using System.Text.Json;
using FrameItSnap.Models;

namespace FrameItSnap.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsDirectory { get; }

    public string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        SettingsDirectory = Path.Combine(appData, "FrameItSnap");
    }

    public AppSettings Load()
    {
        Directory.CreateDirectory(SettingsDirectory);

        if (!File.Exists(SettingsFilePath))
        {
            var defaults = CreateDefaultSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (parsed is null)
            {
                parsed = CreateDefaultSettings();
            }

            Normalize(parsed);
            return parsed;
        }
        catch
        {
            var defaults = CreateDefaultSettings();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        EnsureNoCredentialProperties(json);
        File.WriteAllText(SettingsFilePath, json);
    }

    private static AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings();
        Normalize(settings);
        return settings;
    }

    private static void Normalize(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.CaptureFolder))
        {
            settings.CaptureFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "FrameItSnap");
        }

        settings.FixedRegionWidth = Math.Max(32, settings.FixedRegionWidth);
        settings.FixedRegionHeight = Math.Max(32, settings.FixedRegionHeight);
        settings.LastSaveFolder ??= string.Empty;
        if (settings.JpegQuality is < 1 or > 100)
        {
            settings.JpegQuality = 90;
        }

        settings.CaptureDelaySeconds = Math.Clamp(settings.CaptureDelaySeconds, 0, 10);
        settings.Smtp ??= new SmtpSettings();
        settings.Ftp ??= new FtpSettings();
        settings.Sftp ??= new SftpSettings();
        NormalizeSmtp(settings.Smtp);
        NormalizeFtp(settings.Ftp);
        NormalizeSftp(settings.Sftp);

        settings.Hotkeys ??= new Dictionary<CaptureMode, string>();
        EnsureDefaultHotkey(settings.Hotkeys, CaptureMode.Region, "PrintScreen");
        EnsureDefaultHotkey(settings.Hotkeys, CaptureMode.FullScreen, "Shift+PrintScreen");
        EnsureDefaultHotkey(settings.Hotkeys, CaptureMode.ActiveWindow, "Alt+PrintScreen");
        EnsureDefaultHotkey(settings.Hotkeys, CaptureMode.FixedRegion, "Ctrl+Shift+PrintScreen");
    }

    private static void NormalizeSmtp(SmtpSettings smtp)
    {
        smtp.Host = smtp.Host?.Trim() ?? string.Empty;
        smtp.Username = smtp.Username?.Trim() ?? string.Empty;
        smtp.FromAddress = smtp.FromAddress?.Trim() ?? string.Empty;
        smtp.DefaultToAddress = smtp.DefaultToAddress?.Trim() ?? string.Empty;
        if (smtp.Port is < 1 or > 65535)
        {
            smtp.Port = 587;
        }

        if (!Enum.IsDefined(smtp.Security))
        {
            smtp.Security = SmtpSecurityMode.StartTls;
        }
    }

    private static void NormalizeFtp(FtpSettings ftp)
    {
        ftp.Host = ftp.Host?.Trim() ?? string.Empty;
        ftp.Username = ftp.Username?.Trim() ?? string.Empty;
        ftp.RemotePath = NormalizeRemotePath(ftp.RemotePath);
        if (ftp.Port is < 1 or > 65535)
        {
            ftp.Port = 21;
        }
    }

    private static void NormalizeSftp(SftpSettings sftp)
    {
        sftp.Host = sftp.Host?.Trim() ?? string.Empty;
        sftp.Username = sftp.Username?.Trim() ?? string.Empty;
        sftp.PrivateKeyPath = sftp.PrivateKeyPath?.Trim() ?? string.Empty;
        sftp.RemotePath = NormalizeRemotePath(sftp.RemotePath);
        if (sftp.Port is < 1 or > 65535)
        {
            sftp.Port = 22;
        }

        if (!Enum.IsDefined(sftp.AuthMode))
        {
            sftp.AuthMode = SftpAuthMode.Password;
        }
    }

    private static string NormalizeRemotePath(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim().Replace('\\', '/');
        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        return value;
    }

    private static void EnsureNoCredentialProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (ContainsCredentialName(document.RootElement))
        {
            throw new InvalidOperationException("Refusing to write a password or passphrase into settings.json.");
        }
    }

    private static bool ContainsCredentialName(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (ContainsCredentialName(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsCredentialName(item))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static void EnsureDefaultHotkey(Dictionary<CaptureMode, string> map, CaptureMode mode, string defaultValue)
    {
        if (!map.TryGetValue(mode, out var existing) || string.IsNullOrWhiteSpace(existing))
        {
            map[mode] = defaultValue;
        }
    }
}
