using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace ScrollFix;

internal sealed class WheelCaptureRecorder : IDisposable
{
    private const int QueueCapacity = 32_768;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _captureDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScrollFix",
        "Captures");

    private readonly WheelCaptureHook _hook = new();

    private Channel<CaptureRecord>? _channel;
    private Task? _writerTask;
    private string? _currentFilePath;
    private string? _sessionId;
    private long _sessionStartStopwatch;
    private long _lastEventStopwatch;
    private long _observedEventCount;
    private long _writtenEventCount;
    private long _droppedEventCount;
    private long _callbackErrorCount;
    private int _isCapturing;
    private Exception? _writerFailure;
    private bool _disposed;

    public WheelCaptureRecorder()
    {
        _hook.WheelObserved += Hook_WheelObserved;
    }

    public event EventHandler<WheelCaptureEventArgs>? WheelObserved;

    public bool IsCapturing => Volatile.Read(ref _isCapturing) == 1;

    public string? CurrentFilePath => _currentFilePath;

    public long ObservedEventCount => Interlocked.Read(ref _observedEventCount);

    public long WrittenEventCount => Interlocked.Read(ref _writtenEventCount);

    public long DroppedEventCount => Interlocked.Read(ref _droppedEventCount);

    public long CallbackErrorCount => Interlocked.Read(ref _callbackErrorCount);

    public Exception? WriterFailure => Volatile.Read(ref _writerFailure);

    public async Task<string> StartCaptureAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsCapturing || (_writerTask is not null && !_writerTask.IsCompleted))
        {
            throw new InvalidOperationException("当前捕捉会话尚未结束。");
        }

        Directory.CreateDirectory(_captureDirectory);

        _sessionId = Guid.NewGuid().ToString("N");
        string fileName = $"scroll-wheel-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-{_sessionId[..8]}.jsonl";
        _currentFilePath = Path.Combine(_captureDirectory, fileName);

        FileStream fileStream = new(
            _currentFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        StreamWriter writer = new(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Channel<CaptureRecord> channel = Channel.CreateBounded<CaptureRecord>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        _channel = channel;
        _observedEventCount = 0;
        _writtenEventCount = 0;
        _droppedEventCount = 0;
        _callbackErrorCount = 0;
        _writerFailure = null;
        _sessionStartStopwatch = Stopwatch.GetTimestamp();
        _lastEventStopwatch = 0;
        _writerTask = WriteLoopAsync(channel.Reader, writer);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        channel.Writer.TryWrite(new CaptureRecord
        {
            RecordType = "session_start",
            SessionId = _sessionId,
            TimestampUtc = FormatTimestamp(startedAt),
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
            OperatingSystem = Environment.OSVersion.VersionString,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            StopwatchFrequency = Stopwatch.Frequency,
            TimeZone = TimeZoneInfo.Local.Id,
            CapturedMessages = ["WM_MOUSEWHEEL", "WM_MOUSEHWHEEL"]
        });

        Volatile.Write(ref _isCapturing, 1);

        try
        {
            _hook.Start();
        }
        catch
        {
            Volatile.Write(ref _isCapturing, 0);
            channel.Writer.TryComplete();
            await _writerTask.ConfigureAwait(false);
            _channel = null;
            _writerTask = null;
            throw;
        }

        return _currentFilePath;
    }

    public async Task StopCaptureAsync()
    {
        if (!IsCapturing)
        {
            if (_writerTask is not null)
            {
                await _writerTask.ConfigureAwait(false);
            }

            return;
        }

        Volatile.Write(ref _isCapturing, 0);
        Exception? hookStopFailure = null;

        try
        {
            _hook.Stop();
        }
        catch (Exception exception)
        {
            hookStopFailure = exception;
        }

        Channel<CaptureRecord>? channel = _channel;

        if (channel is not null)
        {
            if (WriterFailure is null)
            {
                try
                {
                    await channel.Writer.WriteAsync(new CaptureRecord
                    {
                        RecordType = "session_end",
                        SessionId = _sessionId,
                        TimestampUtc = FormatTimestamp(DateTimeOffset.UtcNow),
                        ObservedEventCount = ObservedEventCount,
                        WrittenEventCount = WrittenEventCount,
                        DroppedEventCount = DroppedEventCount,
                        CallbackErrorCount = CallbackErrorCount
                    }).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                }
            }

            channel.Writer.TryComplete();
        }

        if (_writerTask is not null)
        {
            await _writerTask.ConfigureAwait(false);
        }

        _channel = null;
        _writerTask = null;

        if (hookStopFailure is not null)
        {
            throw new InvalidOperationException("捕捉已停止，但卸载鼠标钩子时发生错误。", hookStopFailure);
        }

        if (WriterFailure is Exception writerFailure)
        {
            throw new IOException("写入捕捉文件失败，当前文件可能不完整。", writerFailure);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _hook.WheelObserved -= Hook_WheelObserved;
        _hook.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void Hook_WheelObserved(object? sender, WheelCaptureEventArgs e)
    {
        if (!IsCapturing)
        {
            return;
        }

        try
        {
            WheelCaptureSample sample = e.Sample;
            long sequence = Interlocked.Increment(ref _observedEventCount);
            long previousEventTick = Interlocked.Exchange(ref _lastEventStopwatch, sample.StopwatchTimestamp);
            long elapsedUs = ToMicroseconds(sample.StopwatchTimestamp - _sessionStartStopwatch);
            long? sincePreviousUs = previousEventTick == 0
                ? null
                : ToMicroseconds(sample.StopwatchTimestamp - previousEventTick);

            CaptureRecord record = new()
            {
                RecordType = "wheel_event",
                SessionId = _sessionId,
                Sequence = sequence,
                TimestampUtc = FormatTimestamp(sample.ObservedAtUtc),
                ElapsedMicroseconds = elapsedUs,
                SincePreviousMicroseconds = sincePreviousUs,
                SystemEventTimeMs = sample.SystemEventTimeMs,
                Axis = sample.Axis,
                Delta = sample.Delta,
                WheelTicks = sample.Delta / 120D,
                X = sample.X,
                Y = sample.Y,
                HookFlags = $"0x{sample.Flags:X8}",
                IsInjected = (sample.Flags & 0x00000001) != 0,
                IsLowerIntegrityInjected = (sample.Flags & 0x00000002) != 0,
                MouseData = $"0x{sample.MouseData:X8}",
                ExtraInfo = $"0x{(ulong)sample.ExtraInfo:X}",
                ForegroundWindow = $"0x{unchecked((ulong)sample.ForegroundWindowHandle):X}",
                ForegroundProcessId = sample.ForegroundProcessId,
                ForegroundWindowTitle = sample.ForegroundWindowTitle
            };

            Channel<CaptureRecord>? channel = _channel;

            if (channel is null || !channel.Writer.TryWrite(record))
            {
                Interlocked.Increment(ref _droppedEventCount);
            }
            else
            {
                Interlocked.Increment(ref _writtenEventCount);
            }

            try
            {
                WheelObserved?.Invoke(this, e);
            }
            catch
            {
                Interlocked.Increment(ref _callbackErrorCount);
            }
        }
        catch
        {
            Interlocked.Increment(ref _callbackErrorCount);
        }
    }

    private async Task WriteLoopAsync(ChannelReader<CaptureRecord> reader, StreamWriter writer)
    {
        int pendingLines = 0;
        DateTime lastFlushUtc = DateTime.UtcNow;

        try
        {
            await foreach (CaptureRecord record in reader.ReadAllAsync().ConfigureAwait(false))
            {
                string json = JsonSerializer.Serialize(record, JsonOptions);
                await writer.WriteLineAsync(json).ConfigureAwait(false);
                pendingLines++;

                if (pendingLines >= 64 || (DateTime.UtcNow - lastFlushUtc).TotalSeconds >= 1D)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    pendingLines = 0;
                    lastFlushUtc = DateTime.UtcNow;
                }
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _writerFailure, exception);
        }
        finally
        {
            try
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _writerFailure, exception);
            }
        }
    }

    private static long ToMicroseconds(long stopwatchTicks)
    {
        return (long)Math.Round(stopwatchTicks * (1_000_000D / Stopwatch.Frequency));
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private sealed class CaptureRecord
    {
        public required string RecordType { get; init; }
        public int SchemaVersion { get; init; } = 1;
        public string? SessionId { get; init; }
        public string? TimestampUtc { get; init; }
        public string? AppVersion { get; init; }
        public string? OperatingSystem { get; init; }
        public string? Runtime { get; init; }
        public long? StopwatchFrequency { get; init; }
        public string? TimeZone { get; init; }
        public string[]? CapturedMessages { get; init; }
        public long? Sequence { get; init; }
        public long? ElapsedMicroseconds { get; init; }
        public long? SincePreviousMicroseconds { get; init; }
        public uint? SystemEventTimeMs { get; init; }
        public string? Axis { get; init; }
        public int? Delta { get; init; }
        public double? WheelTicks { get; init; }
        public int? X { get; init; }
        public int? Y { get; init; }
        public string? HookFlags { get; init; }
        public bool? IsInjected { get; init; }
        public bool? IsLowerIntegrityInjected { get; init; }
        public string? MouseData { get; init; }
        public string? ExtraInfo { get; init; }
        public string? ForegroundWindow { get; init; }
        public uint? ForegroundProcessId { get; init; }
        public string? ForegroundWindowTitle { get; init; }
        public long? ObservedEventCount { get; init; }
        public long? WrittenEventCount { get; init; }
        public long? DroppedEventCount { get; init; }
        public long? CallbackErrorCount { get; init; }
    }
}
