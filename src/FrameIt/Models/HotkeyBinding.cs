using System.Windows.Input;

namespace FrameIt.Models;

public sealed class HotkeyBinding
{
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }

    public override string ToString()
    {
        var pieces = new List<string>();

        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            pieces.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            pieces.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            pieces.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            pieces.Add("Win");
        }

        pieces.Add(Key == Key.PrintScreen ? "PrintScreen" : Key.ToString());
        return string.Join("+", pieces);
    }

    public static bool TryParse(string? value, out HotkeyBinding? binding, out string? error)
    {
        binding = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Hotkey cannot be empty.";
            return false;
        }

        ModifierKeys modifiers = ModifierKeys.None;
        Key? key = null;

        foreach (var token in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (token.Equals("printscreen", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("prtsc", StringComparison.OrdinalIgnoreCase))
                    {
                        key = Key.PrintScreen;
                    }
                    else if (Enum.TryParse<Key>(token, true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else
                    {
                        error = $"Unsupported key token '{token}'.";
                        return false;
                    }
                    break;
            }
        }

        if (!key.HasValue)
        {
            error = "A non-modifier key is required (for example PrintScreen).";
            return false;
        }

        binding = new HotkeyBinding
        {
            Modifiers = modifiers,
            Key = key.Value
        };
        return true;
    }
}
