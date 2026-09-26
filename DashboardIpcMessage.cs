using System.Text.Json;

namespace ScrollFix;

internal sealed class DashboardIpcMessage
{
    public string Kind { get; init; } = string.Empty;

    public bool? SuppressionEnabled { get; init; }

    public bool? StartupEnabled { get; init; }

    public uint? ReverseWindowMs { get; init; }

    public uint? ResetWindowMs { get; init; }

    public int? MaxRollbackTicks { get; init; }

    public uint? QuickReverseMs { get; init; }

    public uint? IntentionalReverseMs { get; init; }

    public int? EventCount { get; init; }

    public int? RollbackCount { get; init; }

    public long? LastRollbackUnixMs { get; init; }

    public int? RawDelta { get; init; }

    public int? CorrectedDelta { get; init; }

    public int? EffectiveDelta { get; init; }

    public bool? IsRollback { get; init; }

    public bool? WasSuppressed { get; init; }

    public uint? ElapsedMs { get; init; }

    public long? ObservedUnixMs { get; init; }

    public bool? ChartStreamingActive { get; init; }

    public RollbackFilterSettings? GetFilterSettings()
    {
        if (ReverseWindowMs is null || ResetWindowMs is null || MaxRollbackTicks is null)
        {
            return null;
        }

        return new RollbackFilterSettings(
            ReverseWindowMs.Value,
            ResetWindowMs.Value,
            MaxRollbackTicks.Value,
            QuickReverseMs ?? WheelRollbackFilter.DefaultQuickReverseMs,
            IntentionalReverseMs ?? WheelRollbackFilter.DefaultIntentionalReverseMs).Normalize();
    }

    public DateTimeOffset? GetLastRollbackTime()
    {
        return LastRollbackUnixMs is long value
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : null;
    }

    public DateTimeOffset GetObservedAt()
    {
        return ObservedUnixMs is long value
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : DateTimeOffset.Now;
    }
}

internal readonly record struct DashboardSnapshot(
    bool SuppressionEnabled,
    bool StartupEnabled,
    RollbackFilterSettings FilterSettings,
    int EventCount,
    int RollbackCount,
    DateTimeOffset? LastRollbackTime);

internal static class DashboardIpcProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
