using System.Drawing.Text;

namespace ScrollFix;

internal static class WindowAppearance
{
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2;

    public static void Apply(Form form)
    {
        form.Font = GetPreferredUiFont();

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            form.HandleCreated += (_, _) => ApplyWindows11Backdrop(form.Handle);

            if (form.IsHandleCreated)
            {
                ApplyWindows11Backdrop(form.Handle);
            }
        }
    }

    private static void ApplyWindows11Backdrop(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int roundedCorners = DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(handle, DwmaWindowCornerPreference, ref roundedCorners, sizeof(int));

        int backdropType = DwmsbtMainWindow;
        NativeMethods.DwmSetWindowAttribute(handle, DwmaSystemBackdropType, ref backdropType, sizeof(int));
    }

    private static Font GetPreferredUiFont()
    {
        InstalledFontCollection installedFonts = new();

        if (installedFonts.Families.Any(family => string.Equals(family.Name, "Segoe UI Variable Text", StringComparison.OrdinalIgnoreCase)))
        {
            return new Font("Segoe UI Variable Text", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }

        return new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    }
}
