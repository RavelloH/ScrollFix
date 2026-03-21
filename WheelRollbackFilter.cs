namespace ScrollFix;

internal sealed class WheelRollbackFilter
{
    // 机械编码器回滚通常表现为近期稳定滚动方向中夹杂少量反向刻度。
    internal const uint DefaultReverseWindowMs = 120;
    internal const uint DefaultResetWindowMs = 500;
    internal const int DefaultMaxRollbackTicks = 2;
    private const int IntentionalDirectionChangeEvents = 2;
    private const int MinimumDominantEvents = 4;
    private const int MinimumDominantTicks = 4;

    private readonly Queue<TrendSample> _trendSamples = new();

    private int _oppositeDirection;
    private int _oppositeEventCount;
    private uint _lastOppositeTime;
    private int _suppressedOppositeTicks;
    private RollbackFilterSettings _settings = RollbackFilterSettings.Default;

    public void UpdateSettings(RollbackFilterSettings settings)
    {
        _settings = settings.Normalize();
    }

    public RollbackDecision Evaluate(int delta, uint eventTime)
    {
        RollbackFilterSettings settings = _settings;
        int direction = Math.Sign(delta);

        if (direction == 0)
        {
            return RollbackDecision.None;
        }

        int tickUnits = Math.Max(1, (Math.Abs(delta) + 119) / 120);
        TrimTrend(eventTime, settings.ResetWindowMs);

        if (_trendSamples.Count == 0)
        {
            Accept(direction, tickUnits, eventTime, resetTrend: true);
            return RollbackDecision.None;
        }

        TrendSummary trend = SummarizeTrend(eventTime);

        if (trend.DominantDirection == 0 || direction == trend.DominantDirection)
        {
            Accept(direction, tickUnits, eventTime, resetTrend: false);
            return RollbackDecision.None;
        }

        bool strongTrend =
            trend.DominantEventCount >= MinimumDominantEvents &&
            trend.DominantTickUnits >= Math.Max(MinimumDominantTicks, settings.MaxRollbackTicks + 1) &&
            trend.ElapsedSinceLastDominantMs <= settings.ResetWindowMs;

        if (strongTrend)
        {
            if (_oppositeDirection != direction || ElapsedSince(_lastOppositeTime, eventTime) > settings.ReverseWindowMs)
            {
                _oppositeDirection = direction;
                _oppositeEventCount = 0;
                _suppressedOppositeTicks = 0;
            }

            _oppositeEventCount++;
            _suppressedOppositeTicks += tickUnits;
            _lastOppositeTime = eventTime;

            // 连续出现多个反向事件时，更可能是用户主动换方向，而不是编码器回滚。
            if (_oppositeEventCount >= IntentionalDirectionChangeEvents)
            {
                Accept(direction, tickUnits, eventTime, resetTrend: true);
                return RollbackDecision.None;
            }

            if (_suppressedOppositeTicks <= settings.MaxRollbackTicks)
            {
                return new RollbackDecision(
                    true,
                    trend.ElapsedSinceLastDominantMs,
                    trend.DominantDirection * Math.Abs(delta));
            }
        }

        Accept(direction, tickUnits, eventTime, resetTrend: true);
        return RollbackDecision.None;
    }

    private void Accept(int direction, int tickUnits, uint eventTime, bool resetTrend)
    {
        if (resetTrend)
        {
            _trendSamples.Clear();
        }

        _trendSamples.Enqueue(new TrendSample(direction, tickUnits, eventTime));
        _oppositeDirection = 0;
        _oppositeEventCount = 0;
        _lastOppositeTime = 0;
        _suppressedOppositeTicks = 0;
    }

    private static uint ElapsedSince(uint start, uint end)
    {
        return unchecked(end - start);
    }

    private void TrimTrend(uint eventTime, uint resetWindowMs)
    {
        while (_trendSamples.Count > 0 && ElapsedSince(_trendSamples.Peek().EventTime, eventTime) > resetWindowMs)
        {
            _trendSamples.Dequeue();
        }
    }

    private TrendSummary SummarizeTrend(uint eventTime)
    {
        int positiveTicks = 0;
        int negativeTicks = 0;
        int positiveEvents = 0;
        int negativeEvents = 0;
        uint? lastPositiveTime = null;
        uint? lastNegativeTime = null;

        foreach (TrendSample sample in _trendSamples)
        {
            if (sample.Direction > 0)
            {
                positiveTicks += sample.TickUnits;
                positiveEvents++;
                lastPositiveTime = sample.EventTime;
            }
            else if (sample.Direction < 0)
            {
                negativeTicks += sample.TickUnits;
                negativeEvents++;
                lastNegativeTime = sample.EventTime;
            }
        }

        if (positiveTicks == negativeTicks)
        {
            return TrendSummary.None;
        }

        if (positiveTicks > negativeTicks)
        {
            return new TrendSummary(
                1,
                positiveTicks,
                positiveEvents,
                lastPositiveTime is null ? uint.MaxValue : ElapsedSince(lastPositiveTime.Value, eventTime));
        }

        return new TrendSummary(
            -1,
            negativeTicks,
            negativeEvents,
            lastNegativeTime is null ? uint.MaxValue : ElapsedSince(lastNegativeTime.Value, eventTime));
    }

    private readonly record struct TrendSample(int Direction, int TickUnits, uint EventTime);
}

internal readonly record struct RollbackDecision(bool IsRollback, uint ElapsedMs, int CorrectedDelta)
{
    public static RollbackDecision None => new(false, 0, 0);
}

internal readonly record struct TrendSummary(
    int DominantDirection,
    int DominantTickUnits,
    int DominantEventCount,
    uint ElapsedSinceLastDominantMs)
{
    public static TrendSummary None => new(0, 0, 0, uint.MaxValue);
}
