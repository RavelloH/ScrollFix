using System.Diagnostics;

namespace ScrollFix;

internal sealed class CaptureForm : Form
{
    private readonly WheelCaptureRecorder _recorder = new();
    private readonly ScrollWaveformControl _waveform = new();
    private readonly Queue<long> _recentEventTimes = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Label _stateLabel = new();
    private readonly Label _eventSummaryLabel = new();
    private readonly Label _filePathLabel = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();

    private Task? _operationTask;
    private Task? _closeTask;
    private long _verticalEventCount;
    private long _horizontalEventCount;
    private bool _allowClose;
    private bool _writerFailureHandled;

    public CaptureForm()
    {
        Text = "ScrollCapture - 滚轮事件记录器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 700);
        Size = new Size(1100, 820);
        BackColor = SystemColors.Control;

        BuildLayout();
        _waveform.SetShowCorrectedSeries(visible: false);
        _waveform.SetRenderingActive(active: true, refreshRateHz: 60);

        _recorder.WheelObserved += Recorder_WheelObserved;
        _startButton.Click += StartButton_Click;
        _stopButton.Click += StopButton_Click;
        FormClosing += CaptureForm_FormClosing;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();

        UpdateControls();
        UpdateEventSummary();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        Label title = new()
        {
            AutoSize = true,
            Text = "ScrollCapture",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 18, 0)
        };
        _stateLabel.AutoSize = true;
        _stateLabel.Text = "未捕捉";
        _stateLabel.ForeColor = SystemColors.GrayText;
        _stateLabel.Margin = new Padding(0, 12, 0, 0);
        header.Controls.Add(title);
        header.Controls.Add(_stateLabel);

        _eventSummaryLabel.AutoSize = true;
        _eventSummaryLabel.Dock = DockStyle.Fill;
        _eventSummaryLabel.Margin = new Padding(0, 0, 0, 10);

        _waveform.Dock = DockStyle.Fill;
        _waveform.Margin = new Padding(0, 0, 0, 12);

        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _filePathLabel.AutoSize = true;
        _filePathLabel.Dock = DockStyle.Fill;
        _filePathLabel.AutoEllipsis = true;
        _filePathLabel.Text = $"记录文件：{_recorder.CurrentFilePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScrollFix", "Captures")}";
        _filePathLabel.Margin = new Padding(0, 0, 0, 8);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        _startButton.Text = "开始捕捉";
        _startButton.AutoSize = true;
        _startButton.MinimumSize = new Size(120, 34);
        _stopButton.Text = "停止捕捉";
        _stopButton.AutoSize = true;
        _stopButton.MinimumSize = new Size(120, 34);
        Button openFolderButton = new()
        {
            Text = "打开记录文件夹",
            AutoSize = true,
            MinimumSize = new Size(150, 34)
        };
        openFolderButton.Click += OpenFolderButton_Click;
        buttons.Controls.Add(_startButton);
        buttons.Controls.Add(_stopButton);
        buttons.Controls.Add(openFolderButton);

        Label guidance = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "实时图表显示垂直滚轮 delta；文件也记录水平滚轮。仅捕捉滚轮，不记录键盘、鼠标移动或按键。文件含前台窗口标题、进程 ID 和高精度时间。",
            Margin = Padding.Empty
        };
        footer.Controls.Add(_filePathLabel, 0, 0);
        footer.Controls.Add(buttons, 0, 1);
        footer.Controls.Add(guidance, 0, 2);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_eventSummaryLabel, 0, 1);
        root.Controls.Add(_waveform, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        await RunOperationAsync(StartCaptureAsync);
    }

    private async void StopButton_Click(object? sender, EventArgs e)
    {
        await RunOperationAsync(StopCaptureAsync);
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (_operationTask is not null)
        {
            return;
        }

        UpdateControls(operationInProgress: true);
        Task operationTask = operation();
        _operationTask = operationTask;

        try
        {
            await operationTask;
        }
        finally
        {
            if (ReferenceEquals(_operationTask, operationTask))
            {
                _operationTask = null;
            }

            UpdateControls();
        }
    }

    private async Task StartCaptureAsync()
    {
        _waveform.SetRenderingActive(active: false, refreshRateHz: 60);
        _waveform.SetRenderingActive(active: true, refreshRateHz: 60);
        _recentEventTimes.Clear();
        _verticalEventCount = 0;
        _horizontalEventCount = 0;
        _stateLabel.Text = "正在启动…";
        _stateLabel.ForeColor = SystemColors.HotTrack;

        try
        {
            string path = await _recorder.StartCaptureAsync();
            _filePathLabel.Text = $"记录文件：{path}";
            _stateLabel.Text = "正在捕捉";
            _stateLabel.ForeColor = Color.FromArgb(35, 130, 75);
            _writerFailureHandled = false;
        }
        catch (Exception exception)
        {
            _stateLabel.Text = "启动失败";
            _stateLabel.ForeColor = Color.Firebrick;
            MessageBox.Show(this, exception.Message, "ScrollCapture", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        UpdateEventSummary();
        UpdateControls();
    }

    private async Task StopCaptureAsync()
    {
        _stateLabel.Text = "正在保存…";
        _stateLabel.ForeColor = SystemColors.HotTrack;

        try
        {
            await _recorder.StopCaptureAsync();
            _stateLabel.Text = "捕捉已停止";
            _stateLabel.ForeColor = SystemColors.GrayText;
        }
        catch (Exception exception)
        {
            _stateLabel.Text = "已停止，文件可能不完整";
            _stateLabel.ForeColor = Color.Firebrick;
            string message = exception.InnerException is null
                ? exception.Message
                : $"{exception.Message}{Environment.NewLine}{exception.InnerException.Message}";
            MessageBox.Show(this, message, "保存捕捉文件时出错", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        UpdateEventSummary();
        UpdateControls();
    }

    private void Recorder_WheelObserved(object? sender, WheelCaptureEventArgs e)
    {
        if (e.Sample.Axis == "vertical")
        {
            _verticalEventCount++;
            _waveform.AddRawDelta(e.Sample.ObservedAtUtc, e.Sample.Delta);
            _recentEventTimes.Enqueue(e.Sample.ObservedAtUtc.ToUnixTimeMilliseconds());
        }
        else
        {
            _horizontalEventCount++;
        }

        TrimRecentEvents(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        TrimRecentEvents(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        UpdateEventSummary();

        if (_recorder.IsCapturing && _recorder.WriterFailure is not null && !_writerFailureHandled)
        {
            _writerFailureHandled = true;
            await RunOperationAsync(StopCaptureAsync);
        }
    }

    private void UpdateEventSummary()
    {
        _eventSummaryLabel.Text =
            $"本次捕捉：{_recorder.ObservedEventCount:N0}（垂直 {_verticalEventCount:N0} / 水平 {_horizontalEventCount:N0}）    最近 10 秒垂直：{_recentEventTimes.Count:N0}    写入队列丢失：{_recorder.DroppedEventCount:N0}    回调错误：{_recorder.CallbackErrorCount:N0}";
    }

    private void UpdateControls(bool operationInProgress = false)
    {
        _startButton.Enabled = !operationInProgress && !_recorder.IsCapturing;
        _stopButton.Enabled = !operationInProgress && _recorder.IsCapturing;
    }

    private void TrimRecentEvents(long nowUnixMs)
    {
        long cutoff = nowUnixMs - 10_000;

        while (_recentEventTimes.Count > 0 && _recentEventTimes.Peek() < cutoff)
        {
            _recentEventTimes.Dequeue();
        }
    }

    private void OpenFolderButton_Click(object? sender, EventArgs e)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScrollFix",
                "Captures");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "无法打开记录文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void CaptureForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;

        if (_closeTask is not null)
        {
            return;
        }

        _closeTask = CloseSafelyAsync();
        await _closeTask;
    }

    private async Task CloseSafelyAsync()
    {
        _startButton.Enabled = false;
        _stopButton.Enabled = false;

        if (_operationTask is not null)
        {
            try
            {
                await _operationTask;
            }
            catch
            {
            }
        }

        if (_recorder.IsCapturing)
        {
            try
            {
                await _recorder.StopCaptureAsync();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "停止捕捉时出错", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        _refreshTimer.Stop();
        _recorder.WheelObserved -= Recorder_WheelObserved;
        _recorder.Dispose();
        _waveform.SetRenderingActive(active: false, refreshRateHz: 60);
        _allowClose = true;
        Close();
    }
}
