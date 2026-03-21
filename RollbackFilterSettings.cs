namespace ScrollFix;

internal readonly record struct RollbackFilterSettings(uint ReverseWindowMs, uint ResetWindowMs, int MaxRollbackTicks)
{
    public static RollbackFilterSettings Default => new(
        WheelRollbackFilter.DefaultReverseWindowMs,
        WheelRollbackFilter.DefaultResetWindowMs,
        WheelRollbackFilter.DefaultMaxRollbackTicks);

    public RollbackFilterSettings Normalize()
    {
        uint reverseWindowMs = Math.Clamp(ReverseWindowMs, 20U, 500U);
        uint resetWindowMs = Math.Clamp(ResetWindowMs, 120U, 3_000U);
        int maxRollbackTicks = Math.Clamp(MaxRollbackTicks, 1, 6);

        if (resetWindowMs < reverseWindowMs)
        {
            resetWindowMs = reverseWindowMs;
        }

        return new RollbackFilterSettings(reverseWindowMs, resetWindowMs, maxRollbackTicks);
    }
}
