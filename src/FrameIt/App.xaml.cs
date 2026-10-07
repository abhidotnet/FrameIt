using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using FrameIt.Models;
using FrameIt.Services;
using FrameIt.UI;
using Forms = System.Windows.Forms;

namespace FrameIt;

public partial class App : System.Windows.Application
{
    private readonly Stopwatch _startupTimer = Stopwatch.StartNew();
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private Action<CaptureMode>? _hotkeyHandler;
    private Forms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private Forms.ToolStripMenuItem? _delayMenu;
    private SettingsService? _settingsService;
    private TimingLogger? _timingLogger;
    private GlobalHotkeyService? _hotkeyService;
    private CaptureCoordinator? _captureCoordinator;
    private SessionStore? _sessionStore;
    private EditorWindow? _editor;
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

        _sessionStore = new SessionStore();
        var uncleanSession = _sessionStore.BeginSession();
        var snapshot = _sessionStore.LoadSnapshot();

        var captureService = new CaptureService();
        var edgeSnapService = new WindowEdgeSnapService();
        _captureCoordinator = new CaptureCoordinator(
            captureService,
            _timingLogger,
            edgeSnapService,
            SetTrayBadge,
            () => _editor,
            EnsureEditor);

        _hotkeyService = new GlobalHotkeyService();
        _hotkeyHandler = mode => _ = BeginCaptureAsync(mode);
        _hotkeyService.HotkeyPressed += _hotkeyHandler;

        CreateTrayIcon();
        RegisterHotkeys(showConflictNotification: true);

        if (_settings.EnableTimingLogs)
        {
            _timingLogger.LogStartup(_startupTimer.Elapsed);
        }

        if (snapshot.Tabs.Count > 0)
        {
            try
            {
                var editor = EnsureEditor();
                editor.Restore(snapshot, uncleanSession);
                if (editor.HasTabs)
                {
                    editor.Show();
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Could not restore the last session. " + ex.Message);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _sessionStore?.MarkCleanShutdown();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

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
            _notifyIcon.Icon = null;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;

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

        System.Windows.MessageBox.Show(
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
        _delayMenu = new Forms.ToolStripMenuItem("Capture delay");
        for (var seconds = 0; seconds <= 10; seconds++)
        {
            var item = new Forms.ToolStripMenuItem(DelayLabel(seconds))
            {
                Tag = seconds
            };
            item.Click += DelayMenuItem_OnClick;
            _delayMenu.DropDownItems.Add(item);
        }

        menu.Items.Add(_delayMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open editor", null, (_, _) => ShowEditor());
        menu.Items.Add("Open captures folder", null, (_, _) => OpenCaptureFolder());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon = LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "FrameIt",
            Icon = _trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _ = BeginCaptureAsync(CaptureMode.Region);
        UpdateDelayMenuChecks();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        const string packUri = "pack://application:,,,/Assets/FrameIt.ico";
        var resource = GetResourceStream(new Uri(packUri, UriKind.Absolute));
        if (resource is null)
        {
            throw new InvalidOperationException("FrameIt.ico is not embedded. Expected " + packUri + ".");
        }

        // Icon(Stream) copies the multi-size ICO, so the resource stream can close.
        using (resource.Stream)
        {
            return new System.Drawing.Icon(resource.Stream);
        }
    }

    private void DelayMenuItem_OnClick(object? sender, EventArgs e)
    {
        if (_settings is null || _settingsService is null || sender is not Forms.ToolStripMenuItem item || item.Tag is not int seconds)
        {
            return;
        }

        _settings.CaptureDelaySeconds = Math.Clamp(seconds, 0, 10);
        _settingsService.Save(_settings);
        UpdateDelayMenuChecks();
    }

    private void UpdateDelayMenuChecks()
    {
        if (_delayMenu is null || _settings is null)
        {
            return;
        }

        foreach (Forms.ToolStripItem entry in _delayMenu.DropDownItems)
        {
            if (entry is Forms.ToolStripMenuItem item)
            {
                item.Checked = item.Tag is int seconds && seconds == _settings.CaptureDelaySeconds;
            }
        }
    }

    private static string DelayLabel(int seconds)
    {
        if (seconds == 0)
        {
            return "0 seconds (immediate)";
        }

        return seconds == 1 ? "1 second" : seconds + " seconds";
    }

    private void SetTrayBadge(string? text)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Text = string.IsNullOrWhiteSpace(text) ? "FrameIt" : text;
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

    private void ExitApplication()
    {
        try
        {
            _editor?.PrepareForProcessExit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        Shutdown();
    }

    private EditorWindow EnsureEditor()
    {
        if (_editor is not null)
        {
            return _editor;
        }

        var editor = new EditorWindow(_sessionStore!, () => _settings!, () => _settingsService!.Save(_settings!));
        editor.Closed += (_, _) =>
        {
            if (ReferenceEquals(_editor, editor))
            {
                _editor = null;
            }
        };
        _editor = editor;
        return editor;
    }

    private void ShowEditor()
    {
        if (_editor is not { HasTabs: true })
        {
            ShowNotification("No captures are open.");
            return;
        }

        if (!_editor.IsVisible)
        {
            _editor.Show();
        }

        _editor.Activate();
    }

    private long ClearSessionData()
    {
        if (_editor is not null)
        {
            return _editor.ClearSessionData();
        }

        return _sessionStore?.ClearTabFiles() ?? 0;
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

        var window = new SettingsWindow(_settings, _sessionStore, ClearSessionData)
        {
            Owner = Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        };

        if (window.ShowDialog() != true || window.UpdatedSettings is null)
        {
            return;
        }

        _settings = window.UpdatedSettings;
        _settingsService.Save(_settings);
        UpdateDelayMenuChecks();
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
