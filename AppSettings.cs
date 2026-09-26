namespace ScrollFix;

internal sealed class AppSettings
{
    public bool SuppressionEnabled { get; set; } = true;

    public uint ReverseWindowMs { get; set; } = WheelRollbackFilter.DefaultReverseWindowMs;

    public uint ResetWindowMs { get; set; } = WheelRollbackFilter.DefaultResetWindowMs;

    public int MaxRollbackTicks { get; set; } = WheelRollbackFilter.DefaultMaxRollbackTicks;

    public uint QuickReverseMs { get; set; } = WheelRollbackFilter.DefaultQuickReverseMs;

    public uint IntentionalReverseMs { get; set; } = WheelRollbackFilter.DefaultIntentionalReverseMs;

    public RollbackFilterSettings GetRollbackFilterSettings()
    {
        return new RollbackFilterSettings(
            ReverseWindowMs,
            ResetWindowMs,
            MaxRollbackTicks,
            QuickReverseMs,
            IntentionalReverseMs).Normalize();
    }

    public void ApplyRollbackFilterSettings(RollbackFilterSettings settings)
    {
        RollbackFilterSettings normalized = settings.Normalize();
        ReverseWindowMs = normalized.ReverseWindowMs;
        ResetWindowMs = normalized.ResetWindowMs;
        MaxRollbackTicks = normalized.MaxRollbackTicks;
        QuickReverseMs = normalized.QuickReverseMs;
        IntentionalReverseMs = normalized.IntentionalReverseMs;
    }
}
