using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FrameIt.Interop;

namespace FrameIt.UI;

public partial class CountdownWindow : Window
{
    public CountdownWindow()
    {
        InitializeComponent();
    }

    internal void SetRemaining(int seconds)
    {
        CountText.Text = seconds.ToString();
        DetailText.Text = seconds == 1 ? "Capturing in 1 second" : "Capturing in " + seconds + " seconds";
    }

    internal void EnableKeyboardCancel(CancellationTokenSource cancellation)
    {
        Focusable = true;
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key != Key.Escape)
            {
                return;
            }

            cancellation.Cancel();
            e.Handled = true;
        };
        Loaded += (_, _) => Activate();
    }

    internal void PlaceOnCursorMonitor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo
        {
            CbSize = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var scaleX = 1.0;
        var scaleY = 1.0;
        try
        {
            if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out var dpiY) == 0 &&
                dpiX >= 96 &&
                dpiY >= 96)
            {
                scaleX = dpiX / 96.0;
                scaleY = dpiY / 96.0;
            }
        }
        catch (DllNotFoundException)
        {
            scaleX = 1;
            scaleY = 1;
        }
        catch (EntryPointNotFoundException)
        {
            scaleX = 1;
            scaleY = 1;
        }

        var area = info.RcWork.ToRectangle();
        Left = (area.Left / scaleX) + Math.Max(0, ((area.Width / scaleX) - Width) / 2);
        Top = (area.Top / scaleY) + 28;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Do not activate. Active-window capture must still see the window the user switched to.
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }
}
