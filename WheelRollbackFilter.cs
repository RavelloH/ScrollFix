namespace ScrollFix;

internal sealed class WheelRollbackFilter
{
    // 小幅反向事件只有在近期同向滚动趋势足够明确时才作为回滚候选。
    // 候选出现后进入待确认状态：回到原方向则判为孤立回滚；连续反向达到
    // 确认数才切换方向。这样不会因一个偶发的反向事件立刻让趋势翻转。
    internal const uint DefaultReverseWindowMs = 120;
    internal const uint DefaultResetWindowMs = 500;
    internal const int DefaultMaxRollbackTicks = 2;
    internal const uint DefaultQuickReverseMs = 45;
    internal const uint DefaultIntentionalReverseMs = 180;

    private const int MinimumDominantEvents = 2;
    private const int MinimumDominantTicks = 2;
    private const int EstablishedDominantEvents = 3;
    private const int EstablishedDominantTicks = 3;
    private const int RequiredIntentionalReverseEvents = 3;

    private readonly Queue<TrendSample> _trendSamples = new();

    private uint _lastForwardEventTime;
    private bool _hasForwardEvent;
    private int _pendingReverseDirection;
    private int _pendingOriginalDirection;
    private int _pendingReverseEventCount;
    private uint _lastPendingReverseEventTime;
    private RollbackFilterSettings _settings = RollbackFilterSettings.Default;

    public void UpdateSettings(RollbackFilterSettings settings)
    {
        _settings = settings.Normalize();
        ResetPendingReversal();
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

        if (_hasForwardEvent && ElapsedSince(_lastForwardEventTime, eventTime) > settings.ResetWindowMs)
        {
            ResetPendingReversal();
        }

        TrendSummary trend = SummarizeTrend(eventTime);
        int dominantDirection = trend.DominantDirection;

        // 已经有一个小幅反向候选时，先看后续事件是否确认了换向，或回到了原方向。
        if (_pendingReverseEventCount > 0)
        {
            if (direction == _pendingReverseDirection)
            {
                uint sincePendingReverse = ElapsedSince(_lastPendingReverseEventTime, eventTime);
                if (sincePendingReverse > settings.IntentionalReverseMs || tickUnits > settings.MaxRollbackTicks)
                {
                    // 间隔较长或幅度较大的反向事件更像有意换向，直接接受。
                    Accept(direction, tickUnits, eventTime, switchDirection: true);
                    return RollbackDecision.None;
                }

                _pendingReverseEventCount++;
                _lastPendingReverseEventTime = eventTime;

                if (_pendingReverseEventCount >= RequiredIntentionalReverseEvents)
                {
                    // 连续反向已足以确认用户换向。从当前事件起接受新方向。
                    Accept(direction, tickUnits, eventTime, switchDirection: true);
                    return RollbackDecision.None;
                }

                return CreateSuppressedDecision(
                    _pendingOriginalDirection,
                    direction,
                    delta,
                    eventTime);
            }

            if (direction == _pendingOriginalDirection)
            {
                // 反向只出现了一两次后又回到原方向：这些是回滚脉冲。
                ResetPendingReversal();
                Accept(direction, tickUnits, eventTime, switchDirection: false);
                return RollbackDecision.None;
            }
        }

        // 没有趋势或与趋势同向 → 直接接受。
        if (dominantDirection == 0 || direction == dominantDirection)
        {
            Accept(direction, tickUnits, eventTime, switchDirection: false);
            return RollbackDecision.None;
        }

        // 反向事件：按时间贴近度或同向趋势评估是否为回滚候选。
        uint sinceLastForward = _hasForwardEvent
            ? ElapsedSince(_lastForwardEventTime, eventTime)
            : uint.MaxValue;

        bool quickAfterForward =
            sinceLastForward <= settings.QuickReverseMs &&
            tickUnits <= settings.MaxRollbackTicks;

        bool strongTrend =
            trend.DominantEventCount >= MinimumDominantEvents &&
            trend.DominantTickUnits >= Math.Max(MinimumDominantTicks, settings.MaxRollbackTicks + 1) &&
            trend.ElapsedSinceLastDominantMs <= settings.ResetWindowMs &&
            sinceLastForward <= settings.ReverseWindowMs &&
            tickUnits <= settings.MaxRollbackTicks;

        bool establishedRecentTrend =
            trend.DominantEventCount >= EstablishedDominantEvents &&
            trend.DominantTickUnits >= Math.Max(EstablishedDominantTicks, settings.MaxRollbackTicks + 1) &&
            trend.ElapsedSinceLastDominantMs <= settings.ResetWindowMs &&
            tickUnits <= settings.MaxRollbackTicks;

        bool isRollback = quickAfterForward || strongTrend || establishedRecentTrend;

        if (!isRollback)
        {
            // 主动反向：清空趋势，把反向作为新主方向。
            Accept(direction, tickUnits, eventTime, switchDirection: true);
            return RollbackDecision.None;
        }

        // MaxRollbackTicks 限制单个可疑事件的幅度；持续反向则由上面的确认计数处理。
        if (tickUnits > settings.MaxRollbackTicks)
        {
            Accept(direction, tickUnits, eventTime, switchDirection: true);
            return RollbackDecision.None;
        }

        _pendingReverseDirection = direction;
        _pendingOriginalDirection = dominantDirection;
        _pendingReverseEventCount = 1;
        _lastPendingReverseEventTime = eventTime;
        return CreateSuppressedDecision(dominantDirection, direction, delta, eventTime);
    }

    private void Accept(int direction, int tickUnits, uint eventTime, bool switchDirection)
    {
        if (switchDirection)
        {
            _trendSamples.Clear();
        }

        _trendSamples.Enqueue(new TrendSample(direction, tickUnits, eventTime));
        _lastForwardEventTime = eventTime;
        _hasForwardEvent = true;
        ResetPendingReversal();
    }

    private RollbackDecision CreateSuppressedDecision(
        int originalDirection,
        int direction,
        int delta,
        uint eventTime)
    {
        int correctedDelta = originalDirection * Math.Abs(delta);
        if (Math.Sign(correctedDelta) == direction)
        {
            correctedDelta = -correctedDelta;
        }

        uint elapsedMs = _hasForwardEvent
            ? ElapsedSince(_lastForwardEventTime, eventTime)
            : uint.MaxValue;
        return new RollbackDecision(true, elapsedMs, correctedDelta);
    }

    private void ResetPendingReversal()
    {
        _pendingReverseDirection = 0;
        _pendingOriginalDirection = 0;
        _pendingReverseEventCount = 0;
        _lastPendingReverseEventTime = 0;
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
