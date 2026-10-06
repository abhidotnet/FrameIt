using System.IO;
using System.Text.Json;
using FrameIt.Models;

namespace FrameIt.Services;

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
        SettingsDirectory = Path.Combine(appData, "FrameIt");
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
                "FrameIt");
        }

        settings.FixedRegionWidth = Math.Max(32, settings.FixedRegionWidth);
        settings.FixedRegionHeight = Math.Max(32, settings.FixedRegionHeight);

        settings.Hotkeys ??= new Dictionary<CaptureMode, string>();
        EnsureDefaultHotkey(settings.Hotkeys, CaptureMode.Region, "PrintScreen");
        EnsureDefaultHotkey(settings.Hotkeys, CaptureMode.FullScreen, "Shift+PrintScreen");
        EnsureDefaultHotkey(settings.Hotkeys, CaptureMode.ActiveWindow, "Alt+PrintScreen");
        EnsureDefaultHotkey(settings.Hotkeys, CaptureMode.FixedRegion, "Ctrl+Shift+PrintScreen");
    }

    private static void EnsureDefaultHotkey(Dictionary<CaptureMode, string> map, CaptureMode mode, string defaultValue)
    {
        if (!map.TryGetValue(mode, out var existing) || string.IsNullOrWhiteSpace(existing))
        {
            map[mode] = defaultValue;
        }
    }
}
