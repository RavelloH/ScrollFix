using System.Drawing.Drawing2D;

namespace ScrollFix;

internal static class TrayIconFactory
{
    public static Icon CreateWheelIcon(Color accentColor)
    {
        using Bitmap bitmap = new(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using SolidBrush fillBrush = new(Color.FromArgb(245, 245, 245));
        using SolidBrush hubBrush = new(accentColor);
        using Pen rimPen = new(Color.FromArgb(52, 52, 52), 3.2f);
        using Pen spokePen = new(accentColor, 2.8f);
        using Pen detailPen = new(Color.FromArgb(255, 255, 255), 1.6f);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        RectangleF outerCircle = new(4.5f, 4.5f, 23f, 23f);
        RectangleF innerCircle = new(11.5f, 11.5f, 9f, 9f);

        graphics.FillEllipse(fillBrush, outerCircle);
        graphics.DrawEllipse(rimPen, outerCircle);

        PointF center = new(16f, 16f);

        for (int i = 0; i < 6; i++)
        {
            double angle = (Math.PI / 3D) * i - (Math.PI / 2D);
            PointF innerPoint = new(
                center.X + (float)(Math.Cos(angle) * 4.5D),
                center.Y + (float)(Math.Sin(angle) * 4.5D));
            PointF outerPoint = new(
                center.X + (float)(Math.Cos(angle) * 9.5D),
                center.Y + (float)(Math.Sin(angle) * 9.5D));

            graphics.DrawLine(spokePen, innerPoint, outerPoint);
        }

        graphics.FillEllipse(hubBrush, innerCircle);
        graphics.DrawEllipse(detailPen, innerCircle);

        IntPtr iconHandle = bitmap.GetHicon();

        try
        {
            using Icon temporaryIcon = Icon.FromHandle(iconHandle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(iconHandle);
        }
    }
}
