using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FrameItSnap.Editing;
using FrameItSnap.Models;
using CaptureMode = FrameItSnap.Models.CaptureMode;
using FrameItSnap.Services;
using Forms = System.Windows.Forms;

namespace FrameItSnap.UI;

public partial class EditorWindow
{
    private readonly SessionStore _sessions;
    private readonly List<EditorTab> _tabs = new();
    private readonly Dictionary<EditorTab, TabChrome> _chrome = new();
    private DispatcherTimer _sessionTimer = null!;
    private EditorTab? _activeTab;
    private EditorTab? _dragTab;
    private Border? _dragHeader;
    private System.Windows.Point _tabDragOrigin;
    private bool _dragMoved;
    private bool _exitRequested;
    private int _nextCaptureNumber = 1;

    private EditorSession _session => _activeTab?.Session
        ?? throw new InvalidOperationException("The active tab is not loaded.");

    private string? _filePath
    {
        get => _activeTab?.FilePath;
        set
        {
            if (_activeTab is not null)
            {
                _activeTab.FilePath = value;
            }
        }
    }

    public bool HasTabs => _tabs.Count > 0;

    private void InitializeSessionTimer()
    {
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sessionTimer.Tick += (_, _) => FlushOpenTabs();
    }

    public void AddCapture(Bitmap bitmap, string? filePath, CaptureMode source)
    {
        AddTab(
            bitmap,
            string.IsNullOrEmpty(filePath) ? null : filePath,
            source,
            new List<Annotation>(),
            new List<Redaction>(),
            dirty: string.IsNullOrEmpty(filePath),
            ownsBitmap: false);
    }

    public void Restore(SessionSnapshot snapshot, bool uncleanShutdown)
    {
        _nextCaptureNumber = Math.Max(1, snapshot.NextCaptureNumber);
        foreach (var record in snapshot.Tabs)
        {
            var tab = new EditorTab(record.Id, record.Name, record.CaptureSource, record.CreatedUtc)
            {
                FilePath = record.FilePath,
                Dirty = record.Dirty,
                Annotations = record.Annotations,
                Redactions = record.Redactions
            };
            NoteCaptureNumber(tab.Name);
            TryLoadThumbnail(tab);
            _tabs.Add(tab);
        }

        if (_tabs.Count == 0)
        {
            return;
        }

        var active = _tabs.FirstOrDefault(tab => string.Equals(tab.Id, snapshot.ActiveId, StringComparison.OrdinalIgnoreCase))
            ?? _tabs[0];
        ActivateTab(active);
        if (uncleanShutdown && _tabs.Count > 0)
        {
            RestoreNoticeText.Text = $"Restored {_tabs.Count} tabs from your last session";
            RestoreNoticeBar.Visibility = Visibility.Visible;
        }
    }

    public void HideForCapture()
    {
        FlushOpenTabs();
        if (IsVisible)
        {
            Hide();
        }
    }

    public void PrepareForProcessExit()
    {
        _exitRequested = true;
        _sessionTimer.Stop();
        if (_activeTab?.Session is not null)
        {
            CommitStyleChange();
            CommitInlineText();
        }

        FlushOpenTabs();
    }

    public long ClearSessionData()
    {
        var temporary = new List<EditorTab>();
        foreach (var tab in _tabs)
        {
            if (tab.Session is not null)
            {
                continue;
            }

            if (!EnsureSession(tab))
            {
                throw new IOException("Could not read a session image, so nothing was cleared.");
            }

            temporary.Add(tab);
        }

        var wrote = false;
        try
        {
            var freed = _sessions.ClearTabFiles();
            foreach (var tab in _tabs)
            {
                if (tab.Session is null)
                {
                    continue;
                }

                RenderThumbnail(tab);
                PersistTab(tab, writeImage: true);
            }

            PersistManifest();
            wrote = true;
            return freed;
        }
        finally
        {
            if (wrote)
            {
                foreach (var tab in temporary)
                {
                    if (ReferenceEquals(tab, _activeTab))
                    {
                        continue;
                    }

                    tab.Session?.Dispose();
                    tab.Session = null;
                }
            }
        }
    }

    private void AddTab(
        Bitmap bitmap,
        string? filePath,
        CaptureMode source,
        List<Annotation> annotations,
        List<Redaction> redactions,
        bool dirty,
        bool ownsBitmap)
    {
        if (_activeTab?.Session is not null)
        {
            CommitStyleChange();
            CommitInlineText();
            if (_cropLive)
            {
                ApplyLiveCrop();
            }

            SuspendActiveTab();
        }

        var tab = new EditorTab(Guid.NewGuid().ToString("N"), NextCaptureName(), source, DateTimeOffset.UtcNow)
        {
            FilePath = filePath,
            Dirty = dirty,
            Annotations = annotations,
            Redactions = redactions
        };
        var session = new EditorSession(bitmap, startDirty: dirty);
        if (annotations.Count > 0 || redactions.Count > 0)
        {
            session.Restore(CloneAnnotations(annotations), CloneRedactions(redactions));
        }

        if (ownsBitmap)
        {
            bitmap.Dispose();
        }

        tab.Session = session;
        _tabs.Add(tab);
        _activeTab = tab;
        session.Changed += OnSessionChanged;
        RenderThumbnail(tab);
        try
        {
            PersistTab(tab, writeImage: true);
            PersistManifest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Could not update session recovery. " + ex.Message;
        }

        ShowLoadedTab();
        RebuildHeaders();
    }

    private bool ActivateTab(EditorTab tab)
    {
        if (ReferenceEquals(tab, _activeTab) && tab.Session is not null)
        {
            UpdateTabVisuals();
            return true;
        }

        if (_activeTab is not null && !ReferenceEquals(_activeTab, tab))
        {
            if (!TryPrepareToLeaveActiveTab())
            {
                return false;
            }

            SuspendActiveTab();
        }

        _activeTab = tab;
        if (tab.Session is null && !EnsureSession(tab))
        {
            RemoveBrokenTab(tab);
            return false;
        }

        tab.Session!.Changed += OnSessionChanged;
        ShowLoadedTab();
        try
        {
            PersistManifest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Could not update session recovery. " + ex.Message;
        }

        if (_chrome.Count != _tabs.Count)
        {
            RebuildHeaders();
        }
        else
        {
            UpdateTabVisuals();
        }

        return true;
    }

    private bool TryPrepareToLeaveActiveTab()
    {
        if (_activeTab?.Session is null)
        {
            return true;
        }

        CommitStyleChange();
        CommitInlineText();
        if (_dragging)
        {
            CancelInteraction();
        }

        if (_cropLive)
        {
            StatusText.Text = "Apply the crop with Enter, or press Esc to cancel it, before switching tabs.";
            return false;
        }

        return true;
    }

    private void SuspendActiveTab()
    {
        var tab = _activeTab;
        if (tab?.Session is null)
        {
            return;
        }

        _sessionTimer.Stop();
        if (_dragging)
        {
            CancelInteraction();
        }

        if (_cropLive)
        {
            ClearLiveCrop();
        }

        tab.Session.Changed -= OnSessionChanged;
        tab.Dirty = tab.Session.IsDirty;
        tab.Annotations = CloneAnnotations(tab.Session.Annotations);
        tab.Redactions = CloneRedactions(tab.Session.Redactions);
        try
        {
            PersistTab(tab, writeImage: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Could not update session recovery. " + ex.Message;
        }

        _patches.Clear();
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _displayedBitmap = null;
        _selectedAnnotation = null;
        _selectedRedaction = null;
        tab.Session.Dispose();
        tab.Session = null;
    }

    private bool EnsureSession(EditorTab tab)
    {
        if (tab.Session is not null)
        {
            return true;
        }

        try
        {
            using var bitmap = _sessions.LoadImage(tab.Id);
            var session = new EditorSession(bitmap, startDirty: tab.Dirty);
            session.Restore(CloneAnnotations(tab.Annotations), CloneRedactions(tab.Redactions));
            tab.Session = session;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private void ShowLoadedTab()
    {
        _patches.Clear();
        _selectedAnnotation = null;
        _selectedRedaction = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        DisplaySessionImage();
        RebuildVisuals();
        UpdateStatus();
        if (_activeTab is { Thumbnail: null, Session: not null })
        {
            RenderThumbnail(_activeTab);
        }

        Focus();
    }

    private void RemoveBrokenTab(EditorTab tab)
    {
        _sessions.DeleteTab(tab.Id);
        _tabs.Remove(tab);
        _chrome.Remove(tab);
        if (ReferenceEquals(_activeTab, tab))
        {
            _activeTab = null;
        }

        if (_tabs.Count == 0)
        {
            PersistManifest();
            RebuildHeaders();
            Hide();
            return;
        }

        ActivateTab(_tabs[0]);
    }

    private bool CloseTab(EditorTab tab)
    {
        if (ReferenceEquals(tab, _activeTab) && tab.Session is not null)
        {
            CommitStyleChange();
            CommitInlineText();
            if (_dragging)
            {
                CancelInteraction();
            }
        }

        var dirty = tab.IsDirty || (ReferenceEquals(tab, _activeTab) && _cropLive);
        if (dirty)
        {
            var choice = AskSaveOrDiscard(tab);
            if (choice == CloseChoice.Cancel)
            {
                return false;
            }

            if (choice == CloseChoice.Save)
            {
                if (!ReferenceEquals(tab, _activeTab) && !ActivateTab(tab))
                {
                    return false;
                }

                if (!Save())
                {
                    return false;
                }
            }
            else if (ReferenceEquals(tab, _activeTab))
            {
                ClearLiveCrop();
            }
        }

        RemoveTab(tab);
        return true;
    }

    private void CloseOthers(EditorTab keep)
    {
        foreach (var tab in _tabs.ToList())
        {
            if (ReferenceEquals(tab, keep))
            {
                continue;
            }

            if (!CloseTab(tab))
            {
                return;
            }
        }
    }

    private void CloseAllTabs()
    {
        foreach (var tab in _tabs.ToList())
        {
            if (!CloseTab(tab))
            {
                return;
            }
        }
    }

    private void RemoveTab(EditorTab tab)
    {
        if (tab.Session is not null)
        {
            tab.Session.Changed -= OnSessionChanged;
            tab.Session.Dispose();
            tab.Session = null;
        }

        var wasActive = ReferenceEquals(_activeTab, tab);
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        _chrome.Remove(tab);
        if (wasActive)
        {
            _activeTab = null;
        }

        try
        {
            _sessions.DeleteTab(tab.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Could not remove the session file. " + ex.Message;
        }

        if (_tabs.Count == 0)
        {
            _patches.Clear();
            try
            {
                PersistManifest();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            RebuildHeaders();
            Hide();
            return;
        }

        if (wasActive)
        {
            var nextIndex = Math.Clamp(index, 0, _tabs.Count - 1);
            ActivateTab(_tabs[nextIndex]);
        }
        else
        {
            try
            {
                PersistManifest();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (_chrome.Count != _tabs.Count || TabStrip.Children.Count != _tabs.Count)
        {
            RebuildHeaders();
        }
        else
        {
            UpdateTabVisuals();
        }
    }

    private void DuplicateTab(EditorTab source)
    {
        if (!ReferenceEquals(source, _activeTab))
        {
            if (!ActivateTab(source))
            {
                return;
            }
        }
        else if (!TryPrepareToLeaveActiveTab())
        {
            return;
        }

        if (source.Session is null)
        {
            return;
        }

        var image = ImageEffects.CloneArgb(source.Session.Image);
        var annotations = CloneAnnotations(source.Session.Annotations);
        var redactions = CloneRedactions(source.Session.Redactions);
        AddTab(image, filePath: null, source.Source, annotations, redactions, dirty: true, ownsBitmap: true);
    }

    private void CycleTabs(int delta)
    {
        if (_tabs.Count < 2 || _activeTab is null)
        {
            return;
        }

        if (!TryPrepareToLeaveActiveTab())
        {
            return;
        }

        var index = _tabs.IndexOf(_activeTab);
        var next = index + delta;
        if (next < 0)
        {
            next = _tabs.Count - 1;
        }
        else if (next >= _tabs.Count)
        {
            next = 0;
        }

        ActivateTab(_tabs[next]);
    }

    private bool TryHandleTabKeys(Key key, ModifierKeys mods)
    {
        if (key == Key.Tab && mods == ModifierKeys.Control)
        {
            CycleTabs(1);
            return true;
        }

        if (key == Key.Tab && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CycleTabs(-1);
            return true;
        }

        if (key == Key.W && mods == ModifierKeys.Control && _activeTab is not null)
        {
            CloseTab(_activeTab);
            return true;
        }

        return false;
    }

    private void ScheduleSessionPersist()
    {
        _sessionTimer.Stop();
        _sessionTimer.Start();
    }

    private void FlushOpenTabs()
    {
        _sessionTimer.Stop();
        if (_activeTab?.Session is not null)
        {
            try
            {
                PersistTab(_activeTab, writeImage: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText.Text = "Could not update session recovery. " + ex.Message;
            }
        }

        try
        {
            PersistManifest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Could not update session recovery. " + ex.Message;
        }
    }

    private void PersistTab(EditorTab tab, bool writeImage)
    {
        var annotations = tab.Session is not null ? tab.Session.Annotations : tab.Annotations;
        var redactions = tab.Session is not null ? tab.Session.Redactions : tab.Redactions;
        var record = new SessionTabRecord
        {
            Id = tab.Id,
            Name = tab.Name,
            CreatedUtc = tab.CreatedUtc,
            CaptureSource = tab.Source,
            SourceToken = SessionStore.SourceToken(tab.Source),
            FilePath = tab.FilePath,
            Dirty = tab.IsDirty,
            Annotations = annotations,
            Redactions = redactions
        };
        var image = writeImage ? tab.Session?.Image : null;
        _sessions.SaveTab(record, image, writeImage && image is not null);
    }

    private void PersistManifest()
    {
        var index = _activeTab is null ? 0 : Math.Max(0, _tabs.IndexOf(_activeTab));
        _sessions.WriteManifest(
            _tabs.Select(tab => tab.Id).ToList(),
            index,
            _nextCaptureNumber,
            dirtyShutdown: true);
    }

    private void RenderThumbnail(EditorTab tab)
    {
        if (tab.Session is null)
        {
            return;
        }

        try
        {
            using var thumb = SessionStore.CreateThumbnail(tab.Session.Image);
            try
            {
                _sessions.WriteThumbnail(tab.Id, thumb);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            tab.Thumbnail = BitmapInterop.ToBitmapSource(thumb);
            if (_chrome.TryGetValue(tab, out var chrome))
            {
                chrome.Thumb.Source = tab.Thumbnail;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    private void TryLoadThumbnail(EditorTab tab)
    {
        var path = _sessions.ThumbPathFor(tab.Id);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 96;
            image.EndInit();
            image.Freeze();
            tab.Thumbnail = image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
        }
    }

    private void RebuildHeaders()
    {
        _dragTab = null;
        _dragHeader = null;
        _dragMoved = false;
        TabStrip.Children.Clear();
        _chrome.Clear();
        foreach (var tab in _tabs)
        {
            var chrome = CreateHeader(tab);
            _chrome.Add(tab, chrome);
            TabStrip.Children.Add(chrome.Root);
        }

        UpdateTabVisuals();
    }

    private TabChrome CreateHeader(EditorTab tab)
    {
        var thumb = new System.Windows.Controls.Image
        {
            Width = 44,
            Height = 30,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            Source = tab.Thumbnail,
            Margin = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = tab.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 140
        };
        var dirty = new Ellipse
        {
            Width = 8,
            Height = 8,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 81, 0)),
            Visibility = tab.IsDirty ? Visibility.Visible : Visibility.Collapsed
        };
        var close = new System.Windows.Controls.Button
        {
            Content = "×",
            Width = 18,
            Height = 18,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(0),
            Tag = "close",
            ToolTip = "Close tab",
            Focusable = false,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent
        };
        close.Click += (_, _) => CloseTab(tab);

        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(thumb);
        row.Children.Add(label);
        row.Children.Add(dirty);
        row.Children.Add(close);

        var border = new Border
        {
            Tag = tab,
            Child = row,
            Margin = new Thickness(6, 6, 0, 6),
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = TabToolTip(tab),
            SnapsToDevicePixels = true
        };
        border.PreviewMouseLeftButtonDown += Header_OnPreviewMouseLeftButtonDown;
        border.PreviewMouseMove += Header_OnPreviewMouseMove;
        border.PreviewMouseLeftButtonUp += Header_OnPreviewMouseLeftButtonUp;
        border.PreviewMouseDown += Header_OnPreviewMouseDown;
        border.MouseRightButtonUp += Header_OnMouseRightButtonUp;
        border.LostMouseCapture += (_, _) => EndDrag(persist: _dragMoved);
        return new TabChrome(border, thumb, label, dirty);
    }

    private void UpdateTabVisuals()
    {
        foreach (var pair in _chrome)
        {
            var tab = pair.Key;
            var chrome = pair.Value;
            var active = ReferenceEquals(tab, _activeTab);
            chrome.Label.Text = tab.Name;
            chrome.Root.ToolTip = TabToolTip(tab);
            chrome.Root.Background = active
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 232, 232));
            chrome.Root.BorderBrush = active
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(126, 180, 234))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(208, 208, 208));
            var showDirty = tab.IsDirty || (ReferenceEquals(tab, _activeTab) && _cropLive);
            chrome.Dirty.Visibility = showDirty ? Visibility.Visible : Visibility.Collapsed;
            if (tab.Thumbnail is not null)
            {
                chrome.Thumb.Source = tab.Thumbnail;
            }

            if (active)
            {
                chrome.Root.BringIntoView();
            }
        }
    }

    private void Header_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || sender is not Border border || border.Tag is not EditorTab tab)
        {
            return;
        }

        e.Handled = true;
        CloseTab(tab);
    }

    private void Header_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not EditorTab tab || IsCloseSource(e.OriginalSource))
        {
            return;
        }

        if (!ReferenceEquals(tab, _activeTab) && !ActivateTab(tab))
        {
            return;
        }

        _dragTab = tab;
        _dragHeader = border;
        _tabDragOrigin = e.GetPosition(TabStrip);
        _dragMoved = false;
        border.CaptureMouse();
        e.Handled = true;
    }

    private void Header_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragHeader is null || _dragTab is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(TabStrip);
        if (!_dragMoved &&
            Math.Abs(position.X - _tabDragOrigin.X) < 6 &&
            Math.Abs(position.Y - _tabDragOrigin.Y) < 6)
        {
            return;
        }

        _dragMoved = true;
        var target = IndexAt(position.X);
        var current = _tabs.IndexOf(_dragTab);
        if (target < 0 || target == current)
        {
            return;
        }

        _tabs.RemoveAt(current);
        _tabs.Insert(target, _dragTab);
        TabStrip.Children.Remove(_dragHeader);
        TabStrip.Children.Insert(target, _dragHeader);
    }

    private void Header_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndDrag(persist: _dragMoved);
        e.Handled = true;
    }

    private void EndDrag(bool persist)
    {
        var header = _dragHeader;
        var moved = _dragMoved;
        _dragHeader = null;
        _dragTab = null;
        _dragMoved = false;
        if (header is not null && Mouse.Captured == header)
        {
            header.ReleaseMouseCapture();
        }

        if (!persist || !moved)
        {
            return;
        }

        try
        {
            PersistManifest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private CloseChoice AskSaveOrDiscard(EditorTab tab)
    {
        var save = new Forms.TaskDialogButton("Save");
        var discard = new Forms.TaskDialogButton("Discard");
        var cancel = new Forms.TaskDialogButton("Cancel");
        var page = new Forms.TaskDialogPage
        {
            Caption = "FrameIt Snap",
            Heading = "Save changes to " + tab.Name + " before closing?",
            Text = "Save writes the capture file. Discard closes the tab and drops unsaved edits. Session recovery files for this tab are removed either way. Files already in your capture folder stay where they are.",
            Icon = Forms.TaskDialogIcon.Warning,
            Buttons = { save, discard, cancel },
            DefaultButton = cancel
        };

        try
        {
            var result = Forms.TaskDialog.ShowDialog(new Win32Window(new WindowInteropHelper(this).Handle), page);
            if (result == save)
            {
                return CloseChoice.Save;
            }

            if (result == discard)
            {
                return CloseChoice.Discard;
            }

            return CloseChoice.Cancel;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or NotSupportedException)
        {
            var fallback = System.Windows.MessageBox.Show(
                this,
                "Save changes to " + tab.Name + " before closing?",
                "FrameIt Snap",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            return fallback switch
            {
                MessageBoxResult.Yes => CloseChoice.Save,
                MessageBoxResult.No => CloseChoice.Discard,
                _ => CloseChoice.Cancel
            };
        }
    }

    private void Header_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not EditorTab tab)
        {
            return;
        }

        e.Handled = true;
        EndDrag(persist: false);
        if (!ReferenceEquals(tab, _activeTab) && !ActivateTab(tab))
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MakeMenu("Close", () => CloseTab(tab)));
        menu.Items.Add(MakeMenu("Close Others", () => CloseOthers(tab)));
        menu.Items.Add(MakeMenu("Close All", CloseAllTabs));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenu("Duplicate Tab", () => DuplicateTab(tab)));
        border.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static MenuItem MakeMenu(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private int IndexAt(double x)
    {
        double cursor = 0;
        for (var index = 0; index < TabStrip.Children.Count; index++)
        {
            var child = (FrameworkElement)TabStrip.Children[index];
            var width = child.ActualWidth > 0 ? child.ActualWidth : child.DesiredSize.Width;
            if (width <= 0)
            {
                width = 80;
            }

            if (x < cursor + (width / 2))
            {
                return index;
            }

            cursor += width;
        }

        return Math.Max(0, TabStrip.Children.Count - 1);
    }

    private static bool IsCloseSource(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement element && Equals(element.Tag, "close"))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void RestoreNoticeBar_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        RestoreNoticeBar.Visibility = Visibility.Collapsed;
    }

    private void DisposeTabSessions()
    {
        _sessionTimer.Stop();
        if (_activeTab?.Session is not null)
        {
            _activeTab.Session.Changed -= OnSessionChanged;
        }

        foreach (var tab in _tabs)
        {
            tab.Session?.Dispose();
            tab.Session = null;
        }

        _patches.Clear();
        _previewBitmap?.Dispose();
        _previewBitmap = null;
    }

    private string NextCaptureName()
    {
        var name = "Capture " + _nextCaptureNumber;
        _nextCaptureNumber++;
        return name;
    }

    private void NoteCaptureNumber(string name)
    {
        const string prefix = "Capture ";
        if (name.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(prefix.Length), out var number))
        {
            _nextCaptureNumber = Math.Max(_nextCaptureNumber, number + 1);
        }
    }

    private static string TabToolTip(EditorTab tab)
    {
        return tab.Name + "\n" +
               tab.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
               SourceLabel(tab.Source);
    }

    private static string SourceLabel(CaptureMode source)
    {
        return source switch
        {
            CaptureMode.ActiveWindow => "Window",
            CaptureMode.FullScreen => "Full screen",
            CaptureMode.FixedRegion => "Fixed",
            _ => "Region"
        };
    }

    private static List<Annotation> CloneAnnotations(IEnumerable<Annotation> source)
    {
        return source.Select(item => item.Clone()).ToList();
    }

    private static List<Redaction> CloneRedactions(IEnumerable<Redaction> source)
    {
        return source.Select(item => item.Clone()).ToList();
    }

    private sealed class EditorTab
    {
        public EditorTab(string id, string name, CaptureMode source, DateTimeOffset createdUtc)
        {
            Id = id;
            Name = name;
            Source = source;
            CreatedUtc = createdUtc;
            Annotations = new List<Annotation>();
            Redactions = new List<Redaction>();
        }

        public string Id { get; }

        public string Name { get; }

        public CaptureMode Source { get; }

        public DateTimeOffset CreatedUtc { get; }

        public string? FilePath { get; set; }

        public bool Dirty { get; set; }

        public List<Annotation> Annotations { get; set; }

        public List<Redaction> Redactions { get; set; }

        public EditorSession? Session { get; set; }

        public BitmapSource? Thumbnail { get; set; }

        public bool IsDirty => Session?.IsDirty ?? Dirty;
    }

    private sealed class TabChrome
    {
        public TabChrome(Border root, System.Windows.Controls.Image thumb, TextBlock label, Ellipse dirty)
        {
            Root = root;
            Thumb = thumb;
            Label = label;
            Dirty = dirty;
        }

        public Border Root { get; }

        public System.Windows.Controls.Image Thumb { get; }

        public TextBlock Label { get; }

        public Ellipse Dirty { get; }
    }

    private enum CloseChoice
    {
        Save,
        Discard,
        Cancel
    }
}
