using System.Windows.Input;
using System.Windows.Interop;
using FrameIt.Interop;
using FrameIt.Models;

namespace FrameIt.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, CaptureMode> _registrations = new();
    private bool _disposed;

    public event Action<CaptureMode>? HotkeyPressed;

    public GlobalHotkeyService()
    {
        var parameters = new HwndSourceParameters("FrameItHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000) // WS_POPUP
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public List<(CaptureMode mode, HotkeyBinding binding)> RegisterAll(AppSettings settings)
    {
        UnregisterAll();

        var failures = new List<(CaptureMode mode, HotkeyBinding binding)>();
        var pairs = settings.Hotkeys.OrderBy(kvp => kvp.Key).ToArray();

        for (var i = 0; i < pairs.Length; i++)
        {
            var mode = pairs[i].Key;
            if (!HotkeyBinding.TryParse(pairs[i].Value, out var binding, out _))
            {
                continue;
            }

            var hotkeyId = 0x5000 + i;
            var registered = NativeMethods.RegisterHotKey(
                _source.Handle,
                hotkeyId,
                ToNativeModifiers(binding!.Modifiers),
                (uint)KeyInterop.VirtualKeyFromKey(binding.Key));

            if (!registered)
            {
                failures.Add((mode, binding));
                continue;
            }

            _registrations[hotkeyId] = mode;
        }

        return failures;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registrations.Keys.ToArray())
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
            _registrations.Remove(id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey)
        {
            var id = wParam.ToInt32();
            if (_registrations.TryGetValue(id, out var mode))
            {
                HotkeyPressed?.Invoke(mode);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint native = 0;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            native |= NativeMethods.ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            native |= NativeMethods.ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            native |= NativeMethods.ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            native |= NativeMethods.ModWin;
        }

        return native | NativeMethods.ModNoRepeat;
    }
}
