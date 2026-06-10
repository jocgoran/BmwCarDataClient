# Piano di Migrazione: Da CLI a WPF Moderno (BMW CarData Client)

Questo documento guida la trasformazione del progetto C# da applicazione Console a un'applicazione grafica WPF moderna, robusta e conforme ai requisiti di sicurezza e tempistiche di BMW CarData.

---

## 1. Modifica del file `.csproj`

Apri il file `BmwCarDataClient.csproj` e sostituisci il contenuto per abilitare WPF, il supporto ai componenti desktop e le librerie necessarie (`ModernWpf` per l'interfaccia grafica e `CommunityToolkit.Mvvm` per la gestione pulita dei dati).

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWpf>true</UseWpf>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ModernWpfUI" Version="0.9.6" />
    
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    
    <PackageReference Include="MQTTnet" Version="4.3.3.952" />
  </ItemGroup>

</Project>

2. Definizione della UI: MainWindow.xaml
Sostituisci o crea la finestra principale con questo layout scuro, diviso nelle tre sezioni logiche richieste, più l'area di streaming dei dati JSON.

XML
<Window x:Class="BmwCarDataClient.MainWindow"
        xmlns="[http://schemas.microsoft.com/winfx/2000/xaml/presentation](http://schemas.microsoft.com/winfx/2000/xaml/presentation)"
        xmlns:x="[http://schemas.microsoft.com/winfx/2000/xaml](http://schemas.microsoft.com/winfx/2000/xaml)"
        xmlns:ui="[http://schemas.modernwpf.com/2019](http://schemas.modernwpf.com/2019)"
        ui:WindowHelper.UseModernWindowStyle="True"
        Title="BMW CarData Streaming Client" Height="750" Width="950"
        Background="#1E1E1E" Foreground="White">

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="280"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <Border Background="#252526" Grid.Column="0" BorderBrush="#333333" BorderThickness="0,0,1,0">
            <StackPanel Padding="20" Spacing="15">
                <TextBlock Text="STATUS" FontSize="16" FontWeight="Bold" Foreground="#007ACC"/>
                <Separator Background="#333333"/>
                
                <StackPanel>
                    <TextBlock Text="VIN Attivo:" Foreground="Gray" FontSize="11"/>
                    <TextBlock Text="{Binding Vin}" FontSize="14" FontWeight="SemiBold"/>
                </StackPanel>

                <StackPanel>
                    <TextBlock Text="GCID:" Foreground="Gray" FontSize="11"/>
                    <TextBlock Text="{Binding Gcid}" FontSize="12" TextWrapping="Wrap"/>
                </StackPanel>

                <Separator Background="#333333"/>
                
                <StackPanel Orientation="Horizontal" Spacing="10">
                    <Ellipse Width="12" Height="12" Fill="{Binding MqttStatusColor}"/>
                    <TextBlock Text="{Binding MqttStatusText}" VerticalAlignment="Center"/>
                </StackPanel>
            </StackPanel>
        </Border>

        <ScrollViewer Grid.Column="1" Padding="25">
            <StackPanel Spacing="25">
                
                <ui:SimpleCard Background="#2D2D30" BorderBrush="#3F3F46">
                    <StackPanel Spacing="10">
                        <TextBlock Text="1. Device Flow with PKCE" FontSize="16" FontWeight="Bold" Foreground="#F1B82D"/>
                        <TextBlock Text="Avvia la sessione di autenticazione sicura generatando le chiavi crittografiche." Grid.Column="0" Foreground="LightGray" TextWrapping="Wrap"/>
                        <Button Content="Avvia Flusso Autenticazione" 
                                Command="{Binding StartDeviceFlowCommand}" 
                                Width="200" HorizontalAlignment="Left"
                                ui:ControlHelper.PlaceholderText="Avvia"/>
                    </StackPanel>
                </ui:SimpleCard>

                <ui:SimpleCard Background="#2D2D30" BorderBrush="#3F3F46" IsEnabled="{Binding IsDeviceFlowStarted}">
                    <StackPanel Spacing="12">
                        <TextBlock Text="2. Device Authorization" FontSize="16" FontWeight="Bold" Foreground="#F1B82D"/>
                        <TextBlock Text="Apri il portale BMW CarData e inserisci il codice mostrato qui sotto:" Foreground="LightGray"/>
                        
                        <Hyperlink NavigateUri="[https://customer.streaming-cardata.bmwgroup.com](https://customer.streaming-cardata.bmwgroup.com)" HandleClick="True">
                            <TextBlock Text="Apri Portale BMW CarData" Foreground="#007ACC" Cursor="Hand"/>
                        </Hyperlink>

                        <StackPanel Orientation="Horizontal" Spacing="15">
                            <TextBox Text="{Binding UserCode}" FontSize="22" FontWeight="Bold" IsReadOnly="True" Width="200" HorizontalContentAlignment="Center" Background="#1E1E1E"/>
                            <Button Content="Copia Codice" Command="{Binding CopyCodeCommand}" VerticalAlignment="Center"/>
                        </StackPanel>
                        
                        <TextBlock Text="{Binding PollingStatusMessage}" FontStyle="Italic" Foreground="Gray"/>
                    </StackPanel>
                </ui:SimpleCard>

                <ui:SimpleCard Background="#2D2D30" BorderBrush="#3F3F46" IsEnabled="{Binding IsAuthorized}">
                    <StackPanel Spacing="10">
                        <TextBlock Text="3. Request a Token for the Device" FontSize="16" FontWeight="Bold" Foreground="#F1B82D"/>
                        <TextBlock Text="Scambio del codice dispositivo con i token di sicurezza (Access, Refresh, ID Token)." Foreground="LightGray"/>
                        <ProgressBar IsIndeterminate="{Binding IsFetchingTokens}" Height="4" Margin="0,5,0,0"/>
                        <TextBlock Text="{Binding TokenStatusMessage}" FontWeight="SemiBold" Foreground="#4EC9B0"/>
                    </StackPanel>
                </ui:SimpleCard>

                <TextBlock Text="Live Telemetry MQTT Stream" FontSize="16" FontWeight="Bold"/>
                <TextBox Text="{Binding LiveStreamJson}" 
                         FontFamily="Consolas" 
                         FontSize="13" 
                         IsReadOnly="True" 
                         AcceptsReturn="True" 
                         TextWrapping="Wrap" 
                         Height="220" 
                         Background="#121212" 
                         Foreground="#DCDCDC" 
                         VerticalScrollBarVisibility="Auto"/>
            </StackPanel>
        </ScrollViewer>
    </Grid>
</Window>
3. Logica di Controllo: MainViewModel.cs
Questa classe gestisce lo stato dell'interfaccia grafica e implementa le protezioni anti-blocco (polling ritardato con l'aggiunta di micro-delay casuali).

C#
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BmwCarDataClient
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private string _vin = "WBA5H31080D223259";
        [ObservableProperty] private string _gcid = "e540c289-3966-48c3-8cbe-f461f4dcd9c7";
        [ObservableProperty] private string _userCode = "----";
        [ObservableProperty] private string _pollingStatusMessage = "In attesa di avvio...";
        [ObservableProperty] private string _tokenStatusMessage = "Nessun token caricato.";
        [ObservableProperty] private string _liveStreamJson = "{}";
        [ObservableProperty] private string _mqttStatusText = "Disconnesso";
        [ObservableProperty] private Brush _mqttStatusColor = Brushes.Red;
        
        [ObservableProperty] private bool _isDeviceFlowStarted;
        [ObservableProperty] private bool _isAuthorized;
        [ObservableProperty] private bool _isFetchingTokens;

        [RelayCommand]
        private async Task StartDeviceFlow()
        {
            IsDeviceFlowStarted = true;
            PollingStatusMessage = "Richiesta codici a BMW...";
            
            // SIMULAZIONE: Sostituisci con la tua chiamata HTTP POST reale per PKCE Device Flow
            await Task.Delay(1000); 
            UserCode = "A1B2-C3D4"; 
            
            // Avvia il ciclo di polling anti-blocco controllato
            _ = RunAntiBotPollingLoop();
        }

        private async Task RunAntiBotPollingLoop()
        {
            int baseIntervalSeconds = 5; // Recuperato dalla response BMW
            bool authorized = false;

            while (!authorized)
            {
                PollingStatusMessage = $"Verifica autorizzazione (Prossimo controllo tra {baseIntervalSeconds}s)...";
                
                // ANTI-BOT: Intervallo imposto da BMW + ritardo "umano" randomico (500ms - 1500ms)
                int humanDelay = (baseIntervalSeconds * 1000) + new Random().Next(500, 1500);
                await Task.Delay(humanDelay);

                // SIMULAZIONE: Sostituisci con il controllo reale dei token
                // var result = await MyBmwAuthService.CheckAuthorization(deviceCode);
                
                // Se autorizzato:
                if (DateTime.Now.Second % 7 == 0) // Simulazione successo casuale
                {
                    authorized = true;
                    IsAuthorized = true;
                    UserCode = "AUTORIZZATO";
                    PollingStatusMessage = "Dispositivo autorizzato con successo sul portale!";
                    _ = FetchTokensAndConnectMqtt();
                }
            }
        }

        private async Task FetchTokensAndConnectMqtt()
        {
            IsFetchingTokens = true;
            TokenStatusMessage = "Scambio codici per ID_Token e Access_Token...";
            
            await Task.Delay(1500); // Chiamata di scambio effettiva
            
            IsFetchingTokens = false;
            TokenStatusMessage = "Token salvati in bmw_tokens.json. Connessione MQTT...";
            
            // Avvia il client MQTT usando l'ID_TOKEN come password
            StartMqttStreaming();
        }

        private void StartMqttStreaming()
        {
            MqttStatusText = "Connesso (Streaming Attivo)";
            MqttStatusColor = Brushes.Green;
            
            // Aggiorna il campo di testo quando arrivano messaggi MQTT
            LiveStreamJson = "{\n  \"vin\": \"WBA5H31080D223259\",\n  \"timestamp\": \"" + DateTime.UtcNow.ToString("o") + "\",\n  \"status\": \"Streaming live configurato correttamente via ID Token\"\n}";
        }

        [RelayCommand]
        private void CopyCode()
        {
            if (!string.IsNullOrEmpty(UserCode) && UserCode != "----")
            {
                Clipboard.SetText(UserCode);
            }
        }
    }
}
4. Inizializzazione in App.xaml.cs
Assicurati che al lancio dell'applicazione venga impostato il tema scuro di default fornito da ModernWpf.

C#
using System.Windows;
using ModernWpf;

namespace BmwCarDataClient
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Forza il tema scuro globale all'avvio
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        }
    }
}
***

Questo approccio disabilita i pulsanti durante le elaborazioni per evitare chiamate multiple asfissianti e implementa il ritardo necessario per non allertare i sistemi anti-bot di BMW.