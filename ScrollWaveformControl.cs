using System.Drawing.Drawing2D;

namespace ScrollFix;

internal sealed class ScrollWaveformControl : Control
{
    private const double HistorySeconds = 10D;
    private const int DefaultRefreshRateHz = 60;
    private const int DifferenceThreshold = 1;
    private const float StemCapWidth = 4F;
    private const int OuterPadding = 12;
    private const int LeftAxisWidth = 56;
    private const int HeaderHeight = 34;
    private const int FooterHeight = 28;
    private const int RightPadding = 12;
    private const float DifferenceOffset = 2.5F;
    private const int MaxSampleCount = 2048;

    private readonly Queue<WaveformSample> _samples = new();
    private readonly System.Windows.Forms.Timer _animationTimer;
    private bool _captureActive;
    private long _lastTimestampMs;
    private bool _renderingActive;

    public ScrollWaveformControl()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;

        _animationTimer = new System.Windows.Forms.Timer
        {
            Interval = GetIntervalForRefreshRate(DefaultRefreshRateHz)
        };
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    public void AddActivity(WheelActivityEventArgs activity)
    {
        if (!_captureActive)
        {
            return;
        }

        AppendSample(activity.ObservedAt.ToUnixTimeMilliseconds(), activity.RawDelta, activity.CorrectedDelta);
    }

    public void SetRenderingActive(bool active, int refreshRateHz)
    {
        if (!active)
        {
            _captureActive = false;
            _renderingActive = false;
            _animationTimer.Stop();
            ClearSamples();
            return;
        }

        if (!_captureActive)
        {
            ClearSamples();
        }

        _captureActive = true;
        _renderingActive = true;
        _animationTimer.Interval = GetIntervalForRefreshRate(refreshRateHz);
        TrimSamples(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (!_animationTimer.Enabled)
        {
            _animationTimer.Start();
        }

        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Stop();
            _animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.Clear(BackColor);

        Rectangle surfaceBounds = Rectangle.Inflate(ClientRectangle, -OuterPadding, -OuterPadding);
        Rectangle plotBounds = Rectangle.FromLTRB(
            surfaceBounds.Left + LeftAxisWidth,
            surfaceBounds.Top + HeaderHeight,
            surfaceBounds.Right - RightPadding,
            surfaceBounds.Bottom - FooterHeight);

        if (plotBounds.Width <= 0 || plotBounds.Height <= 0)
        {
            return;
        }

        using SolidBrush backgroundBrush = new(SystemColors.Window);
        using Pen borderPen = new(SystemColors.ActiveBorder, 1F);
        using Pen gridPen = new(SystemColors.ControlLight, 1F) { DashStyle = DashStyle.Dot };
        using Pen axisPen = new(SystemColors.GrayText, 1.2F);
        using Pen rawPen = new(Color.FromArgb(220, 38, 38), 2.8F);
        using Pen correctedPen = new(SystemColors.HotTrack, 2.6F);
        using SolidBrush textBrush = new(ForeColor);
        using SolidBrush mutedTextBrush = new(SystemColors.GrayText);
        using Font titleFont = new(Font, FontStyle.Bold);

        graphics.FillRectangle(backgroundBrush, surfaceBounds);
        graphics.DrawRectangle(borderPen, plotBounds);

        float midY = plotBounds.Top + plotBounds.Height / 2F;
        int axisRange = GetAxisRange();
        float valueScale = plotBounds.Height * 0.42F / axisRange;

        for (int i = -2; i <= 2; i++)
        {
            int value = axisRange * i / 2;
            float y = midY - value * valueScale;
            graphics.DrawLine(gridPen, plotBounds.Left, y, plotBounds.Right, y);
            string label = value > 0 ? $"+{value}" : value.ToString();
            SizeF labelSize = graphics.MeasureString(label, Font);
            graphics.DrawString(label, Font, mutedTextBrush, surfaceBounds.Left + 4, y - labelSize.Height / 2F);
        }

        graphics.DrawLine(axisPen, plotBounds.Left, midY, plotBounds.Right, midY);

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long startMs = nowMs - (long)Math.Round(HistorySeconds * 1000D);

        if (_samples.Count > 0)
        {
            DrawStems(graphics, plotBounds, startMs, nowMs, midY, valueScale, correctedPen, sample => sample.CorrectedDelta, drawOnlyDifference: false);
            DrawStems(graphics, plotBounds, startMs, nowMs, midY, valueScale, rawPen, sample => sample.RawDelta, drawOnlyDifference: true);
        }
        else
        {
            string emptyHint = Strings.Waveform_EmptyHint;
            SizeF hintSize = graphics.MeasureString(emptyHint, Font);
            graphics.DrawString(
                emptyHint,
                Font,
                mutedTextBrush,
                plotBounds.Left + (plotBounds.Width - hintSize.Width) / 2F,
                plotBounds.Top + (plotBounds.Height - hintSize.Height) / 2F);
        }

        float legendX = plotBounds.Left;
        float legendY = surfaceBounds.Top + 10;
        legendX = DrawLegendItem(graphics, rawPen, titleFont, textBrush, legendX, legendY, Strings.Waveform_LegendRaw);
        legendX += 22F;
        _ = DrawLegendItem(graphics, correctedPen, titleFont, textBrush, legendX, legendY, Strings.Waveform_LegendCorrected);

        string yAxisLabel = Strings.Waveform_YAxisLabel;
        SizeF yAxisLabelSize = graphics.MeasureString(yAxisLabel, Font);
        graphics.DrawString(yAxisLabel, Font, mutedTextBrush, plotBounds.Right - yAxisLabelSize.Width - 4, legendY + 2);

        string leftText = Strings.Waveform_TimeAgo(HistorySeconds);
        string rightText = Strings.Waveform_TimeNow;
        graphics.DrawString(leftText, Font, mutedTextBrush, plotBounds.Left, plotBounds.Bottom + 6);
        SizeF rightSize = graphics.MeasureString(rightText, Font);
        graphics.DrawString(rightText, Font, mutedTextBrush, plotBounds.Right - rightSize.Width, plotBounds.Bottom + 6);
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (!_renderingActive || !Visible)
        {
            return;
        }

        TrimSamples(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Invalidate();
    }

    private void AppendSample(long timestampMs, int rawDelta, int correctedDelta)
    {
        if (_lastTimestampMs != 0 && timestampMs <= _lastTimestampMs)
        {
            timestampMs = _lastTimestampMs + 1;
        }

        _samples.Enqueue(new WaveformSample(timestampMs, rawDelta, correctedDelta));
        _lastTimestampMs = timestampMs;
        TrimSamples(timestampMs);

        while (_samples.Count > MaxSampleCount)
        {
            _samples.Dequeue();
        }
    }

    private bool TrimSamples(long nowMs)
    {
        bool trimmed = false;
        long cutoffMs = nowMs - (long)Math.Round(HistorySeconds * 1000D);

        while (_samples.Count > 0 && _samples.Peek().TimestampMs < cutoffMs)
        {
            _samples.Dequeue();
            trimmed = true;
        }

        if (_samples.Count == 0)
        {
            _lastTimestampMs = 0;
        }

        return trimmed;
    }

    private void ClearSamples()
    {
        _samples.Clear();
        _lastTimestampMs = 0;
    }

    private void DrawStems(
        Graphics graphics,
        Rectangle chartBounds,
        long startMs,
        long endMs,
        float midY,
        float valueScale,
        Pen pen,
        Func<WaveformSample, int> selector,
        bool drawOnlyDifference)
    {
        if (_samples.Count == 0)
        {
            return;
        }

        foreach (WaveformSample sample in _samples)
        {
            if (drawOnlyDifference && Math.Abs(sample.RawDelta - sample.CorrectedDelta) < DifferenceThreshold)
            {
                continue;
            }

            int value = selector(sample);

            if (value == 0)
            {
                continue;
            }

            float x = MapX(sample.TimestampMs, startMs, endMs, chartBounds);

            if (Math.Abs(sample.RawDelta - sample.CorrectedDelta) >= DifferenceThreshold)
            {
                x += drawOnlyDifference ? -DifferenceOffset : DifferenceOffset;
            }

            float y = midY - value * valueScale;
            graphics.DrawLine(pen, x, midY, x, y);
            graphics.DrawLine(pen, x - StemCapWidth, y, x + StemCapWidth, y);
        }
    }

    private static float MapX(
        long timestampMs,
        long startMs,
        long endMs,
        Rectangle chartBounds)
    {
        double progress = (double)(timestampMs - startMs) / (endMs - startMs);
        progress = Math.Clamp(progress, 0D, 1D);
        return chartBounds.Left + (float)(chartBounds.Width * progress);
    }

    private int GetAxisRange()
    {
        int maxAbs = 120;

        foreach (WaveformSample sample in _samples)
        {
            maxAbs = Math.Max(maxAbs, Math.Abs(sample.RawDelta));
            maxAbs = Math.Max(maxAbs, Math.Abs(sample.CorrectedDelta));
        }

        return Math.Max(120, ((maxAbs + 59) / 60) * 60);
    }

    private static int GetIntervalForRefreshRate(int refreshRateHz)
    {
        int normalizedRefreshRate = refreshRateHz is >= 30 and <= 500
            ? refreshRateHz
            : DefaultRefreshRateHz;

        return Math.Max(1, (int)Math.Round(1000D / normalizedRefreshRate));
    }

    private static float DrawLegendItem(
        Graphics graphics,
        Pen pen,
        Font font,
        Brush textBrush,
        float x,
        float y,
        string text)
    {
        graphics.DrawLine(pen, x, y + 8F, x + 16F, y + 8F);
        graphics.DrawString(text, font, textBrush, x + 22F, y);
        SizeF textSize = graphics.MeasureString(text, font);
        return x + 22F + textSize.Width;
    }

    private readonly record struct WaveformSample(
        long TimestampMs,
        int RawDelta,
        int CorrectedDelta);
}
