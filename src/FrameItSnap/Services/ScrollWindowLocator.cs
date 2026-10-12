using System.Drawing;
using System.Text;
using FrameItSnap.Interop;

namespace FrameItSnap.Services;

/// <summary>
/// Finds the window under the cursor, skipping FrameIt Snap itself, and sends the
/// scroll messages used while stitching.
/// </summary>
internal readonly record struct ScrollSelection(IntPtr Hwnd, IntPtr Root, Rectangle Client, Point WheelPoint);

internal static class ScrollWindowLocator
{
    internal readonly record struct Hit(IntPtr Hwnd, IntPtr Root, Rectangle Client, string Title);

    public static bool TryHit(Point screen, out Hit hit)
    {
        hit = default;
        var top = TopLevelAt(screen);
        if (top == IntPtr.Zero)
        {
            return false;
        }

        var deepest = DeepestChild(top, screen);
        var chosen = PreferScrollable(deepest);
        if (!TryClient(chosen, out var client))
        {
            if (chosen == deepest || !TryClient(deepest, out client))
            {
                return false;
            }

            chosen = deepest;
        }

        var root = NativeMethods.GetAncestor(chosen, NativeMethods.GaRoot);
        if (root == IntPtr.Zero)
        {
            root = top;
        }

        hit = new Hit(chosen, root, client, TitleOf(root));
        return true;
    }

    public static void TryFocus(IntPtr root)
    {
        if (root == IntPtr.Zero)
        {
            return;
        }

        var current = NativeMethods.GetForegroundWindow();
        var currentThread = current == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(current, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(root, out _);
        var thisThread = NativeMethods.GetCurrentThreadId();
        var attachedCurrent = false;
        var attachedTarget = false;
        try
        {
            if (currentThread != 0 && currentThread != thisThread)
            {
                attachedCurrent = NativeMethods.AttachThreadInput(thisThread, currentThread, true);
            }

            if (targetThread != 0 && targetThread != thisThread && targetThread != currentThread)
            {
                attachedTarget = NativeMethods.AttachThreadInput(thisThread, targetThread, true);
            }

            NativeMethods.SetForegroundWindow(root);
        }
        finally
        {
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(thisThread, targetThread, false);
            }

            if (attachedCurrent)
            {
                NativeMethods.AttachThreadInput(thisThread, currentThread, false);
            }
        }
    }

    public static void SendWheel(IntPtr hwnd, Point screen)
    {
        var target = MessageTarget(hwnd);
        var delta = (short)-NativeMethods.WheelDelta;
        var wParam = (IntPtr)(delta << 16);
        var lParam = PackPoint(screen.X, screen.Y);
        NativeMethods.SendMessageTimeout(
            target,
            NativeMethods.WmMouseWheel,
            wParam,
            lParam,
            NativeMethods.SmtoAbortIfHung,
            250,
            out _);
    }

    public static void SendPageDown(IntPtr hwnd)
    {
        var target = MessageTarget(hwnd);
        // Page Down is an extended key. Scan code 0x51, extended bit, then key-up transition.
        NativeMethods.SendMessageTimeout(
            target,
            NativeMethods.WmKeyDown,
            (IntPtr)NativeMethods.VkNext,
            (IntPtr)0x01510001,
            NativeMethods.SmtoAbortIfHung,
            250,
            out _);
        NativeMethods.SendMessageTimeout(
            target,
            NativeMethods.WmKeyUp,
            (IntPtr)NativeMethods.VkNext,
            unchecked((IntPtr)(int)0xC1510001),
            NativeMethods.SmtoAbortIfHung,
            250,
            out _);
    }

    private static IntPtr MessageTarget(IntPtr picked)
    {
        var thread = NativeMethods.GetWindowThreadProcessId(picked, out _);
        var info = new NativeMethods.GuiThreadInfo
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GuiThreadInfo>()
        };
        if (thread != 0 &&
            NativeMethods.GetGUIThreadInfo(thread, ref info) &&
            info.HwndFocus != IntPtr.Zero &&
            (info.HwndFocus == picked || NativeMethods.IsChild(picked, info.HwndFocus)))
        {
            return info.HwndFocus;
        }

        return picked;
    }

    private static IntPtr TopLevelAt(Point screen)
    {
        var self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == self)
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var bounds = rect.ToRectangle();
            if (screen.X < bounds.Left || screen.X >= bounds.Right || screen.Y < bounds.Top || screen.Y >= bounds.Bottom)
            {
                return true;
            }

            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static IntPtr DeepestChild(IntPtr hwnd, Point screen)
    {
        var current = hwnd;
        for (var i = 0; i < 16; i++)
        {
            var client = new NativeMethods.Point { X = screen.X, Y = screen.Y };
            if (!NativeMethods.ScreenToClient(current, ref client))
            {
                break;
            }

            var child = NativeMethods.RealChildWindowFromPoint(current, client);
            if (child == IntPtr.Zero || child == current)
            {
                break;
            }

            current = child;
        }

        return current;
    }

    private static IntPtr PreferScrollable(IntPtr deepest)
    {
        // A tiny child (a toolbar button, a scrollbar) is a poor capture target.
        // Prefer the nearest ancestor that actually scrolls, otherwise the nearest
        // ancestor whose client area is large enough to capture.
        IntPtr fallback = IntPtr.Zero;
        var current = deepest;
        for (var i = 0; i < 16 && current != IntPtr.Zero; i++)
        {
            var largeEnough = TryClient(current, out _);
            if (largeEnough && HasVerticalScroll(current))
            {
                return current;
            }

            if (largeEnough && fallback == IntPtr.Zero)
            {
                fallback = current;
            }

            var parent = NativeMethods.GetParent(current);
            if (parent == IntPtr.Zero)
            {
                break;
            }

            current = parent;
        }

        return fallback == IntPtr.Zero ? deepest : fallback;
    }

    private static bool HasVerticalScroll(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        if ((style & NativeMethods.WsVScroll) != 0)
        {
            return true;
        }

        var info = new NativeMethods.ScrollInfo
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ScrollInfo>(),
            FMask = NativeMethods.SifRange | NativeMethods.SifPage
        };
        if (!NativeMethods.GetScrollInfo(hwnd, NativeMethods.SbVert, ref info))
        {
            return false;
        }

        var span = info.NMax - info.NMin;
        if (info.NPage == 0)
        {
            return span > 0;
        }

        return span >= (int)info.NPage;
    }

    private static bool TryClient(IntPtr hwnd, out Rectangle client)
    {
        client = Rectangle.Empty;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(hwnd, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 32 || height < 32)
        {
            return false;
        }

        var origin = new NativeMethods.Point();
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
        {
            return false;
        }

        client = new Rectangle(origin.X, origin.Y, width, height);
        return true;
    }

    private static string TitleOf(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    private static IntPtr PackPoint(int x, int y)
    {
        return (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
    }
}
