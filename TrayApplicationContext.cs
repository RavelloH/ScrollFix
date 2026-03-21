namespace ScrollFix;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int AlertDurationMs = 1200;

    private readonly AppSettings _settings;
    private readonly AppSettingsStore _settingsStore;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly MouseWheelHook _mouseWheelHook;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _suppressionMenuItem;
    private readonly ToolStripMenuItem _startupMenuItem;
    private readonly System.Windows.Forms.Timer _alertTimer;
    private readonly Icon _normalIcon;
    private readonly Icon _alertIcon;
    private readonly Icon _disabledIcon;
    private readonly Control _uiDispatcher;
    private readonly DashboardProcessHost _dashboardHost;

    private bool _alertActive;
    private bool _lastRollbackSuppressed;
    private bool _startupEnabled;
    private int _eventCount;
    private int _rollbackCount;
    private DateTimeOffset? _lastRollbackTime;

    public TrayApplicationContext()
    {
        _settingsStore = new AppSettingsStore(AppInfo.Name);
        _settings = _settingsStore.Load();
        _settings.ApplyRollbackFilterSettings(_settings.GetRollbackFilterSettings());

        _startupRegistrationService = new StartupRegistrationService(AppInfo.Name);
        _startupEnabled = _startupRegistrationService.IsEnabled();

        _uiDispatcher = new Control();
        _uiDispatcher.CreateControl();

        _normalIcon = TrayIconFactory.CreateWheelIcon(Color.FromArgb(46, 127, 229));
        _alertIcon = TrayIconFactory.CreateWheelIcon(Color.FromArgb(224, 82, 62));
        _disabledIcon = TrayIconFactory.CreateWheelIcon(Color.FromArgb(145, 145, 145));

        _statusMenuItem = new ToolStripMenuItem
        {
            Enabled = false
        };

        ToolStripMenuItem openPanelMenuItem = new(Strings.TrayMenu_OpenPanel);
        openPanelMenuItem.Click += (_, _) => ShowDashboard();

        _suppressionMenuItem = new ToolStripMenuItem(Strings.TrayMenu_EnableSuppression)
        {
            CheckOnClick = true,
            Checked = _settings.SuppressionEnabled
        };
        _suppressionMenuItem.Click += (_, _) => SetSuppressionEnabled(!_settings.SuppressionEnabled);

        _startupMenuItem = new ToolStripMenuItem(Strings.TrayMenu_AutoStart)
        {
            CheckOnClick = true,
            Checked = _startupEnabled
        };
        _startupMenuItem.Click += (_, _) => SetStartupEnabled(!_startupEnabled);

        ToolStripMenuItem exitMenuItem = new(Strings.TrayMenu_Exit);
        exitMenuItem.Click += (_, _) => ExitThread();

        ContextMenuStrip menu = new();
        menu.Items.AddRange(
        [
            openPanelMenuItem,
            _statusMenuItem,
            new ToolStripSeparator(),
            _suppressionMenuItem,
            _startupMenuItem,
            new ToolStripSeparator(),
            exitMenuItem
        ]);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowDashboard();

        _alertTimer = new System.Windows.Forms.Timer
        {
            Interval = AlertDurationMs
        };
        _alertTimer.Tick += AlertTimer_Tick;

        _dashboardHost = new DashboardProcessHost(
            GetDashboardSnapshot,
            enabled => ExecuteOnUiThread(() => SetSuppressionEnabled(enabled)),
            enabled => ExecuteOnUiThread(() => SetStartupEnabled(enabled)),
            settings => ExecuteOnUiThread(() => SetRollbackFilterSettings(settings)));

        _mouseWheelHook = new MouseWheelHook
        {
            SuppressionEnabled = _settings.SuppressionEnabled
        };
        _mouseWheelHook.UpdateFilterSettings(_settings.GetRollbackFilterSettings());
        _mouseWheelHook.WheelActivity += MouseWheelHook_WheelActivity;
        _mouseWheelHook.RollbackDetected += MouseWheelHook_RollbackDetected;
        _mouseWheelHook.Start();

        RefreshVisualState();
    }

    protected override void ExitThreadCore()
    {
        _mouseWheelHook.WheelActivity -= MouseWheelHook_WheelActivity;
        _mouseWheelHook.RollbackDetected -= MouseWheelHook_RollbackDetected;
        _mouseWheelHook.Dispose();

        _alertTimer.Stop();
        _alertTimer.Dispose();

        _dashboardHost.Dispose();
        _uiDispatcher.Dispose();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        _normalIcon.Dispose();
        _alertIcon.Dispose();
        _disabledIcon.Dispose();

        base.ExitThreadCore();
    }

    private void MouseWheelHook_WheelActivity(object? sender, WheelActivityEventArgs e)
    {
        _eventCount++;

        if (e.IsRollback)
        {
            _rollbackCount++;
        }

        _dashboardHost.PublishRuntimeState(_eventCount, _rollbackCount, _lastRollbackTime);
        _dashboardHost.PublishWheelActivity(e);
    }

    private void MouseWheelHook_RollbackDetected(object? sender, RollbackDetectedEventArgs e)
    {
        _lastRollbackTime = e.ObservedAt;
        _lastRollbackSuppressed = e.Suppressed;
        _alertActive = true;

        _alertTimer.Stop();
        _alertTimer.Start();

        _dashboardHost.PublishRuntimeState(_eventCount, _rollbackCount, _lastRollbackTime);
        RefreshVisualState();
    }

    private void AlertTimer_Tick(object? sender, EventArgs e)
    {
        _alertTimer.Stop();
        _alertActive = false;
        RefreshVisualState();
    }

    private void SetSuppressionEnabled(bool enabled)
    {
        if (_settings.SuppressionEnabled == enabled)
        {
            return;
        }

        _settings.SuppressionEnabled = enabled;
        _settingsStore.Save(_settings);
        _mouseWheelHook.SuppressionEnabled = enabled;
        SyncMenuState(_suppressionMenuItem, enabled);
        _dashboardHost.PublishSnapshot(GetDashboardSnapshot());
        RefreshVisualState();
    }

    private void SetStartupEnabled(bool enabled)
    {
        if (_startupEnabled == enabled)
        {
            return;
        }

        try
        {
            _startupRegistrationService.SetEnabled(enabled);
            _startupEnabled = enabled;
            SyncMenuState(_startupMenuItem, enabled);
            _dashboardHost.PublishSnapshot(GetDashboardSnapshot());
            RefreshVisualState();
        }
        catch (Exception exception)
        {
            SyncMenuState(_startupMenuItem, _startupEnabled);
            _dashboardHost.PublishSnapshot(GetDashboardSnapshot());

            MessageBox.Show(
                Strings.Error_UpdateAutoStartFailed(exception.Message),
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void SetRollbackFilterSettings(RollbackFilterSettings settings)
    {
        RollbackFilterSettings normalized = settings.Normalize();
        _settings.ApplyRollbackFilterSettings(normalized);
        _settingsStore.Save(_settings);
        _mouseWheelHook.UpdateFilterSettings(normalized);
        _dashboardHost.PublishSnapshot(GetDashboardSnapshot());
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        _notifyIcon.Icon = GetCurrentIcon();
        _notifyIcon.Text = GetTooltipText();
        _statusMenuItem.Text = GetStatusText();
    }

    private Icon GetCurrentIcon()
    {
        if (_alertActive)
        {
            return _alertIcon;
        }

        return _settings.SuppressionEnabled ? _normalIcon : _disabledIcon;
    }

    private string GetTooltipText()
    {
        if (_alertActive)
        {
            return Strings.Tooltip_RollbackDetected;
        }

        return _settings.SuppressionEnabled
            ? Strings.Tooltip_SuppressionOn
            : Strings.Tooltip_DetectOnly;
    }

    private string GetStatusText()
    {
        if (_lastRollbackTime is null)
        {
            return _settings.SuppressionEnabled
                ? Strings.Status_MonitoringWithSuppression
                : Strings.Status_MonitoringDetectOnly;
        }

        string action = _lastRollbackSuppressed ? Strings.Status_Suppressed : Strings.Status_NotSuppressed;
        return Strings.Status_RollbackDetectedAt($"{_lastRollbackTime:HH:mm:ss}", action);
    }

    private static void SyncMenuState(ToolStripMenuItem menuItem, bool value)
    {
        menuItem.Checked = value;
    }

    private void ShowDashboard()
    {
        _dashboardHost.ShowOrLaunch();
    }

    private DashboardSnapshot GetDashboardSnapshot()
    {
        return new DashboardSnapshot(
            _settings.SuppressionEnabled,
            _startupEnabled,
            _settings.GetRollbackFilterSettings(),
            _eventCount,
            _rollbackCount,
            _lastRollbackTime);
    }

    private void ExecuteOnUiThread(Action action)
    {
        if (_uiDispatcher.IsDisposed)
        {
            return;
        }

        if (_uiDispatcher.IsHandleCreated)
        {
            _uiDispatcher.BeginInvoke(action);
            return;
        }

        action();
    }
}
