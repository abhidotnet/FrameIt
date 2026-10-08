using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FrameItSnap.Services;

/// <summary>
/// Swallows Esc while a capture delay is running, including after the user focuses another window.
/// </summary>
internal sealed class EscapeKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkEscape = 0x1B;

    private readonly LowLevelKeyboardProc _proc;
    private readonly Action _onEscape;
    private IntPtr _hook;

    public EscapeKeyHook(Action onEscape)
    {
        _onEscape = onEscape;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown) && lParam != IntPtr.Zero)
        {
            var vk = Marshal.ReadInt32(lParam);
            if (vk == VkEscape)
            {
                try
                {
                    _onEscape();
                }
                catch
                {
                    // A failure to cancel must not stop keyboard input.
                }

                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
