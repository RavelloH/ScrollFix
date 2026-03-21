using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;

namespace ScrollFix;

internal sealed class DashboardClientContext : ApplicationContext
{
    private readonly object _syncRoot = new();
    private readonly ScrollDashboardForm _dashboardForm;
    private readonly Control _uiDispatcher;
    private readonly string _pipeName;

    private NamedPipeClientStream? _pipeClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Channel<DashboardIpcMessage>? _sendChannel;
    private CancellationTokenSource? _sessionCts;
    private bool _disposed;

    public DashboardClientContext(string pipeName)
    {
        _pipeName = pipeName;
        _uiDispatcher = new Control();
        _uiDispatcher.CreateControl();

        _dashboardForm = new ScrollDashboardForm(
            suppressionEnabled: false,
            startupEnabled: false,
            filterSettings: RollbackFilterSettings.Default,
            suppressionChanged: enabled => Send(new DashboardIpcMessage
            {
                Kind = "set_suppression",
                SuppressionEnabled = enabled
            }),
            startupChanged: enabled => Send(new DashboardIpcMessage
            {
                Kind = "set_startup",
                StartupEnabled = enabled
            }),
            filterSettingsChanged: settings => Send(new DashboardIpcMessage
            {
                Kind = "set_filter",
                ReverseWindowMs = settings.ReverseWindowMs,
                ResetWindowMs = settings.ResetWindowMs,
                MaxRollbackTicks = settings.MaxRollbackTicks
            }),
            chartStreamingChanged: active => Send(new DashboardIpcMessage
            {
                Kind = "chart_streaming",
                ChartStreamingActive = active
            }));

        _dashboardForm.FormClosed += DashboardForm_FormClosed;
        MainForm = _dashboardForm;

        _ = Task.Run(ConnectAsync);
    }

    protected override void ExitThreadCore()
    {
        if (_disposed)
        {
            base.ExitThreadCore();
            return;
        }

        _disposed = true;
        _dashboardForm.FormClosed -= DashboardForm_FormClosed;
        DisposeConnection();
        _uiDispatcher.Dispose();
        base.ExitThreadCore();
    }

    private async Task ConnectAsync()
    {
        try
        {
            NamedPipeClientStream pipeClient = new(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipeClient.ConnectAsync(5000).ConfigureAwait(false);

            StreamReader reader = new(pipeClient, leaveOpen: true);
            StreamWriter writer = new(pipeClient, leaveOpen: true)
            {
                AutoFlush = true
            };
            Channel<DashboardIpcMessage> sendChannel = Channel.CreateUnbounded<DashboardIpcMessage>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            CancellationTokenSource sessionCts = new();

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    sessionCts.Dispose();
                    sendChannel.Writer.TryComplete();
                    reader.Dispose();
                    writer.Dispose();
                    pipeClient.Dispose();
                    return;
                }

                _pipeClient = pipeClient;
                _reader = reader;
                _writer = writer;
                _sendChannel = sendChannel;
                _sessionCts = sessionCts;
            }

            _ = Task.Run(() => WriterLoopAsync(sendChannel.Reader, writer, sessionCts.Token));
            _ = Task.Run(() => ReaderLoopAsync(reader, sessionCts.Token));
        }
        catch (Exception exception)
        {
            PostToUi(() =>
            {
                if (_disposed)
                {
                    return;
                }

                MessageBox.Show(
                    Strings.Error_ConnectFailed(exception.Message),
                    AppInfo.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                ExitThread();
            });
        }
    }

    private async Task ReaderLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (line is null)
                {
                    break;
                }

                DashboardIpcMessage? message = JsonSerializer.Deserialize<DashboardIpcMessage>(
                    line,
                    DashboardIpcProtocol.JsonOptions);

                if (message is null)
                {
                    continue;
                }

                PostToUi(() => ApplyMessage(message));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            PostToUi(() =>
            {
                if (!_disposed)
                {
                    ExitThread();
                }
            });
        }
    }

    private async Task WriterLoopAsync(
        ChannelReader<DashboardIpcMessage> reader,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (DashboardIpcMessage message in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                string payload = JsonSerializer.Serialize(message, DashboardIpcProtocol.JsonOptions);
                await writer.WriteLineAsync(payload).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void ApplyMessage(DashboardIpcMessage message)
    {
        switch (message.Kind)
        {
            case "snapshot":
                ApplySnapshot(message);
                break;

            case "state":
                _dashboardForm.ApplyRuntimeState(
                    message.EventCount ?? 0,
                    message.RollbackCount ?? 0,
                    message.GetLastRollbackTime());
                break;

            case "wheel":
                _dashboardForm.AddWheelActivity(new WheelActivityEventArgs(
                    message.RawDelta ?? 0,
                    message.CorrectedDelta ?? 0,
                    message.EffectiveDelta ?? 0,
                    message.IsRollback ?? false,
                    message.WasSuppressed ?? false,
                    message.ElapsedMs ?? 0,
                    message.GetObservedAt()));
                break;

            case "show":
                _dashboardForm.ShowPanel();
                break;
        }
    }

    private void ApplySnapshot(DashboardIpcMessage message)
    {
        _dashboardForm.ApplySuppressionEnabled(message.SuppressionEnabled ?? false);
        _dashboardForm.ApplyStartupEnabled(message.StartupEnabled ?? false);

        RollbackFilterSettings? filterSettings = message.GetFilterSettings();

        if (filterSettings is not null)
        {
            _dashboardForm.ApplyFilterSettings(filterSettings.Value);
        }

        _dashboardForm.ApplyRuntimeState(
            message.EventCount ?? 0,
            message.RollbackCount ?? 0,
            message.GetLastRollbackTime());
    }

    private void Send(DashboardIpcMessage message)
    {
        if (_disposed)
        {
            return;
        }

        Channel<DashboardIpcMessage>? sendChannel;

        lock (_syncRoot)
        {
            sendChannel = _sendChannel;
        }

        sendChannel?.Writer.TryWrite(message);
    }

    private void DashboardForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        ExitThread();
    }

    private void DisposeConnection()
    {
        NamedPipeClientStream? pipeClient;
        StreamReader? reader;
        StreamWriter? writer;
        Channel<DashboardIpcMessage>? sendChannel;
        CancellationTokenSource? sessionCts;

        lock (_syncRoot)
        {
            pipeClient = _pipeClient;
            reader = _reader;
            writer = _writer;
            sendChannel = _sendChannel;
            sessionCts = _sessionCts;

            _pipeClient = null;
            _reader = null;
            _writer = null;
            _sendChannel = null;
            _sessionCts = null;
        }

        sendChannel?.Writer.TryComplete();

        if (sessionCts is not null)
        {
            try
            {
                sessionCts.Cancel();
            }
            catch
            {
            }

            sessionCts.Dispose();
        }

        reader?.Dispose();

        try
        {
            writer?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }

        pipeClient?.Dispose();
    }

    private void PostToUi(Action action)
    {
        if (_disposed || _uiDispatcher.IsDisposed)
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
