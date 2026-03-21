using Microsoft.Win32;

namespace ScrollFix;

internal sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;

    public StartupRegistrationService(string valueName)
    {
        _valueName = valueName;
    }

    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        string? value = key?.GetValue(_valueName) as string;
        return string.Equals(value, BuildCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(_valueName, BuildCommand());
            return;
        }

        key.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    private static string BuildCommand()
    {
        return $"\"{Application.ExecutablePath}\"";
    }
}
