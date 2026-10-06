using System.Diagnostics;
using System.Threading;
using System.Windows;
using FrameIt.Models;
using FrameIt.Services;
using FrameIt.UI;
using Forms = System.Windows.Forms;

namespace FrameIt;

public partial class App : Application
{
    private readonly Stopwatch _startupTimer = Stopwatch.StartNew();
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private Action<CaptureMode>? _hotkeyHandler;
    private Forms.NotifyIcon? _notifyIcon;
    private SettingsService? _settingsService;
    private TimingLogger? _timingLogger;
    private GlobalHotkeyService? _hotkeyService;
    private CaptureCoordinator? _captureCoordinator;
    private AppSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TryAcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _timingLogger = new TimingLogger(_settingsService.SettingsDirectory);

        var captureService = new CaptureService();
        var edgeSnapService = new WindowEdgeSnapService();
        _captureCoordinator = new CaptureCoordinator(_settingsService, captureService, _timingLogger, edgeSnapService);

        _hotkeyService = new GlobalHotkeyService();
        _hotkeyHandler = mode => _ = BeginCaptureAsync(mode);
        _hotkeyService.HotkeyPressed += _hotkeyHandler;

        CreateTrayIcon();
        RegisterHotkeys(showConflictNotification: true);

        if (_settings.EnableTimingLogs)
        {
            _timingLogger.LogStartup(_startupTimer.Elapsed);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hotkeyService is not null)
        {
            if (_hotkeyHandler is not null)
            {
                _hotkeyService.HotkeyPressed -= _hotkeyHandler;
            }

            _hotkeyService.Dispose();
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(true, @"Global\FrameIt.SingleInstance", out var createdNew);
        if (createdNew)
        {
            _ownsSingleInstanceMutex = true;
            return true;
        }

        MessageBox.Show(
            "FrameIt is already running in the tray.",
            "FrameIt",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Region", null, (_, _) => _ = BeginCaptureAsync(CaptureMode.Region));
        menu.Items.Add("Full screen", null, (_, _) => _ = BeginCaptureAsync(CaptureMode.FullScreen));
        menu.Items.Add("Active window", null, (_, _) => _ = BeginCaptureAsync(CaptureMode.ActiveWindow));
        menu.Items.Add("Fixed-size region", null, (_, _) => _ = BeginCaptureAsync(CaptureMode.FixedRegion));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open captures folder", null, (_, _) => OpenCaptureFolder());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "FrameIt",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _ = BeginCaptureAsync(CaptureMode.Region);
    }

    private async Task BeginCaptureAsync(CaptureMode mode)
    {
        if (_settings is null || _captureCoordinator is null)
        {
            return;
        }

        if (!await _captureLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            await _captureCoordinator.CaptureAsync(mode, _settings);
        }
        catch (Exception ex)
        {
            ShowNotification($"Capture failed: {ex.Message}");
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private void RegisterHotkeys(bool showConflictNotification)
    {
        if (_hotkeyService is null || _settings is null)
        {
            return;
        }

        var failures = _hotkeyService.RegisterAll(_settings);
        if (!showConflictNotification || failures.Count == 0)
        {
            return;
        }

        var failureText = string.Join(", ", failures.Select(item => $"{item.mode}: {item.binding}"));
        ShowNotification(
            "Some hotkeys could not be registered.\n" +
            $"Conflicts: {failureText}\n" +
            "If PrintScreen is blocked, disable 'Use the Print screen key to open screen capture' in Windows Settings > Accessibility > Keyboard.");
    }

    private void OpenCaptureFolder()
    {
        if (_settings is null)
        {
            return;
        }

        Directory.CreateDirectory(_settings.CaptureFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = _settings.CaptureFolder,
            UseShellExecute = true
        });
    }

    private void OpenSettings()
    {
        if (_settingsService is null || _settings is null)
        {
            return;
        }

        var window = new SettingsWindow(_settings)
        {
            Owner = Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        };

        if (window.ShowDialog() != true || window.UpdatedSettings is null)
        {
            return;
        }

        _settings = window.UpdatedSettings;
        _settingsService.Save(_settings);
        RegisterHotkeys(showConflictNotification: true);
    }

    private void ShowNotification(string message)
    {
        _notifyIcon?.ShowBalloonTip(
            timeout: 5000,
            tipTitle: "FrameIt",
            tipText: message,
            tipIcon: Forms.ToolTipIcon.Info);
    }
}
