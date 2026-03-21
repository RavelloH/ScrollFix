using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;

namespace ScrollFix;

internal sealed class DashboardProcessHost : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Func<DashboardSnapshot> _snapshotProvider;
    private readonly Action<bool> _suppressionChanged;
    private readonly Action<bool> _startupChanged;
    private readonly Action<RollbackFilterSettings> _filterSettingsChanged;

    private Process? _dashboardProcess;
    private NamedPipeServerStream? _pipeServer;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Channel<DashboardIpcMessage>? _sendChannel;
    private CancellationTokenSource? _sessionCts;
    private bool _disposed;
    private int _chartStreamingActive;

    public DashboardProcessHost(
        Func<DashboardSnapshot> snapshotProvider,
        Action<bool> suppressionChanged,
        Action<bool> startupChanged,
        Action<RollbackFilterSettings> filterSettingsChanged)
    {
        _snapshotProvider = snapshotProvider;
        _suppressionChanged = suppressionChanged;
        _startupChanged = startupChanged;
        _filterSettingsChanged = filterSettingsChanged;
    }

    public bool IsChartStreamingActive => Volatile.Read(ref _chartStreamingActive) == 1;

    public void ShowOrLaunch()
    {
        ThrowIfDisposed();

        bool shouldLaunch = false;
        Process? staleProcess = null;

        lock (_syncRoot)
        {
            CleanupExitedProcess_NoLock();

            if (_sendChannel is not null)
            {
                _sendChannel.Writer.TryWrite(new DashboardIpcMessage
                {
                    Kind = "show"
                });
                return;
            }

            if (_dashboardProcess is not null && !_dashboardProcess.HasExited)
            {
                if (_pipeServer is not null)
                {
                    return;
                }

                staleProcess = _dashboardProcess;
                _dashboardProcess = null;
            }

            shouldLaunch = true;
        }

        if (staleProcess is not null)
        {
            TerminateProcess(staleProcess);
        }

        if (shouldLaunch)
        {
            LaunchDashboardProcess();
        }
    }

    public void PublishSnapshot(DashboardSnapshot snapshot)
    {
        Enqueue(CreateSnapshotMessage(snapshot));
    }

    public void PublishRuntimeState(int eventCount, int rollbackCount, DateTimeOffset? lastRollbackTime)
    {
        Enqueue(new DashboardIpcMessage
        {
            Kind = "state",
            EventCount = eventCount,
            RollbackCount = rollbackCount,
            LastRollbackUnixMs = lastRollbackTime?.ToUnixTimeMilliseconds()
        });
    }

    public void PublishWheelActivity(WheelActivityEventArgs activity)
    {
        if (!IsChartStreamingActive)
        {
            return;
        }

        Enqueue(new DashboardIpcMessage
        {
            Kind = "wheel",
            RawDelta = activity.RawDelta,
            CorrectedDelta = activity.CorrectedDelta,
            EffectiveDelta = activity.EffectiveDelta,
            IsRollback = activity.IsRollback,
            WasSuppressed = activity.WasSuppressed,
            ElapsedMs = activity.ElapsedMs,
            ObservedUnixMs = activity.ObservedAt.ToUnixTimeMilliseconds()
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Process? process;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            process = _dashboardProcess;
            _dashboardProcess = null;
        }

        HandleConnectionClosed();

        if (process is not null)
        {
            process.Exited -= DashboardProcess_Exited;

            try
            {
                if (!process.HasExited)
                {
                    process.WaitForExit(800);
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void LaunchDashboardProcess()
    {
        string pipeName = $"ScrollFix.Dashboard.{Environment.ProcessId}.{Guid.NewGuid():N}";
        NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        CancellationTokenSource sessionCts = new();

        lock (_syncRoot)
        {
            if (_disposed)
            {
                sessionCts.Dispose();
                server.Dispose();
                return;
            }

            _pipeServer = server;
            _sessionCts = sessionCts;
        }

        _ = Task.Run(() => AcceptConnectionAsync(server, sessionCts.Token));

        Process? process = null;

        try
        {
            ProcessStartInfo startInfo = new(Application.ExecutablePath)
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--dashboard");
            startInfo.ArgumentList.Add(pipeName);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(Strings.Error_LaunchDashboardFailed);
            process.EnableRaisingEvents = true;
            process.Exited += DashboardProcess_Exited;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    process.Exited -= DashboardProcess_Exited;
                    TerminateProcess(process);
                    return;
                }

                _dashboardProcess = process;
            }
        }
        catch
        {
            HandleConnectionClosed(server);
            process?.Dispose();
            throw;
        }
    }

    private async Task AcceptConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        try
        {
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

            StreamReader reader = new(server);
            StreamWriter writer = new(server)
            {
                AutoFlush = true
            };
            Channel<DashboardIpcMessage> sendChannel = Channel.CreateUnbounded<DashboardIpcMessage>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

            bool accepted;

            lock (_syncRoot)
            {
                accepted = !_disposed && ReferenceEquals(_pipeServer, server);

                if (accepted)
                {
                    _reader = reader;
                    _writer = writer;
                    _sendChannel = sendChannel;
                }
            }

            if (!accepted)
            {
                sendChannel.Writer.TryComplete();
                reader.Dispose();
                writer.Dispose();
                return;
            }

            _ = Task.Run(() => WriterLoopAsync(sendChannel.Reader, writer, cancellationToken));
            _ = Task.Run(() => ReaderLoopAsync(reader, cancellationToken));

            PublishSnapshot(_snapshotProvider());
            Enqueue(new DashboardIpcMessage
            {
                Kind = "show"
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            HandleConnectionClosed(server);
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

                HandleIncomingMessage(message);
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
            HandleConnectionClosed();
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
            HandleConnectionClosed();
        }
    }

    private void HandleIncomingMessage(DashboardIpcMessage message)
    {
        switch (message.Kind)
        {
            case "set_suppression" when message.SuppressionEnabled is bool suppressionEnabled:
                _suppressionChanged(suppressionEnabled);
                break;

            case "set_startup" when message.StartupEnabled is bool startupEnabled:
                _startupChanged(startupEnabled);
                break;

            case "set_filter":
                RollbackFilterSettings? filterSettings = message.GetFilterSettings();

                if (filterSettings is not null)
                {
                    _filterSettingsChanged(filterSettings.Value);
                }
                break;

            case "chart_streaming" when message.ChartStreamingActive is bool chartStreamingActive:
                Volatile.Write(ref _chartStreamingActive, chartStreamingActive ? 1 : 0);
                break;
        }
    }

    private void HandleConnectionClosed(NamedPipeServerStream? expectedServer = null)
    {
        NamedPipeServerStream? pipeServer;
        StreamReader? reader;
        StreamWriter? writer;
        Channel<DashboardIpcMessage>? sendChannel;
        CancellationTokenSource? sessionCts;

        lock (_syncRoot)
        {
            if (expectedServer is not null && _pipeServer is not null && !ReferenceEquals(_pipeServer, expectedServer))
            {
                return;
            }

            pipeServer = _pipeServer;
            reader = _reader;
            writer = _writer;
            sendChannel = _sendChannel;
            sessionCts = _sessionCts;

            _pipeServer = null;
            _reader = null;
            _writer = null;
            _sendChannel = null;
            _sessionCts = null;
        }

        Volatile.Write(ref _chartStreamingActive, 0);
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
        writer?.Dispose();
        pipeServer?.Dispose();
    }

    private void Enqueue(DashboardIpcMessage message)
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

    private static DashboardIpcMessage CreateSnapshotMessage(DashboardSnapshot snapshot)
    {
        return new DashboardIpcMessage
        {
            Kind = "snapshot",
            SuppressionEnabled = snapshot.SuppressionEnabled,
            StartupEnabled = snapshot.StartupEnabled,
            ReverseWindowMs = snapshot.FilterSettings.ReverseWindowMs,
            ResetWindowMs = snapshot.FilterSettings.ResetWindowMs,
            MaxRollbackTicks = snapshot.FilterSettings.MaxRollbackTicks,
            EventCount = snapshot.EventCount,
            RollbackCount = snapshot.RollbackCount,
            LastRollbackUnixMs = snapshot.LastRollbackTime?.ToUnixTimeMilliseconds()
        };
    }

    private void DashboardProcess_Exited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
        {
            return;
        }

        process.Exited -= DashboardProcess_Exited;

        lock (_syncRoot)
        {
            if (ReferenceEquals(_dashboardProcess, process))
            {
                _dashboardProcess = null;
            }
        }

        process.Dispose();
        HandleConnectionClosed();
    }

    private void CleanupExitedProcess_NoLock()
    {
        if (_dashboardProcess is null || !_dashboardProcess.HasExited)
        {
            return;
        }

        _dashboardProcess.Exited -= DashboardProcess_Exited;
        _dashboardProcess.Dispose();
        _dashboardProcess = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DashboardProcessHost));
        }
    }

    private static void TerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(800);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
