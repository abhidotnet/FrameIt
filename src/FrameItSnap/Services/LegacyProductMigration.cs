using System.IO;
using System.Security;
using FrameItSnap.Models;
using Microsoft.Win32;

namespace FrameItSnap.Services;

/// <summary>
/// Copies FrameIt settings, sessions, and the default Pictures folder into FrameItSnap on first run.
/// The old folders are left in place.
/// </summary>
public static class LegacyProductMigration
{
    public const string OldProduct = "FrameIt";

    public const string NewProduct = "FrameItSnap";

    public static void CopyAppDataIfNeeded()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        CopyTreeIfMissing(Path.Combine(roaming, OldProduct), Path.Combine(roaming, NewProduct));
        CopyTreeIfMissing(Path.Combine(local, OldProduct), Path.Combine(local, NewProduct));
    }

    public static bool RetargetDefaultCaptureFolder(AppSettings settings)
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var oldDefault = Path.Combine(pictures, OldProduct);
        var newDefault = Path.Combine(pictures, NewProduct);
        var current = settings.CaptureFolder ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(current) && !PathsEqual(current, oldDefault))
        {
            return false;
        }

        CopyTreeIfMissing(oldDefault, newDefault);
        settings.CaptureFolder = newDefault;
        return true;
    }

    public static void RemoveLegacyRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);
            key?.DeleteValue(OldProduct, throwOnMissingValue: false);
            key?.DeleteValue("FrameIt Beta", throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }

    private static void CopyTreeIfMissing(string source, string destination)
    {
        if (Directory.Exists(destination) || !Directory.Exists(source))
        {
            return;
        }

        CopyTree(source, destination);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
