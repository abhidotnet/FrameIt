using FrameItSnap.Models;

namespace FrameItSnap.Services;

/// <summary>
/// Scrolling capture uses Ctrl+Alt+PrintScreen, the same chord PicPick uses.
/// Ctrl+Shift+S stays the editor Save As shortcut. If the PrintScreen chord is
/// already assigned to another capture mode, or Windows will not register it,
/// scrolling capture uses Ctrl+Shift+Alt+S instead.
/// </summary>
public static class ScrollCaptureHotkey
{
    public const string Preferred = "Ctrl+Alt+PrintScreen";

    public const string Fallback = "Ctrl+Shift+Alt+S";

    /// <summary>
    /// The first build of scrolling capture used this chord. It is Save As in the editor.
    /// </summary>
    public const string RetiredDefault = "Ctrl+Shift+S";

    public static readonly string[] EditorChords =
    [
        "Ctrl+S",
        "Ctrl+Shift+S",
        "Ctrl+Z",
        "Ctrl+Y",
        "Ctrl+C",
        "Ctrl+W",
        "Ctrl+Tab",
        "Ctrl+Shift+Tab"
    ];

    public static readonly string[] DefaultCaptureChords =
    [
        "PrintScreen",
        "Shift+PrintScreen",
        "Alt+PrintScreen",
        "Ctrl+Shift+PrintScreen",
        Preferred,
        Fallback
    ];

    public static string Choose(IReadOnlyDictionary<CaptureMode, string> hotkeys)
    {
        if (Available(hotkeys, Preferred))
        {
            return Preferred;
        }

        if (Available(hotkeys, Fallback))
        {
            return Fallback;
        }

        return Preferred;
    }

    public static bool IsRetiredDefault(string? value)
    {
        return SameBinding(value, RetiredDefault);
    }

    public static bool ClashesWithEditor(string? value)
    {
        var canonical = Canonical(value);
        if (canonical.Length == 0)
        {
            return false;
        }

        foreach (var chord in EditorChords)
        {
            if (string.Equals(canonical, Canonical(chord), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// When the preferred scrolling chord is the one stored and Windows rejected it,
    /// switch to <see cref="Fallback"/> if that chord is free of the other modes and
    /// of the editor shortcuts.
    /// </summary>
    public static bool TryApplyRegistrationFallback(
        IDictionary<CaptureMode, string> hotkeys,
        IEnumerable<(CaptureMode mode, string binding)> failures)
    {
        if (!hotkeys.TryGetValue(CaptureMode.Scrolling, out var current) || !SameBinding(current, Preferred))
        {
            return false;
        }

        var rejected = false;
        foreach (var failure in failures)
        {
            if (failure.mode == CaptureMode.Scrolling && SameBinding(failure.binding, Preferred))
            {
                rejected = true;
                break;
            }
        }

        if (!rejected || !Available(hotkeys, Fallback))
        {
            return false;
        }

        hotkeys[CaptureMode.Scrolling] = Fallback;
        return true;
    }

    public static bool SameBinding(string? left, string? right)
    {
        var a = Canonical(left);
        var b = Canonical(right);
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static string Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var ctrl = false;
        var shift = false;
        var alt = false;
        var win = false;
        string? key = null;
        foreach (var token in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "win":
                case "windows":
                    win = true;
                    break;
                default:
                    key = token.Equals("printscreen", StringComparison.OrdinalIgnoreCase) ||
                          token.Equals("prtsc", StringComparison.OrdinalIgnoreCase)
                        ? "PrintScreen"
                        : token;
                    break;
            }
        }

        if (key is null)
        {
            return string.Empty;
        }

        var pieces = new List<string>(5);
        if (ctrl)
        {
            pieces.Add("Ctrl");
        }

        if (shift)
        {
            pieces.Add("Shift");
        }

        if (alt)
        {
            pieces.Add("Alt");
        }

        if (win)
        {
            pieces.Add("Win");
        }

        pieces.Add(key);
        return string.Join("+", pieces);
    }

    private static bool Available(IEnumerable<KeyValuePair<CaptureMode, string>> hotkeys, string candidate)
    {
        return !Taken(hotkeys, candidate) && !ClashesWithEditor(candidate);
    }

    private static bool Taken(IEnumerable<KeyValuePair<CaptureMode, string>> hotkeys, string candidate)
    {
        foreach (var pair in hotkeys)
        {
            if (pair.Key == CaptureMode.Scrolling)
            {
                continue;
            }

            if (SameBinding(pair.Value, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
