using FrameItSnap.Models;

namespace FrameItSnap.Services;

/// <summary>
/// Ctrl+Shift+S is the scrolling-capture hotkey when no other capture mode already uses it.
/// That chord is also the editor Save As shortcut. A registered global hotkey is delivered
/// first, so Settings can move scrolling capture off those keys.
/// </summary>
public static class ScrollCaptureHotkey
{
    public const string Preferred = "Ctrl+Shift+S";

    private static readonly string[] Fallbacks = ["Ctrl+Alt+S", "Ctrl+Shift+W"];

    public static string Choose(IReadOnlyDictionary<CaptureMode, string> hotkeys)
    {
        if (!Taken(hotkeys, Preferred))
        {
            return Preferred;
        }

        foreach (var candidate in Fallbacks)
        {
            if (!Taken(hotkeys, candidate))
            {
                return candidate;
            }
        }

        return Preferred;
    }

    private static bool Taken(IReadOnlyDictionary<CaptureMode, string> hotkeys, string candidate)
    {
        foreach (var pair in hotkeys)
        {
            if (pair.Key == CaptureMode.Scrolling)
            {
                continue;
            }

            if (string.Equals(pair.Value?.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
