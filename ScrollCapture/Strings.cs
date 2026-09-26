namespace ScrollFix;

internal static class Strings
{
    public static string Waveform_EmptyHint => "开始捕捉后，滚轮事件会显示在这里";
    public static string Waveform_LegendRaw => "捕捉到的滚轮事件";
    public static string Waveform_LegendCorrected => "校正结果";
    public static string Waveform_YAxisLabel => "Y 轴：deltaY";
    public static string Waveform_TimeAgo(double seconds) => $"{seconds:0} 秒前";
    public static string Waveform_TimeNow => "现在";
}

internal sealed class WheelActivityEventArgs : EventArgs
{
    public WheelActivityEventArgs(DateTimeOffset observedAt, int rawDelta, int correctedDelta)
    {
        ObservedAt = observedAt;
        RawDelta = rawDelta;
        CorrectedDelta = correctedDelta;
    }

    public DateTimeOffset ObservedAt { get; }

    public int RawDelta { get; }

    public int CorrectedDelta { get; }
}
