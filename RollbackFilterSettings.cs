namespace ScrollFix;

internal readonly record struct RollbackFilterSettings(
    uint ReverseWindowMs,
    uint ResetWindowMs,
    int MaxRollbackTicks,
    uint QuickReverseMs,
    uint IntentionalReverseMs)
{
    public static RollbackFilterSettings Default => new(
        WheelRollbackFilter.DefaultReverseWindowMs,
        WheelRollbackFilter.DefaultResetWindowMs,
        WheelRollbackFilter.DefaultMaxRollbackTicks,
        WheelRollbackFilter.DefaultQuickReverseMs,
        WheelRollbackFilter.DefaultIntentionalReverseMs);

    public RollbackFilterSettings Normalize()
    {
        uint reverseWindowMs = Math.Clamp(ReverseWindowMs, 20U, 500U);
        uint resetWindowMs = Math.Clamp(ResetWindowMs, 120U, 3_000U);
        int maxRollbackTicks = Math.Clamp(MaxRollbackTicks, 1, 6);
        uint quickReverseMs = Math.Clamp(QuickReverseMs, 10U, 200U);
        uint intentionalReverseMs = Math.Clamp(IntentionalReverseMs, 60U, 600U);

        if (resetWindowMs < reverseWindowMs)
        {
            resetWindowMs = reverseWindowMs;
        }

        if (intentionalReverseMs < quickReverseMs)
        {
            intentionalReverseMs = quickReverseMs;
        }

        return new RollbackFilterSettings(
            reverseWindowMs,
            resetWindowMs,
            maxRollbackTicks,
            quickReverseMs,
            intentionalReverseMs);
    }
}
