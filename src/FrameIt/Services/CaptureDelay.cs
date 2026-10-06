using System.ComponentModel;
using FrameIt.UI;

namespace FrameIt.Services;

public static class CaptureDelay
{
    public static async Task<bool> WaitAsync(int seconds, Action<string?> setBadge)
    {
        seconds = Math.Clamp(seconds, 0, 10);
        if (seconds == 0)
        {
            return true;
        }

        using var cancellation = new CancellationTokenSource();
        CountdownWindow? overlay = null;
        EscapeKeyHook? hook = null;
        try
        {
            overlay = new CountdownWindow();
            try
            {
                hook = new EscapeKeyHook(() => cancellation.Cancel());
            }
            catch (Win32Exception)
            {
                overlay.EnableKeyboardCancel(cancellation);
            }

            overlay.PlaceOnCursorMonitor();
            overlay.Show();

            for (var remaining = seconds; remaining > 0; remaining--)
            {
                overlay.SetRemaining(remaining);
                setBadge("FrameIt - " + remaining + "s");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }

            overlay.Hide();
            // The badge is topmost. Give the desktop a moment to repaint so it is not in the shot.
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            setBadge(null);
            hook?.Dispose();
            overlay?.Close();
        }
    }
}
