using BmwCarDataClient.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using ModernWpf;
using System.Globalization;

namespace BmwCarDataClient.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        [ObservableProperty]
        private int selectedThemeIndex = 0; // 0 = Light, 1 = Dark, 2 = System

        partial void OnSelectedThemeIndexChanged(int value)
        {
            // Cambia immediatamente il tema dell'intera applicazione
            ThemeManager.Current.ApplicationTheme = value switch
            {
                0 => ApplicationTheme.Light,
                1 => ApplicationTheme.Dark,
                _ => null // Default di sistema
            };
        }

        [ObservableProperty]
        private int selectedLanguageIndex = 0; 

        [ObservableProperty]
        private string languageStatusMessage = string.Empty;

        partial void OnSelectedLanguageIndexChanged(int value)
        {
            string cultureCode = value switch
            {
                0 => "it-IT",
                1 => "en-US",
                2 => "de-DE",
                3 => "fr-FR",
                4 => "es-ES",
                5 => "pt-BR",
                6 => "pl-PL",
                7 => "hr-HR",
                8 => "zh-CN",
                _ => "it-IT"
            };

            // Cambia la lingua in tempo reale!
            TranslationSource.Instance.CurrentCulture = new CultureInfo(cultureCode);
            
            // TODO: Save 'cultureCode' to preferences so App.xaml.cs can set it at next boot.
            
            LanguageStatusMessage = "✓ Lingua applicata istantaneamente.";
        }
    }
}
