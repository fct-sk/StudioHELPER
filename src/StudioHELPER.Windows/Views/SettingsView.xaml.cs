using StudioHELPER.Core;
using StudioHELPER.Windows.Services;
using System.Windows;
using System.Windows.Controls;
using Localization = StudioHELPER.Core.Localization;
using UserControl = System.Windows.Controls.UserControl;

namespace StudioHELPER.Windows.Views;

public partial class SettingsView : UserControl
{
    private readonly SettingsStore _settingsStore;
    private readonly Action _settingsChanged;
    private readonly GitHubUpdateService _updateService;
    private readonly Action<bool> _updateAvailableChanged;
    private readonly Action _requestShutdown;
    private UpdateInfo? _availableUpdate;
    private string? _statusKey;
    private bool _initializing = true;

    internal SettingsView(SettingsStore settingsStore, Action settingsChanged,
        GitHubUpdateService updateService, Action<bool> updateAvailableChanged, Action requestShutdown)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        _settingsChanged = settingsChanged;
        _updateService = updateService;
        _updateAvailableChanged = updateAvailableChanged;
        _requestShutdown = requestShutdown;
        RunAtStartupCheckBox.IsChecked = settingsStore.Settings.RunAtSystemStartup;
        MinimizeToTrayCheckBox.IsChecked = settingsStore.Settings.MinimizeToSystemTray;
        RefreshLanguage();
        _initializing = false;
    }

    public void RefreshLanguage()
    {
        RunAtStartupCheckBox.Content = Localization.T("RunAtStartup");
        MinimizeToTrayCheckBox.Content = Localization.T("MinimizeToTray");
        UpdatesTitleText.Text = Localization.T("Updates");
        CurrentVersionText.Text = $"{Localization.T("CurrentVersion")}: {_updateService.CurrentVersion.ToString(3)}";
        CheckUpdatesButton.Content = Localization.T("CheckUpdates");
        InstallUpdateButton.Content = Localization.T("InstallUpdate");
        RenderStatus();
    }

    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        CheckUpdatesButton.IsEnabled = false;
        _statusKey = "CheckingUpdates";
        RenderStatus();
        try
        {
            UpdateInfo? update = await _updateService.CheckForUpdatesAsync();
            SetAvailableUpdate(update);
            _statusKey = update is null ? "UpToDate" : "UpdateAvailable";
        }
        catch
        {
            if (userInitiated) _statusKey = "UpdateError";
            else _statusKey = null;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            RenderStatus();
        }
    }

    private void SetAvailableUpdate(UpdateInfo? update)
    {
        _availableUpdate = update;
        InstallUpdateButton.Visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
        _updateAvailableChanged(update is not null);
    }

    private void RenderStatus()
    {
        if (_statusKey is null) { UpdateStatusText.Text = string.Empty; return; }
        UpdateStatusText.Text = _statusKey == "UpdateAvailable" && _availableUpdate is not null
            ? string.Format(Localization.T(_statusKey), _availableUpdate.Version.ToString(3))
            : Localization.T(_statusKey);
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(userInitiated: true);

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<int>(value =>
            {
                UpdateProgressBar.Value = value;
                UpdateStatusText.Text = string.Format(Localization.T("DownloadingUpdate"), value);
            });
            string installer = await _updateService.DownloadInstallerAsync(_availableUpdate, progress);
            UpdateStatusText.Text = Localization.T("StartingUpdate");
            GitHubUpdateService.StartInstaller(installer);
            _requestShutdown();
        }
        catch
        {
            _statusKey = "UpdateError";
            RenderStatus();
            CheckUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settingsStore.Settings.RunAtSystemStartup = RunAtStartupCheckBox.IsChecked == true;
        _settingsStore.Settings.MinimizeToSystemTray = MinimizeToTrayCheckBox.IsChecked == true;
        StartupManager.SetEnabled(_settingsStore.Settings.RunAtSystemStartup);
        _settingsStore.Save();
        _settingsChanged();
    }
}
