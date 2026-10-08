using System.Runtime.InteropServices;

namespace FrameIt.Services;

/// <summary>
/// Start-with-Windows for the MSIX package. Unpackaged builds (portable and Inno) leave this alone.
/// </summary>
public static class PackagedStartup
{
    public const string TaskId = "FrameItStartup";

    public static bool IsPackaged()
    {
        try
        {
            var length = 0;
            var result = GetCurrentPackageFullName(ref length, 0);
            return result != 15700;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static StartupPreference Query()
    {
        if (!IsPackaged())
        {
            return StartupPreference.Unpackaged();
        }

        try
        {
            return Task.Run(QueryCoreAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return StartupPreference.Unavailable(ex.Message);
        }
    }

    public static string? Apply(bool enable)
    {
        if (!IsPackaged())
        {
            return null;
        }

        return Task.Run(() => ApplyCoreAsync(enable)).GetAwaiter().GetResult();
    }

    private static async Task<StartupPreference> QueryCoreAsync()
    {
        var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
        return Map(task.State, canChange: true);
    }

    private static async Task<string?> ApplyCoreAsync(bool enable)
    {
        var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
        if (enable)
        {
            if (task.State is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
            {
                return null;
            }

            if (task.State == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
            {
                return "Windows previously turned this off. Enable FrameIt under Settings → Apps → Startup, or in Task Manager → Startup apps.";
            }

            if (task.State == Windows.ApplicationModel.StartupTaskState.DisabledByPolicy)
            {
                return "A policy on this PC keeps FrameIt from starting with Windows.";
            }

            var result = await task.RequestEnableAsync();
            if (result is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
            {
                return null;
            }

            if (result == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
            {
                return "Windows previously turned this off. Enable FrameIt under Settings → Apps → Startup, or in Task Manager → Startup apps.";
            }

            return "Windows did not enable the startup task.";
        }

        if (task.State is Windows.ApplicationModel.StartupTaskState.DisabledByPolicy or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
        {
            return "A policy on this PC controls whether FrameIt starts with Windows.";
        }

        task.Disable();
        return null;
    }

    private static StartupPreference Map(Windows.ApplicationModel.StartupTaskState state, bool canChange)
    {
        var enabled = state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
        var locked = state is Windows.ApplicationModel.StartupTaskState.EnabledByPolicy or Windows.ApplicationModel.StartupTaskState.DisabledByPolicy;
        var hint = state switch
        {
            Windows.ApplicationModel.StartupTaskState.DisabledByUser => "Windows previously turned this off. Turn FrameIt back on under Settings → Apps → Startup.",
            Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => "A policy on this PC keeps FrameIt from starting with Windows.",
            Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => "A policy on this PC starts FrameIt with Windows.",
            _ => "Off until you turn it on. Windows may ask the first time."
        };
        return new StartupPreference(true, enabled, canChange && !locked, hint);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, nint packageFullName);
}

public readonly record struct StartupPreference(bool IsPackaged, bool Enabled, bool CanChange, string Hint)
{
    public static StartupPreference Unpackaged()
    {
        return new StartupPreference(
            false,
            false,
            false,
            "The portable and installer builds do not start with Windows. This is available in the Microsoft Store package, and it stays off until you turn it on there.");
    }

    public static StartupPreference Unavailable(string message)
    {
        return new StartupPreference(true, false, false, "Start with Windows is unavailable. " + message);
    }
}
