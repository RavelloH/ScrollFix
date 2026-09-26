using System.Diagnostics;
using System.Text;

namespace ScrollFix;

internal sealed class ScrollDashboardForm : Form
{
    private readonly Action<bool> _suppressionChanged;
    private readonly Action<bool> _startupChanged;
    private readonly Action<RollbackFilterSettings> _filterSettingsChanged;
    private readonly Action<bool> _chartStreamingChanged;
    private readonly System.Windows.Forms.Timer _statusTimer;

    private TabControl _tabControl = null!;
    private TabPage _chartPage = null!;
    private TabPage _statusPage = null!;
    private ScrollWaveformControl _waveformControl = null!;
    private CheckBox _suppressionCheckBox = null!;
    private CheckBox _startupCheckBox = null!;
    private NumericUpDown _reverseWindowInput = null!;
    private NumericUpDown _resetWindowInput = null!;
    private NumericUpDown _maxRollbackTicksInput = null!;
    private NumericUpDown _quickReverseInput = null!;
    private NumericUpDown _intentionalReverseInput = null!;
    private Label _currentStateValue = null!;
    private Label _modeValue = null!;
    private Label _eventCountValue = null!;
    private Label _rollbackCountValue = null!;
    private Label _errorRateValue = null!;
    private Label _filterValue = null!;
    private ToolStripStatusLabel _footerStatusLabel = null!;

    private bool _chartStreamingActive;
    private bool _isForeground;
    private bool _syncingUi;
    private bool _suppressionEnabled;
    private int _eventCount;
    private int _rollbackCount;
    private DateTimeOffset? _lastRollbackTime;

    private const double LogRetentionSeconds = 10D;
    private const int MaxLoggedActivities = 4096;
    private readonly Queue<LoggedActivity> _recentActivities = new();

    public ScrollDashboardForm(
        bool suppressionEnabled,
        bool startupEnabled,
        RollbackFilterSettings filterSettings,
        Action<bool> suppressionChanged,
        Action<bool> startupChanged,
        Action<RollbackFilterSettings> filterSettingsChanged,
        Action<bool> chartStreamingChanged)
    {
        _suppressionEnabled = suppressionEnabled;
        _suppressionChanged = suppressionChanged;
        _startupChanged = startupChanged;
        _filterSettingsChanged = filterSettingsChanged;
        _chartStreamingChanged = chartStreamingChanged;

        Text = Strings.Dashboard_Title;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 680);
        Size = new Size(1180, 820);
        BackColor = SystemColors.Control;
        ShowInTaskbar = true;

        FormClosing += ScrollDashboardForm_FormClosing;
        Resize += ScrollDashboardForm_Resize;
        Activated += ScrollDashboardForm_Activated;
        Deactivate += ScrollDashboardForm_Deactivate;
        VisibleChanged += ScrollDashboardForm_VisibleChanged;
        LocationChanged += ScrollDashboardForm_LocationChanged;

        WindowAppearance.Apply(this);
        BuildLayout();

        ApplySuppressionEnabled(suppressionEnabled);
        ApplyStartupEnabled(startupEnabled);
        ApplyFilterSettings(filterSettings);
        _statusTimer = new System.Windows.Forms.Timer
        {
            Interval = 400
        };
        _statusTimer.Tick += (_, _) =>
        {
            if (Visible && _tabControl.SelectedTab == _statusPage)
            {
                RefreshStatusLabels();
            }
        };
        _statusTimer.Start();
        UpdateStatusTimerState();
        RefreshStatusLabels();
    }

    public void AddWheelActivity(WheelActivityEventArgs activity)
    {
        _waveformControl.AddActivity(activity);
        AppendLog(activity);
    }

    public void ApplySuppressionEnabled(bool enabled)
    {
        _suppressionEnabled = enabled;
        SyncCheckBox(_suppressionCheckBox, enabled);
        RefreshStatusLabels();
    }

    public void ApplyStartupEnabled(bool enabled)
    {
        SyncCheckBox(_startupCheckBox, enabled);
        RefreshStatusLabels();
    }

    public void ApplyFilterSettings(RollbackFilterSettings settings)
    {
        RollbackFilterSettings normalized = settings.Normalize();

        _syncingUi = true;

        try
        {
            _reverseWindowInput.Value = normalized.ReverseWindowMs;
            _resetWindowInput.Value = normalized.ResetWindowMs;
            _maxRollbackTicksInput.Value = normalized.MaxRollbackTicks;
            _quickReverseInput.Value = normalized.QuickReverseMs;
            _intentionalReverseInput.Value = normalized.IntentionalReverseMs;
        }
        finally
        {
            _syncingUi = false;
        }

        RefreshStatusLabels();
    }

    public void ApplyRuntimeState(int eventCount, int rollbackCount, DateTimeOffset? lastRollbackTime)
    {
        _eventCount = eventCount;
        _rollbackCount = rollbackCount;
        _lastRollbackTime = lastRollbackTime;
        RefreshStatusLabels();
    }

    public void ShowPanel()
    {
        _isForeground = true;

        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        RefreshStatusLabels();
        Activate();
        BringToFront();
        UpdateWaveformRenderingState();
        UpdateStatusTimerState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_chartStreamingActive)
            {
                _chartStreamingActive = false;
                _chartStreamingChanged(false);
            }

            _statusTimer.Stop();
            _statusTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill
        };
        _chartPage = BuildChartPage();
        _statusPage = BuildStatusPage();
        _tabControl.TabPages.Add(_chartPage);
        _tabControl.TabPages.Add(_statusPage);
        _tabControl.TabPages.Add(BuildSettingsPage());
        _tabControl.TabPages.Add(BuildAboutPage());
        _tabControl.SelectedIndexChanged += (_, _) =>
        {
            RefreshStatusLabels();
            UpdateWaveformRenderingState();
            UpdateStatusTimerState();
        };

        StatusStrip statusStrip = new();
        _footerStatusLabel = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusStrip.Items.Add(_footerStatusLabel);

        Controls.Add(_tabControl);
        Controls.Add(statusStrip);
    }

    private TabPage BuildChartPage()
    {
        TabPage page = new(Strings.Tab_Chart)
        {
            UseVisualStyleBackColor = true,
            AutoScroll = true
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        GroupBox waveformGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = Strings.Chart_WaveformGroup
        };

        TableLayoutPanel waveformLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(8)
        };
        waveformLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _waveformControl = new ScrollWaveformControl
        {
            Dock = DockStyle.Fill
        };

        waveformLayout.Controls.Add(_waveformControl, 0, 0);
        waveformGroup.Controls.Add(waveformLayout);

        FlowLayoutPanel actionBar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };

        Button exportLogButton = new()
        {
            AutoSize = true,
            Text = Strings.Chart_ExportButton
        };
        exportLogButton.Click += (_, _) => ExportRecentLog();
        actionBar.Controls.Add(exportLogButton);

        layout.Controls.Add(waveformGroup, 0, 0);
        layout.Controls.Add(actionBar, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildStatusPage()
    {
        TabPage page = new(Strings.Tab_Status)
        {
            UseVisualStyleBackColor = true,
            AutoScroll = true
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 54F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 46F));
        layout.Controls.Add(BuildStatusSummaryGroup(), 0, 0);
        layout.Controls.Add(BuildStatusMetricsGroup(), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildStatusSummaryGroup()
    {
        GroupBox group = new()
        {
            Dock = DockStyle.Fill,
            Text = Strings.Status_OverviewGroup
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (int i = 0; i < 3; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        }

        _currentStateValue = CreateValueLabel();
        _modeValue = CreateValueLabel();
        _filterValue = CreateValueLabel();

        AddField(layout, 0, Strings.Status_FieldCurrentState, _currentStateValue);
        AddField(layout, 1, Strings.Status_FieldMode, _modeValue);
        AddField(layout, 2, Strings.Status_FieldCurrentRule, _filterValue);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildStatusMetricsGroup()
    {
        GroupBox group = new()
        {
            Dock = DockStyle.Fill,
            Text = Strings.Status_MetricsGroup
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (int i = 0; i < 3; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        }

        _eventCountValue = CreateValueLabel();
        _rollbackCountValue = CreateValueLabel();
        _errorRateValue = CreateValueLabel();

        AddField(layout, 0, Strings.Status_FieldEventCount, _eventCountValue);
        AddField(layout, 1, Strings.Status_FieldRollbackCount, _rollbackCountValue);
        AddField(layout, 2, Strings.Status_FieldErrorRate, _errorRateValue);

        group.Controls.Add(layout);
        return group;
    }

    private TabPage BuildSettingsPage()
    {
        TabPage page = new(Strings.Tab_Settings)
        {
            UseVisualStyleBackColor = true,
            AutoScroll = true
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(BuildBasicSettingsGroup(), 0, 0);
        layout.Controls.Add(BuildDetectionSettingsGroup(), 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private Control BuildBasicSettingsGroup()
    {
        GroupBox group = new()
        {
            Dock = DockStyle.Fill,
            Text = Strings.Settings_BasicGroup
        };

        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12),
            AutoScroll = true
        };

        _suppressionCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = Strings.Settings_EnableSuppression
        };
        _suppressionCheckBox.CheckedChanged += SuppressionCheckBox_CheckedChanged;

        _startupCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = Strings.Settings_AutoStart
        };
        _startupCheckBox.CheckedChanged += StartupCheckBox_CheckedChanged;

        panel.Controls.Add(_suppressionCheckBox);
        panel.Controls.Add(_startupCheckBox);
        group.Controls.Add(panel);
        return group;
    }

    private Control BuildDetectionSettingsGroup()
    {
        GroupBox group = new()
        {
            Dock = DockStyle.Fill,
            Text = Strings.Settings_DetectionGroup
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));

        for (int i = 0; i < 6; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        }

        _reverseWindowInput = CreateNumericInput(20, 500, 10);
        _resetWindowInput = CreateNumericInput(120, 3000, 50);
        _maxRollbackTicksInput = CreateNumericInput(1, 6, 1);
        _quickReverseInput = CreateNumericInput(10, 200, 5);
        _intentionalReverseInput = CreateNumericInput(60, 600, 10);

        _reverseWindowInput.ValueChanged += FilterInput_ValueChanged;
        _resetWindowInput.ValueChanged += FilterInput_ValueChanged;
        _maxRollbackTicksInput.ValueChanged += FilterInput_ValueChanged;
        _quickReverseInput.ValueChanged += FilterInput_ValueChanged;
        _intentionalReverseInput.ValueChanged += FilterInput_ValueChanged;

        Button resetDefaultsButton = new()
        {
            AutoSize = true,
            Text = Strings.Settings_ResetDefaults
        };
        resetDefaultsButton.Click += (_, _) =>
        {
            ApplyFilterSettings(RollbackFilterSettings.Default);
            CommitFilterSettings();
        };

        AddField(layout, 0, Strings.Settings_ReverseWindow, _reverseWindowInput);
        AddField(layout, 1, Strings.Settings_ResetWindow, _resetWindowInput);
        AddField(layout, 2, Strings.Settings_MaxRollbackTicks, _maxRollbackTicksInput);
        AddField(layout, 3, Strings.Settings_QuickReverse, _quickReverseInput);
        AddField(layout, 4, Strings.Settings_IntentionalReverse, _intentionalReverseInput);
        layout.Controls.Add(resetDefaultsButton, 1, 5);

        group.Controls.Add(layout);
        return group;
    }

    private TabPage BuildAboutPage()
    {
        TabPage page = new(Strings.Tab_About)
        {
            UseVisualStyleBackColor = true,
            AutoScroll = true
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));

        GroupBox infoGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = Strings.About_InfoGroup
        };

        TableLayoutPanel infoLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12)
        };
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (int i = 0; i < 5; i++)
        {
            infoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        }

        AddField(infoLayout, 0, Strings.About_FieldName, CreateValueLabel(AppInfo.Name));
        AddField(infoLayout, 1, Strings.About_FieldAuthor, CreateValueLabel(AppInfo.Author));
        AddField(infoLayout, 2, Strings.About_FieldEmail, CreateLinkLabel(AppInfo.Email, $"mailto:{AppInfo.Email}"));
        AddField(infoLayout, 3, Strings.About_FieldRepository, CreateLinkLabel(AppInfo.RepositoryUrl, AppInfo.RepositoryUrl));
        AddField(infoLayout, 4, Strings.About_FieldLicense, CreateValueLabel(AppInfo.License));
        infoGroup.Controls.Add(infoLayout);

        GroupBox descriptionGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = Strings.About_DescriptionGroup
        };

        TableLayoutPanel descriptionLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        descriptionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        descriptionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));

        Label descriptionBox = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = Strings.About_Description
        };

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 6, 0, 0)
        };

        Button hideButton = new()
        {
            AutoSize = true,
            Text = Strings.About_ClosePanel
        };
        hideButton.Click += (_, _) => Close();

        Button openRepoButton = new()
        {
            AutoSize = true,
            Text = Strings.About_OpenRepo
        };
        openRepoButton.Click += (_, _) => OpenLink(AppInfo.RepositoryUrl);

        actions.Controls.Add(hideButton);
        actions.Controls.Add(openRepoButton);

        descriptionLayout.Controls.Add(descriptionBox, 0, 0);
        descriptionLayout.Controls.Add(actions, 0, 1);
        descriptionGroup.Controls.Add(descriptionLayout);

        layout.Controls.Add(infoGroup, 0, 0);
        layout.Controls.Add(descriptionGroup, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private void ScrollDashboardForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _isForeground = false;
        UpdateWaveformRenderingState();
        UpdateStatusTimerState();
    }

    private void ScrollDashboardForm_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            _isForeground = false;
        }

        UpdateWaveformRenderingState();
        UpdateStatusTimerState();
    }

    private void ScrollDashboardForm_Activated(object? sender, EventArgs e)
    {
        _isForeground = true;
        UpdateWaveformRenderingState();
        UpdateStatusTimerState();
    }

    private void ScrollDashboardForm_Deactivate(object? sender, EventArgs e)
    {
        _isForeground = false;
        UpdateWaveformRenderingState();
        UpdateStatusTimerState();
    }

    private void ScrollDashboardForm_VisibleChanged(object? sender, EventArgs e)
    {
        UpdateWaveformRenderingState();
        UpdateStatusTimerState();
    }

    private void ScrollDashboardForm_LocationChanged(object? sender, EventArgs e)
    {
        if (_isForeground)
        {
            UpdateWaveformRenderingState();
        }
    }

    private void SuppressionCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (_syncingUi)
        {
            return;
        }

        _suppressionChanged(_suppressionCheckBox.Checked);
    }

    private void StartupCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (_syncingUi)
        {
            return;
        }

        _startupChanged(_startupCheckBox.Checked);
    }

    private void FilterInput_ValueChanged(object? sender, EventArgs e)
    {
        if (_syncingUi)
        {
            return;
        }

        CommitFilterSettings();
    }

    private void CommitFilterSettings()
    {
        RollbackFilterSettings settings = new(
            (uint)_reverseWindowInput.Value,
            (uint)_resetWindowInput.Value,
            (int)_maxRollbackTicksInput.Value,
            (uint)_quickReverseInput.Value,
            (uint)_intentionalReverseInput.Value);

        _filterSettingsChanged(settings.Normalize());
        RefreshStatusLabels();
    }

    private void RefreshStatusLabels()
    {
        bool recentRollback = _lastRollbackTime is not null &&
            DateTimeOffset.Now - _lastRollbackTime <= TimeSpan.FromSeconds(3);

        _currentStateValue.Text = recentRollback
            ? Strings.Status_RecentRollback(GetRollbackSuffix())
            : Strings.Status_NoRecentRollback;

        _modeValue.Text = _suppressionEnabled
            ? Strings.Status_ModeSuppress
            : Strings.Status_ModeDetectOnly;

        _eventCountValue.Text = _eventCount.ToString();
        _rollbackCountValue.Text = _rollbackCount.ToString();
        _errorRateValue.Text = _eventCount == 0
            ? "0.00%"
            : $"{(double)_rollbackCount / _eventCount:P2}";
        _filterValue.Text = Strings.Status_FilterSummary(
            _reverseWindowInput.Value,
            _resetWindowInput.Value,
            _maxRollbackTicksInput.Value,
            _quickReverseInput.Value,
            _intentionalReverseInput.Value);

        _footerStatusLabel.Text = recentRollback
            ? Strings.Footer_RollbackActive(_modeValue.Text)
            : Strings.Footer_Normal(_modeValue.Text);
    }

    private void UpdateWaveformRenderingState()
    {
        if (_waveformControl is null || _tabControl is null)
        {
            return;
        }

        bool shouldRender =
            _isForeground &&
            Visible &&
            WindowState != FormWindowState.Minimized &&
            _tabControl.SelectedTab == _chartPage;

        int refreshRate = shouldRender
            ? NativeMethods.GetDisplayRefreshRate(Screen.FromControl(this).DeviceName)
            : 0;

        _waveformControl.SetRenderingActive(shouldRender, refreshRate);

        if (_chartStreamingActive == shouldRender)
        {
            return;
        }

        _chartStreamingActive = shouldRender;
        _chartStreamingChanged(shouldRender);
    }

    private void UpdateStatusTimerState()
    {
        if (_statusTimer is null || _tabControl is null)
        {
            return;
        }

        bool shouldRun =
            _isForeground &&
            Visible &&
            WindowState != FormWindowState.Minimized &&
            _tabControl.SelectedTab == _statusPage;

        if (shouldRun)
        {
            if (!_statusTimer.Enabled)
            {
                _statusTimer.Start();
            }
        }
        else
        {
            _statusTimer.Stop();
        }
    }

    private string GetRollbackSuffix()
    {
        if (_lastRollbackTime is null)
        {
            return Strings.Status_SuffixNone;
        }

        double elapsedMs = (DateTimeOffset.Now - _lastRollbackTime.Value).TotalMilliseconds;
        return Strings.Status_SuffixAgo($"{elapsedMs:0}");
    }

    private void SyncCheckBox(CheckBox checkBox, bool value)
    {
        if (checkBox.Checked == value)
        {
            return;
        }

        _syncingUi = true;

        try
        {
            checkBox.Checked = value;
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void AppendLog(WheelActivityEventArgs activity)
    {
        long timestampMs = activity.ObservedAt.ToUnixTimeMilliseconds();
        _recentActivities.Enqueue(new LoggedActivity(
            timestampMs,
            activity.RawDelta,
            activity.CorrectedDelta,
            activity.EffectiveDelta,
            activity.IsRollback,
            activity.WasSuppressed,
            activity.ElapsedMs));
        TrimLog(timestampMs);

        while (_recentActivities.Count > MaxLoggedActivities)
        {
            _recentActivities.Dequeue();
        }
    }

    private void TrimLog(long nowMs)
    {
        long cutoffMs = nowMs - (long)Math.Round(LogRetentionSeconds * 1000D);

        while (_recentActivities.Count > 0 && _recentActivities.Peek().TimestampMs < cutoffMs)
        {
            _recentActivities.Dequeue();
        }
    }

    private void ExportRecentLog()
    {
        long nowMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        TrimLog(nowMs);

        if (_recentActivities.Count == 0)
        {
            MessageBox.Show(
                Strings.Export_Empty,
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        StringBuilder sb = new();
        sb.AppendLine(Strings.Export_HeaderTitle(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
        sb.AppendLine(Strings.Export_HeaderFilter(
            _reverseWindowInput.Value,
            _resetWindowInput.Value,
            _maxRollbackTicksInput.Value,
            _quickReverseInput.Value,
            _intentionalReverseInput.Value));
        sb.AppendLine(Strings.Export_HeaderSuppression(_suppressionEnabled));
        sb.AppendLine(Strings.Export_HeaderCounts(_eventCount, _rollbackCount));
        sb.AppendLine(Strings.Export_HeaderColumns);

        foreach (LoggedActivity entry in _recentActivities)
        {
            long relMs = entry.TimestampMs - nowMs;
            string absTime = DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampMs)
                .LocalDateTime.ToString("HH:mm:ss.fff");

            sb.Append(relMs).Append(',')
                .Append(absTime).Append(',')
                .Append(entry.RawDelta).Append(',')
                .Append(entry.CorrectedDelta).Append(',')
                .Append(entry.EffectiveDelta).Append(',')
                .Append(entry.IsRollback ? '1' : '0').Append(',')
                .Append(entry.WasSuppressed ? '1' : '0').Append(',')
                .Append(entry.ElapsedMs)
                .AppendLine();
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            MessageBox.Show(
                Strings.Export_Success(_recentActivities.Count),
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                Strings.Export_Failed(exception.Message),
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private readonly record struct LoggedActivity(
        long TimestampMs,
        int RawDelta,
        int CorrectedDelta,
        int EffectiveDelta,
        bool IsRollback,
        bool WasSuppressed,
        uint ElapsedMs);

    private static void AddField(TableLayoutPanel layout, int row, string labelText, Control valueControl)
    {
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"{labelText}{Strings.LabelSeparator}",
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, row);

        valueControl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        valueControl.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(valueControl, 1, row);
    }

    private static NumericUpDown CreateNumericInput(int minimum, int maximum, int increment)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            ThousandsSeparator = false,
            Width = 90
        };
    }

    private static Label CreateValueLabel(string? initialText = null)
    {
        return new Label
        {
            AutoSize = true,
            Text = initialText ?? string.Empty
        };
    }

    private static LinkLabel CreateLinkLabel(string text, string target)
    {
        LinkLabel linkLabel = new()
        {
            AutoSize = true,
            Text = text
        };

        linkLabel.LinkClicked += (_, _) => OpenLink(target);
        return linkLabel;
    }

    private static void OpenLink(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

}
