using StudioHELPER.Core;
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

namespace StudioHELPER.Windows;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private readonly HomeView _homeView;
    private StudioUsbView? _studioUsbView;
    private string _currentPage = "home";

    public MainWindow()
    {
        InitializeComponent();
        _settingsStore = new SettingsStore();
        _homeView = new HomeView(_settingsStore, LanguageChanged);
        Closing += MainWindow_Closing;
        ApplyLocalization();
        ShowHome();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void StudioUsbButton_Click(object sender, RoutedEventArgs e) => ShowStudioUsb();

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

    private void LanguageChanged()
    {
        ApplyLocalization();
        _homeView.RefreshLanguage();
        _studioUsbView?.RefreshLanguage();
        PageTitleText.Text = _currentPage == "home"
            ? Localization.T("HomeTitle")
            : Localization.T("StudioUsbTitle");
    }

    private void ApplyLocalization()
    {
        HomeButton.ToolTip = Localization.T("HomeTooltip");
        StudioUsbButton.ToolTip = Localization.T("StudioUsbTooltip");
    }

    private void SetSelectedButton(Button selected)
    {
        var active = new SolidColorBrush(Color.FromRgb(43, 58, 85));
        var inactive = Brushes.Transparent;
        HomeButton.Background = ReferenceEquals(selected, HomeButton) ? active : inactive;
        StudioUsbButton.Background = ReferenceEquals(selected, StudioUsbButton) ? active : inactive;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _studioUsbView?.SaveSettings();
        _settingsStore.Save();
    }
}
