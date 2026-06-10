using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace BmwCarDataClient.Localization
{
    public class TranslationSource : INotifyPropertyChanged
    {
        private static readonly TranslationSource instance = new TranslationSource();
        public static TranslationSource Instance => instance;

        // Assicurati che Localization.Resources sia accessibile (Access Modifier = Public nel file .resx)
        private readonly ResourceManager resourceManager = Localization.Resources.ResourceManager;

        public event PropertyChangedEventHandler? PropertyChanged;

        // Questo indexer permette di fare il binding direttamente alla chiave: Path=[Chiave]
        public string this[string key] => resourceManager.GetString(key, CurrentCulture) ?? $"[{key}]";

        public CultureInfo CurrentCulture
        {
            get => Thread.CurrentThread.CurrentUICulture;
            set
            {
                if (Equals(Thread.CurrentThread.CurrentUICulture, value)) return;
                
                Thread.CurrentThread.CurrentCulture = value;
                Thread.CurrentThread.CurrentUICulture = value;
                
                // Notificare un cambiamento con string.Empty o null aggiorna TUTTI i binding agganciati a questa istanza!
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            }
        }
    }
}
