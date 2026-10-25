using Microsoft.Win32;
using System.IO;

namespace StudioHELPER.Windows.Services;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StudioHELPER";

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static void SynchronizeInstalledApplication(bool enabled)
    {
        string executable = Environment.ProcessPath ?? string.Empty;
        string installedRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "StudioHELPER"));
        if (executable.StartsWith(installedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            SetEnabled(enabled);
    }
}
