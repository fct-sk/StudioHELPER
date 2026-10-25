using StudioHELPER.Core;
using StudioHELPER.Windows.Services;
using StudioHELPER.Windows.Tools.StudioUsb;
using StudioHELPER.Windows.Views;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Localization = StudioHELPER.Core.Localization;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace StudioHELPER.Windows;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private readonly HomeView _homeView;
    private readonly SettingsView _settingsView;
    private readonly GitHubUpdateService _updateService;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _trayOpenItem;
    private readonly ToolStripMenuItem _trayExitItem;
    private StudioUsbView? _studioUsbView;
    private string _currentPage = "home";

    public MainWindow()
    {
        InitializeComponent();
        _settingsStore = new SettingsStore();
        _homeView = new HomeView(_settingsStore, LanguageChanged);
        _updateService = new GitHubUpdateService();
        _settingsView = new SettingsView(_settingsStore, SettingsChanged, _updateService,
            UpdateAvailableChanged, RequestShutdown);
        _trayOpenItem = new ToolStripMenuItem();
        _trayExitItem = new ToolStripMenuItem();
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add(_trayOpenItem);
        trayMenu.Items.Add(_trayExitItem);
        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "StudioHELPER",
            ContextMenuStrip = trayMenu,
            Visible = false
        };
        _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        _trayOpenItem.Click += delegate { RestoreFromTray(); };
        _trayExitItem.Click += delegate { Close(); };
        StateChanged += MainWindow_StateChanged;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        try { StartupManager.SynchronizeInstalledApplication(_settingsStore.Settings.RunAtSystemStartup); } catch { }
        ApplyLocalization();
        ShowHome();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void StudioUsbButton_Click(object sender, RoutedEventArgs e) => ShowStudioUsb();
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowHome()
    {
        _currentPage = "home";
        ContentHost.Content = _homeView;
        PageTitleText.Text = Localization.T("HomeTitle");
        SetSelectedButton(HomeButton);
    }

    private void ShowStudioUsb()
    {
        _currentPage = "studio-usb";
        _studioUsbView ??= new StudioUsbView(_settingsStore);
        ContentHost.Content = _studioUsbView;
        PageTitleText.Text = Localization.T("StudioUsbTitle");
        SetSelectedButton(StudioUsbButton);
    }

    private void ShowSettings()
    {
        _currentPage = "settings";
        ContentHost.Content = _settingsView;
        PageTitleText.Text = Localization.T("SettingsTitle");
        SetSelectedButton(SettingsButton);
    }

    private void LanguageChanged()
    {
        ApplyLocalization();
        _homeView.RefreshLanguage();
        _settingsView.RefreshLanguage();
        _studioUsbView?.RefreshLanguage();
        PageTitleText.Text = _currentPage switch
        {
            "studio-usb" => Localization.T("StudioUsbTitle"),
            "settings" => Localization.T("SettingsTitle"),
            _ => Localization.T("HomeTitle")
        };
    }

    private void SettingsChanged() => ApplyLocalization();

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) =>
        await _settingsView.CheckForUpdatesAsync(userInitiated: false);

    private void UpdateAvailableChanged(bool available) =>
        UpdateBadge.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

    private static void RequestShutdown() => System.Windows.Application.Current.Shutdown();

    private void ApplyLocalization()
    {
        HomeButton.ToolTip = Localization.T("HomeTooltip");
        StudioUsbButton.ToolTip = Localization.T("StudioUsbTooltip");
        SettingsButton.ToolTip = Localization.T("SettingsTooltip");
        _trayOpenItem.Text = Localization.T("Open");
        _trayExitItem.Text = Localization.T("Exit");
    }

    private void SetSelectedButton(Button selected)
    {
        var active = new SolidColorBrush(Color.FromRgb(43, 58, 85));
        var inactive = Brushes.Transparent;
        HomeButton.Background = ReferenceEquals(selected, HomeButton) ? active : inactive;
        StudioUsbButton.Background = ReferenceEquals(selected, StudioUsbButton) ? active : inactive;
        SettingsButton.Background = ReferenceEquals(selected, SettingsButton) ? active : inactive;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized || !_settingsStore.Settings.MinimizeToSystemTray) return;
        Hide();
        _trayIcon.Visible = true;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _trayIcon.Visible = false;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _studioUsbView?.SaveSettings();
        _settingsStore.Save();
        _trayIcon.Visible = false;
    }

    private void MainWindow_Closed(object? sender, EventArgs e) => _trayIcon.Dispose();
}
