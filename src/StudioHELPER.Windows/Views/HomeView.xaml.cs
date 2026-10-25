using StudioHELPER.Core;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using Localization = StudioHELPER.Core.Localization;

namespace StudioHELPER.Windows.Views;

public partial class HomeView : UserControl
{
    private readonly SettingsStore _settingsStore;
    private readonly Action _languageChanged;
    private bool _initializing = true;

    public HomeView(SettingsStore settingsStore, Action languageChanged)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        _languageChanged = languageChanged;
        LanguageComboBox.ItemsSource = new[]
        {
            new LanguageOption("system", "System"),
            new LanguageOption("en", "English"),
            new LanguageOption("sk", "Slovensky")
        };
        LanguageComboBox.SelectedValue = settingsStore.Settings.Language;
        RefreshLanguage();
        _initializing = false;
    }

    public void RefreshLanguage() => LanguageLabel.Text = Localization.T("Language");

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LanguageComboBox.SelectedValue is not string language) return;
        _settingsStore.Settings.Language = language;
        Localization.SetLanguage(language);
        _settingsStore.Save();
        _languageChanged();
    }

    private sealed record LanguageOption(string Value, string DisplayName);
}
