namespace ScrollFix;

internal static class Program
{
    private const string DashboardModeArgument = "--dashboard";
    private const string SingleInstanceMutexName = @"Global\ScrollFix.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (TryRunDashboardProcess(args))
        {
            return;
        }

        using Mutex singleInstanceMutex = new(false, SingleInstanceMutexName);
        bool hasMutex;

        try
        {
            hasMutex = singleInstanceMutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            hasMutex = true;
        }

        if (!hasMutex)
        {
            MessageBox.Show(
                Strings.Error_AlreadyRunning,
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Application.Run(new TrayApplicationContext());
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                Strings.Error_StartupFailed(exception.Message),
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            try
            {
                singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }
    }

    private static bool TryRunDashboardProcess(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], DashboardModeArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            Application.Run(new DashboardClientContext(args[1]));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                Strings.Error_DashboardStartFailed(exception.Message),
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        return true;
    }
}
