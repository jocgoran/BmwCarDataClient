using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BmwCarDataClient
{
    public partial class MainViewModel : ObservableObject
    {
        private BmwOAuthService? _oauthService;
        private BmwRestService? _restService;
        private BmwMqttService? _mqttService;

        [ObservableProperty]
        private string clientId = string.Empty;

        private readonly string _authEndpoint;
        private readonly string _restApiEndpoint;
        private readonly string _mqttBroker;
        private readonly int _mqttPort;
        private readonly string _mqttTopic;

        [ObservableProperty]
        private string portalUrlTemplate = string.Empty;

        private CancellationTokenSource? _mqttCts;

        private string? _deviceCode;
        private string? _codeVerifier;

        [ObservableProperty]
        private string vin;

        [ObservableProperty]
        private BitmapImage? vehicleImage;

        [ObservableProperty]
        private string gcid;

        [ObservableProperty]
        private bool isDeviceFlowStarted;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsStep4Active))]
        private bool isAuthorized;

        [ObservableProperty]
        private bool isStep1Active = true;

        [ObservableProperty]
        private bool isStep2Active = false;

        [ObservableProperty]
        private bool isStep3Active = false;

        public bool IsStep4Active => IsAuthorized;

        [ObservableProperty]
        private bool isFetchingTokens;

        [ObservableProperty]
        private string pollingStatusMessage;

        [ObservableProperty]
        private string tokenStatusMessage;

        [ObservableProperty]
        private string liveStreamJson;

        [ObservableProperty]
        private string liveStreamStatusMessage;

        [ObservableProperty]
        private string userCode;

        [ObservableProperty]
        private string authorizationUrl;

        [ObservableProperty]
        private string verificationUri;

        [ObservableProperty]
        private string getContainersStatusColor = "#666666";

        [ObservableProperty]
        private string getContainersStatusMessage = "Waiting...";

        [ObservableProperty]
        private string containersJson;

        [ObservableProperty]
        private bool isFetchingContainers;

        [ObservableProperty]
        private string getSmartMaintenanceStatusColor = "#666666";

        [ObservableProperty]
        private string getSmartMaintenanceStatusMessage = "Waiting...";

        [ObservableProperty]
        private string smartMaintenanceJson;

        [ObservableProperty]
        private bool isFetchingSmartMaintenance;



        partial void OnUserCodeChanged(string value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(PortalUrlTemplate))
            {
                AuthorizationUrl = string.Empty;
                return;
            }
            
            try
            {
                // Dynamically format the regional URL using the template from appsettings
                AuthorizationUrl = string.Format(PortalUrlTemplate, Uri.EscapeDataString(value));
            }
            catch (Exception ex)
            {
                AuthorizationUrl = "Errore nel formato del link regionale.";
                Log.Error(ex, "Failed to format regional BMW Portal URL.");
            }
        }

        partial void OnVinChanged(string value)
        {
            SaveSettings();
            ResetFlow();
        }

        partial void OnClientIdChanged(string value)
        {
            SaveSettings();
            ResetFlow();
        }

        private void ResetFlow()
        {
            IsStep2Active = false;
            IsStep3Active = false;
        }

        partial void OnPortalUrlTemplateChanged(string value) => SaveSettings();

        private void SaveSettings()
        {
            try
            {
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (!File.Exists(settingsPath)) return;

                var json = File.ReadAllText(settingsPath);
                var node = System.Text.Json.Nodes.JsonNode.Parse(json);
                var bmwNode = node?["BmwCarData"];
                
                if (bmwNode != null)
                {
                    bmwNode["ClientId"] = ClientId?.Trim();
                    bmwNode["Vin"] = Vin?.Trim();
                    bmwNode["PortalUrlTemplate"] = PortalUrlTemplate?.Trim();

                    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(settingsPath, node.ToJsonString(options));
                    
                    // Log target file change to verify persistence path
                    Log.Information("AppSettings saved successfully to {Path}", settingsPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to persist runtime appsettings changes.");
            }
        }

        [ObservableProperty]
        private string mqttStatusColor = "#FF4444";

        [ObservableProperty]
        private string mqttStatusText = "Disconnected";

        [ObservableProperty]
        private string step1StatusColor = "#666666";

        [ObservableProperty]
        private string step1StatusMessage = "Waiting...";

        [ObservableProperty]
        private string step3StatusColor = "#666666";

        [ObservableProperty]
        private string step3StatusMessage = "Waiting...";

        [ObservableProperty]
        private string containerDeploymentStatus;

        [ObservableProperty]
        private string deleteContainersStatus;

        public MainViewModel(IConfiguration configuration)
        {
            var bmwConfig = configuration.GetSection("BmwCarData");
            clientId = bmwConfig["ClientId"] ?? throw new InvalidOperationException("ClientId not found in appsettings.json");
            _authEndpoint = bmwConfig["AuthEndpoint"] ?? throw new InvalidOperationException("AuthEndpoint not found in appsettings.json");
            _restApiEndpoint = bmwConfig["RestApiEndpoint"] ?? "https://api-cardata.bmwgroup.com";
            _mqttBroker = bmwConfig["MqttBroker"] ?? bmwConfig["Broker"] ?? "customer.streaming-cardata.bmwgroup.com";
            _mqttPort = int.TryParse(bmwConfig["MqttPort"] ?? bmwConfig["Port"], out var port) ? port : 9000;
            _mqttTopic = bmwConfig["Topic"] ?? string.Empty;
            portalUrlTemplate = bmwConfig["PortalUrlTemplate"] ?? "https://customer.bmwgroup.com/oneid/#/link?brand=bmw&user_code={0}";

            // Initialize defaults
            vin = bmwConfig["Vin"] ?? string.Empty;
            Gcid = string.Empty;
            IsDeviceFlowStarted = false;
            IsAuthorized = false;
            IsFetchingTokens = false;
            PollingStatusMessage = string.Empty;
            TokenStatusMessage = string.Empty;
            LiveStreamJson = string.Empty;
            LiveStreamStatusMessage = string.Empty;
            UserCode = string.Empty;
            VerificationUri = string.Empty;
            ContainersJson = string.Empty;
            IsFetchingContainers = false;
            SmartMaintenanceJson = string.Empty;
            IsFetchingSmartMaintenance = false;
            ContainerDeploymentStatus = "Not started. Ready to deploy.";

            C1.OnActiveChanged = OnContainerActiveChanged;
            C2.OnActiveChanged = OnContainerActiveChanged;
            C3.OnActiveChanged = OnContainerActiveChanged;
            C4.OnActiveChanged = OnContainerActiveChanged;
            C5.OnActiveChanged = OnContainerActiveChanged;
            C6.OnActiveChanged = OnContainerActiveChanged;
            C7.OnActiveChanged = OnContainerActiveChanged;
            C8.OnActiveChanged = OnContainerActiveChanged;
            C9.OnActiveChanged = OnContainerActiveChanged;
            C10.OnActiveChanged = OnContainerActiveChanged;
        }

        private void OnContainerActiveChanged(TelematicsContainerItem item, bool isActive)
        {
            if (isActive) _ = ToggleContainerExecutionAsync(item);
            else item.Data = "Pacchetto disattivato.";
        }

        /// <summary>
        /// Initializes services (called after DI resolution).
        /// </summary>
        public void InitializeServices(BmwOAuthService oauthService, BmwRestService restService, BmwMqttService mqttService)
        {
            _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
            _restService = restService ?? throw new ArgumentNullException(nameof(restService));
            _mqttService = mqttService ?? throw new ArgumentNullException(nameof(mqttService));

            // Subscribe to DeviceCodeReady event to update UI with user code and verification URI
            _oauthService.DeviceCodeReady += OnDeviceCodeReady;

            _ = LoadAllContainerIdsAsync();
        }

        private void OnDeviceCodeReady(object? sender, DeviceCodeEventArgs e)
        {
            // Update UI-bound properties when the device code is ready
            UserCode = e.UserCode;
            VerificationUri = e.VerificationUri;
            PollingStatusMessage = $"Please authorize at: {e.VerificationUri} (code: {e.UserCode})";
        }

        [RelayCommand]
        private async Task StartDeviceFlowAsync()
        {
            try
            {
                IsDeviceFlowStarted = true;
                Step1StatusColor = "#FFB81C"; // yellow — in progress
                Step1StatusMessage = "Requesting...";
                PollingStatusMessage = "Requesting device authorization codes...";

                // Only request device_code and user_code — no polling, no token exchange.
                var result = await _oauthService!.RequestDeviceCodeAsync(ClientId, _authEndpoint);

                if (result != null)
                {
                    // Store device code + PKCE verifier for the manual token request in Step 3
                    _deviceCode = result.DeviceCode;
                    _codeVerifier = result.CodeVerifier;

                    UserCode = result.UserCode;
                    VerificationUri = result.VerificationUri;
                    PollingStatusMessage = $"Codes generated! Authorize at the BMW portal, then request your token in Step 3.";

                    Step1StatusColor = "#00AA00"; // green — success
                    Step1StatusMessage = "OK";
                    IsStep2Active = true;
                }
                else
                {
                    PollingStatusMessage = "Failed to obtain device codes. Check your Client ID and try again.";
                    Step1StatusColor = "#FF4444"; // red — error
                    Step1StatusMessage = "Failed";
                    IsDeviceFlowStarted = false;
                }
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == HttpStatusCode.InternalServerError)
            {
                PollingStatusMessage = "500 - Internal server error. The BMW server encountered an unexpected condition. Please try again later.";
                Step1StatusColor = "#FF4444";
                Step1StatusMessage = "500 - Internal Server Error";
                IsDeviceFlowStarted = false;
            }
            catch (Exception ex)
            {
                PollingStatusMessage = $"Error: {ex.Message}";
                Step1StatusColor = "#FF4444";
                Step1StatusMessage = ex.Message;
                IsDeviceFlowStarted = false;
            }
        }

        [RelayCommand]
        private async Task RequestTokenAsync()
        {
            if (string.IsNullOrEmpty(_deviceCode) || string.IsNullOrEmpty(_codeVerifier))
            {
                TokenStatusMessage = "No device code available. Complete Step 1 first.";
                return;
            }

            try
            {
                IsFetchingTokens = true;
                Step3StatusColor = "#FFB81C"; // yellow — in progress
                Step3StatusMessage = "Requesting...";
                TokenStatusMessage = "Requesting access token...";

                var token = await _oauthService!.RequestTokenWithDeviceCodeAsync(
                    ClientId, _authEndpoint, _deviceCode, _codeVerifier);

                if (token != null)
                {
                    _oauthService.SaveToken(token);
                    IsAuthorized = true;
                    Gcid = token.gcid ?? string.Empty;
                    TokenStatusMessage = "Success (authentication successful and token was generated)";
                    PollingStatusMessage = "✓ Device authorized successfully!";

                    Step3StatusMessage = "Token ottenuto con successo! (HTTP 200)";
                    Step3StatusColor = "#00AA00"; // Green

                    // TRIGGER IMMEDIATELY AFTER POSITIVE RESULT
                    await FetchVehicleImageBackgroundAsync(token.access_token);
                    _ = LoadAllContainerIdsAsync();
                }
                else
                {
                    TokenStatusMessage = "Authorization not yet completed — try again after authorizing in the browser.";
                    Step3StatusColor = "#FF4444"; // red — error
                    Step3StatusMessage = "authorization_pending";
                }
            }
            catch (HttpRequestException httpEx)
            {
                Step3StatusColor = "#FF4444";
                int? statusCode = (int?)httpEx.StatusCode;
                Log.Warning("HTTP Request failed with status code {StatusCode}: {Message}", statusCode, httpEx.Message);
                
                if (httpEx.StatusCode == HttpStatusCode.BadRequest)
                {
                    Step3StatusMessage = "400 - Bad Request";
                    TokenStatusMessage = "Bad Request: malformed or polling faster than allowed";
                }
                else if (httpEx.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Step3StatusMessage = "401 - Unauthorized";
                    TokenStatusMessage = "Unauthorized: invalid client or expired code - can also mean that the account requesting a token is pending activation";
                }
                else if (httpEx.StatusCode == HttpStatusCode.Forbidden)
                {
                    Step3StatusMessage = "403 - Forbidden";
                    TokenStatusMessage = "Forbidden: user has not completed authorization or denied access";
                }
                else
                {
                    Step3StatusMessage = $"{statusCode} - Error";
                    TokenStatusMessage = $"Error: {httpEx.Message}";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error requesting token");
                TokenStatusMessage = $"Error: {ex.Message}";
                Step3StatusColor = "#FF4444";
                Step3StatusMessage = "Error";
            }
            finally
            {
                IsFetchingTokens = false;
            }
        }

        private async Task FetchVehicleImageBackgroundAsync(string accessToken)
        {
            if (string.IsNullOrEmpty(Vin)) return;

            try
            {
                Log.Information("Scaricamento immagine veicolo per VIN: {Vin}", Vin);
                
                // 1. Get raw bytes
                byte[] imageBytes = await _restService!.GetVehicleImageBytesAsync(Vin, accessToken, _restApiEndpoint);

                // 2. Convert to BitmapImage safely on the UI Thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(imageBytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze(); // Crucial for WPF cross-thread data binding
                    VehicleImage = bitmap;
                });
                
                Log.Information("Immagine veicolo caricata e renderizzata con successo.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore nel caricamento dell'immagine del veicolo.");
            }
        }

        [RelayCommand]
        private void OpenBmwPortal()
        {
            if (!string.IsNullOrEmpty(AuthorizationUrl))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AuthorizationUrl,
                    UseShellExecute = true
                });
                Log.Information("Opened BMW Portal URL: {Url}", AuthorizationUrl);
                IsStep3Active = true;
            }
        }

        private async Task FetchVehicleDataAsync(BmwTokenResponse token)
        {
            try
            {
                IsFetchingTokens = true;
                TokenStatusMessage = "Fetching vehicle data...";

                var success = await _restService!.FetchAndSaveVehicleDataAsync(
                    Vin,
                    token.access_token!,
                    _restApiEndpoint
                );

                if (success)
                {
                    TokenStatusMessage = "✓ Vehicle data fetched and saved!";
                    await StartMqttStreamingAsync(token);
                }
                else
                {
                    TokenStatusMessage = "Failed to fetch vehicle data.";
                }
            }
            catch (Exception ex)
            {
                TokenStatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsFetchingTokens = false;
            }
        }

        [RelayCommand]
        private async Task GetContainersAsync()
        {
            var token = _oauthService!.LoadCachedToken();
            if (token == null || string.IsNullOrEmpty(token.access_token))
            {
                GetContainersStatusMessage = "Error";
                ContainersJson = "No valid token available. Complete Step 3 first.";
                GetContainersStatusColor = "#FF4444";
                return;
            }

            try
            {
                IsFetchingContainers = true;
                GetContainersStatusColor = "#FFB81C"; // yellow — in progress
                GetContainersStatusMessage = "Requesting...";
                ContainersJson = "Fetching containers...";

                var json = await _restService!.GetContainersAsync(token.access_token, _restApiEndpoint);

                ContainersJson = json;
                GetContainersStatusColor = "#00AA00";
                GetContainersStatusMessage = "200 - OK";
            }
            catch (HttpRequestException httpEx)
            {
                GetContainersStatusColor = "#FF4444";
                
                if (httpEx.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    GetContainersStatusMessage = "400 - Bad Request";
                    ContainersJson = "Bad request. Please check API specification";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    GetContainersStatusMessage = "401 - Unauthorized";
                    ContainersJson = "Authentication Failed";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    GetContainersStatusMessage = "403 - Forbidden";
                    ContainersJson = "Access to resource is forbidden";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    GetContainersStatusMessage = "404 - Not Found";
                    ContainersJson = "Not found";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    GetContainersStatusMessage = "500 - Internal Server Error";
                    ContainersJson = "A permanent server error occurred. Report this error if it occurs";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    GetContainersStatusMessage = "503 - Service Unavailable";
                    ContainersJson = "A temporary server error occurred. Retry again later or report this error if it persists.";
                }
                else
                {
                    GetContainersStatusMessage = $"{(int?)httpEx.StatusCode} - Error";
                    ContainersJson = $"Error: {httpEx.Message}";
                }
            }
            catch (Exception ex)
            {
                GetContainersStatusColor = "#FF4444";
                GetContainersStatusMessage = "Error";
                ContainersJson = $"Error: {ex.Message}";
            }
            finally
            {
                IsFetchingContainers = false;
            }
        }

        [RelayCommand]
        private async Task GetSmartMaintenanceAsync()
        {
            var token = _oauthService!.LoadCachedToken();
            if (token == null || string.IsNullOrEmpty(token.access_token))
            {
                GetSmartMaintenanceStatusMessage = "Error";
                SmartMaintenanceJson = "No valid token available. Complete Step 3 first.";
                GetSmartMaintenanceStatusColor = "#FF4444";
                return;
            }

            if (string.IsNullOrEmpty(Vin))
            {
                GetSmartMaintenanceStatusMessage = "Error";
                SmartMaintenanceJson = "No VIN provided. Enter VIN in Step 1.";
                GetSmartMaintenanceStatusColor = "#FF4444";
                return;
            }

            try
            {
                IsFetchingSmartMaintenance = true;
                GetSmartMaintenanceStatusColor = "#FFB81C"; // yellow — in progress
                GetSmartMaintenanceStatusMessage = "Requesting...";
                SmartMaintenanceJson = "Fetching smart maintenance tyre diagnosis...";

                var json = await _restService!.GetSmartMaintenanceTyreDiagnosisAsync(Vin, token.access_token, _restApiEndpoint);

                SmartMaintenanceJson = json;
                GetSmartMaintenanceStatusColor = "#00AA00";
                GetSmartMaintenanceStatusMessage = "200 - OK";
            }
            catch (HttpRequestException httpEx)
            {
                GetSmartMaintenanceStatusColor = "#FF4444";
                
                if (httpEx.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    GetSmartMaintenanceStatusMessage = "400 - Bad Request";
                    SmartMaintenanceJson = "Bad request. Please check API specification";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    GetSmartMaintenanceStatusMessage = "401 - Unauthorized";
                    SmartMaintenanceJson = "Authentication Failed";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    GetSmartMaintenanceStatusMessage = "403 - Forbidden";
                    SmartMaintenanceJson = "Access to resource is forbidden";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    GetSmartMaintenanceStatusMessage = "404 - Not Found";
                    SmartMaintenanceJson = "Not found";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    GetSmartMaintenanceStatusMessage = "500 - Internal Server Error";
                    SmartMaintenanceJson = "A permanent server error occurred. Report this error if it occurs";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    GetSmartMaintenanceStatusMessage = "503 - Service Unavailable";
                    SmartMaintenanceJson = "A temporary server error occurred. Retry again later or report this error if it persists.";
                }
                else
                {
                    GetSmartMaintenanceStatusMessage = $"{(int?)httpEx.StatusCode} - Error";
                    SmartMaintenanceJson = $"Error: {httpEx.Message}";
                }
            }
            catch (Exception ex)
            {
                GetSmartMaintenanceStatusColor = "#FF4444";
                GetSmartMaintenanceStatusMessage = "Error";
                SmartMaintenanceJson = $"Error: {ex.Message}";
            }
            finally
            {
                IsFetchingSmartMaintenance = false;
            }
        }



        [ObservableProperty]
        private string basicDataStatusMessage = string.Empty;

        [ObservableProperty]
        private string basicDataStatusColor = "#666666";

        [ObservableProperty]
        private ObservableCollection<VehicleProperty> basicVehicleDataList = new();

        [RelayCommand]
        private async Task LoadBasicVehicleDataAsync()
        {
            var token = _oauthService!.LoadCachedToken();
            if (token == null || string.IsNullOrEmpty(token.access_token))
            {
                BasicDataStatusColor = "#FF4444";
                BasicDataStatusMessage = "Errore: Token non disponibile. Effettua il login.";
                return;
            }

            try
            {
                BasicDataStatusColor = "#FFB81C"; // Yellow
                BasicDataStatusMessage = "Scaricamento dati statici in corso...";
                string rawJson = await _restService!.GetBasicVehicleDataAsync(Vin, token.access_token, _restApiEndpoint);
                
                using var doc = JsonDocument.Parse(rawJson);
                BasicVehicleDataList.Clear();
                
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    string key = property.Name;
                    // Formatta la chiave come "bodyType" -> "BodyType"
                    if (key.Length > 0)
                    {
                        key = char.ToUpper(key[0]) + key.Substring(1);
                    }

                    string valueStr = property.Value.ToString();

                    BasicVehicleDataList.Add(new VehicleProperty 
                    { 
                        Key = key, 
                        Value = valueStr
                    });
                }
                BasicDataStatusColor = "#00AA00"; // Green
                BasicDataStatusMessage = "200 A specific list of telematic key and values was returned.";
            }
            catch (HttpRequestException httpEx)
            {
                BasicDataStatusColor = "#FF4444";
                
                if (httpEx.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    BasicDataStatusMessage = "400 - Bad Request";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    BasicDataStatusMessage = "401 - Unauthorized";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    BasicDataStatusMessage = "403 - Forbidden";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    BasicDataStatusMessage = "404 - Not Found";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    BasicDataStatusMessage = "500 - Internal Server Error";
                }
                else if (httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    BasicDataStatusMessage = "503 - Service Unavailable";
                }
                else
                {
                    BasicDataStatusMessage = $"{(int?)httpEx.StatusCode} - Error: {httpEx.Message}";
                }
                BasicVehicleDataList.Clear();
            }
            catch (Exception ex)
            {
                BasicDataStatusColor = "#FF4444";
                BasicDataStatusMessage = $"Errore: {ex.Message}";
                BasicVehicleDataList.Clear();
            }
        }

        [RelayCommand]
        private async Task LoadInitialStateAndStreamAsync()
        {
            var token = _oauthService!.LoadCachedToken();
            if (token == null || string.IsNullOrEmpty(token.access_token))
            {
                LiveStreamJson = "Errore: Nessun token valido trovato. Effettua prima il login nello Step 3.";
                return;
            }

            if (string.IsNullOrEmpty(Vin))
            {
                LiveStreamJson = "Errore: Inserisci un VIN valido per procedere.";
                return;
            }

            try
            {
                LiveStreamStatusMessage = "🟡 Connessione al broker in corso...";
                
                // 2. HANDOFF TO CONTINUOUS STREAMING (MQTT)
                var connectionResult = await StartMqttStreamingAsync(token);

                // Aggiorna la UI con il messaggio reale restituito dal server
                LiveStreamStatusMessage = connectionResult.Message;
                
                if (!connectionResult.IsSuccess)
                {
                    LiveStreamJson = "Connessione fallita. Controlla il messaggio di stato sopra per i dettagli forniti dal server BMW.";
                }
            }
            catch (Exception ex) when (ex.Message.Contains("403") || ex.Message.Contains("Forbidden"))
            {
                Log.Warning("Streaming negato (403): il veicolo è in sleep mode.");
                LiveStreamStatusMessage = "🔴 Accesso negato o errore di rete.";
                LiveStreamJson = "⚠️ ACCESSO NEGATO (Veicolo in Sleep Mode)\n\n" +
                                 "Per proteggere la batteria della tua BMW, lo streaming continuo viene bloccato dai server quando l'auto è spenta da troppo tempo.\n\n" +
                                 "👉 AZIONE RICHIESTA: Accendi il quadro strumenti o avvia il motore della tua auto, attendi 5 secondi e clicca di nuovo su 'Avvia Streaming'.";
                MqttStatusText = "REST Error (403)";
                MqttStatusColor = "#FF4444"; // Red
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore generico durante l'avvio dello streaming.");
                LiveStreamStatusMessage = "🔴 Errore imprevisto durante lo streaming.";
                LiveStreamJson = $"Dettagli: {ex.Message}";
                MqttStatusText = "REST Error";
                MqttStatusColor = "#FF4444"; // Red
            }
        }

        private async Task<(bool IsSuccess, string Message)> StartMqttStreamingAsync(BmwTokenResponse token)
        {
            try
            {
                MqttStatusText = "Connecting to MQTT...";
                MqttStatusColor = "#FFB81C";

                _mqttCts?.Cancel();
                _mqttCts = new CancellationTokenSource();

                var topic = !string.IsNullOrEmpty(_mqttTopic)
                    ? _mqttTopic
                    : $"customers/vehicles/{Vin}/stream";

                var clientId = $"BmwCarDataClient-{Guid.NewGuid().ToString().Substring(0, 8)}";

                // We try ID Token first as recommended for streaming password, and fallback to access_token if needed
                var password = string.IsNullOrEmpty(token.id_token) ? token.access_token : token.id_token;

                var options = new MQTTnet.Client.MqttClientOptionsBuilder()
                    .WithTcpServer(_mqttBroker, _mqttPort)
                    .WithCredentials(token.gcid ?? Gcid, password)
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                    .WithClientId(clientId)
                    .WithCleanSession(true)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                    .WithTimeout(TimeSpan.FromSeconds(10))
                    .WithTlsOptions(o => o.UseTls())
                    .Build();

                var result = await _mqttService!.ConnectToStreamWithResultAsync(options, topic, _mqttCts.Token);

                MqttStatusText = result.Message;
                MqttStatusColor = result.IsSuccess ? "#00AA00" : "#FF4444";
                
                return result;
            }
            catch (OperationCanceledException)
            {
                MqttStatusText = "Disconnected";
                MqttStatusColor = "#FF4444";
                return (false, "🔴 Operazione annullata o disconnessa.");
            }
            catch (Exception ex)
            {
                MqttStatusText = $"Error: {ex.Message}";
                MqttStatusColor = "#FF4444";
                return (false, $"🔴 Errore: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CopyCode()
        {
            if (!string.IsNullOrEmpty(UserCode))
            {
                System.Windows.Clipboard.SetText(UserCode);
                PollingStatusMessage = "User code copied to clipboard!";
            }
        }

        [RelayCommand]
        private async Task DeployContainersAsync()
        {
            var token = _oauthService!.LoadCachedToken();
            if (token == null || string.IsNullOrEmpty(token.access_token))
            {
                ContainerDeploymentStatus = "Error: No valid token available. Complete Step 3 first.";
                return;
            }

            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "StreamingData.csv");
            if (!File.Exists(filePath))
            {
                // Try parent folders up to 4 levels up for development environments
                var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                bool found = false;
                for (int i = 0; i < 4; i++)
                {
                    if (currentDir == null) break;
                    var testPath = Path.Combine(currentDir.FullName, "docs", "StreamingData.csv");
                    if (File.Exists(testPath))
                    {
                        filePath = testPath;
                        found = true;
                        break;
                    }
                    currentDir = currentDir.Parent;
                }

                if (!found)
                {
                    ContainerDeploymentStatus = "Error: docs/StreamingData.csv not found in output directory or parent directories.";
                    return;
                }
            }

            ContainerDeploymentStatus = $"Reading CSV file: {System.IO.Path.GetFileName(filePath)}...";

            try
            {
                var lines = await System.IO.File.ReadAllLinesAsync(filePath);
                if (lines.Length == 0)
                {
                    ContainerDeploymentStatus = "Error: The selected CSV file is empty.";
                    return;
                }

                // Parse columns
                var headerParts = lines[0].Split(';');
                int keyIndex = Array.FindIndex(headerParts, part => part.Trim().Equals("Technischer Bezeichner", StringComparison.OrdinalIgnoreCase));
                if (keyIndex == -1)
                {
                    ContainerDeploymentStatus = "Error: Column 'Technischer Bezeichner' not found in CSV header.";
                    return;
                }

                // 1. Parse and Validate Keys
                var keys = new List<string>();
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(';');
                    
                    if (parts.Length > keyIndex)
                    {
                        var cleanKey = parts[keyIndex].Trim().Trim('"');
                        
                        // CRITICAL FIX: Validate it's an actual BMW key (starts with "vehicle." and has no spaces)
                        if (cleanKey.StartsWith("vehicle.") && !cleanKey.Contains(" "))
                        {
                            keys.Add(cleanKey);
                        }
                    }
                }

                if (keys.Count == 0)
                {
                    ContainerDeploymentStatus = "Error: No valid keys found in CSV.";
                    return;
                }

                // 2. Dynamic Container Dictionary Mapping
                var containerDictionary = new Dictionary<string, BmwContainer>();

                foreach (var key in keys)
                {
                    // Extract the category (e.g., "vehicle.cabin.seat.heating" -> "cabin")
                    var nodes = key.Split('.');
                    if (nodes.Length < 2) continue; // Skip malformed keys

                    string rawCategory = nodes[1];
                    string displayName = char.ToUpper(rawCategory[0]) + rawCategory.Substring(1) + " Domain"; // E.g., "Cabin Domain"

                    // Initialize the container if it doesn't exist
                    if (!containerDictionary.ContainsKey(rawCategory))
                    {
                        containerDictionary[rawCategory] = new BmwContainer
                        {
                            Name = displayName,
                            Purpose = $"Subscription container for {rawCategory} telemetry data.",
                            TechnicalDescriptors = new List<string>()
                        };
                    }

                    // Add the key to the corresponding category container
                    containerDictionary[rawCategory].TechnicalDescriptors.Add(key);
                }

                // Convert dictionary values back to the list expected by the rest of the code
                var containers = containerDictionary.Values.ToList();

                ContainerDeploymentStatus = $"Parsed {keys.Count} valid keys. Grouped into {containers.Count} dynamic domain containers.";

                ContainerDeploymentStatus = $"Generated {containers.Count} subscription containers. Provisioning on BMW API...";

                int successCount = 0;
                int failCount = 0;

                foreach (var container in containers)
                {
                    // 1. MUST CHECK FOR EMPTY DESCRIPTORS TO PREVENT CU-401 API ERROR
                    if (container.TechnicalDescriptors == null || container.TechnicalDescriptors.Count == 0)
                    {
                        ContainerDeploymentStatus += $"\n\n[SKIP] Container '{container.Name}' ignorato: nessuna chiave presente nel CSV per questa categoria.";
                        continue; // Skip the API call completely
                    }

                    ContainerDeploymentStatus += $"\n\n[DEPLOYING] Container '{container.Name}' with {container.TechnicalDescriptors.Count} telemetry keys...";

                    var payload = new
                    {
                        name = container.Name,
                        purpose = container.Purpose,
                        technicalDescriptors = container.TechnicalDescriptors
                    };
                    var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);

                    try
                    {
                        var resultJson = await _restService!.CreateContainerAsync(token.access_token, _restApiEndpoint, jsonPayload);
                        ContainerDeploymentStatus += $"\n✓ Success: Provisioned container '{container.Name}'. Response: {resultJson}";
                        successCount++;
                    }
                    catch (HttpRequestException httpEx)
                    {
                        ContainerDeploymentStatus += $"\n✗ Failed. Status: {httpEx.StatusCode}. Details: {httpEx.Message}";
                        failCount++;
                    }
                    catch (Exception ex)
                    {
                        ContainerDeploymentStatus += $"\n✗ Error: {ex.Message}";
                        failCount++;
                    }
                }

                ContainerDeploymentStatus += $"\n\n==================================================";
                ContainerDeploymentStatus += $"\nDeployment finished: {successCount} succeeded, {failCount} failed.";
                ContainerDeploymentStatus += $"\n==================================================";
            }
            catch (Exception ex)
            {
                ContainerDeploymentStatus = $"Deployment aborted due to read/parse error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task DeleteAllContainersAsync()
        {
            var token = _oauthService!.LoadCachedToken();
            if (token == null || string.IsNullOrEmpty(token.access_token)) return;

            DeleteContainersStatus = "Fetching containers to delete...";
            try
            {
                // 1. Get current containers
                var json = await _restService!.GetContainersAsync(token.access_token, _restApiEndpoint);
                
                // Very basic parsing to find "name":"ContainerName"
                var names = new System.Collections.Generic.List<string>();
                var matches = System.Text.RegularExpressions.Regex.Matches(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    names.Add(match.Groups[1].Value);
                }

                if (names.Count == 0)
                {
                    DeleteContainersStatus = "No containers found to delete.";
                    return;
                }

                DeleteContainersStatus = $"Found {names.Count} containers. Deleting...\n";
                
                // 2. Delete them
                foreach (var name in names)
                {
                    bool success = await _restService.DeleteContainerAsync(name, token.access_token, _restApiEndpoint);
                    DeleteContainersStatus += success ? $"\n✓ Deleted: {name}" : $"\n✗ Failed to delete: {name}";
                }
                DeleteContainersStatus += "\n\nCleanup complete. You can now Deploy new containers!";
            }
            catch (Exception ex)
            {
                DeleteContainersStatus = $"Error during deletion: {ex.Message}";
            }
        }

        [ObservableProperty] private TelematicsContainerItem c1 = new() { Name = "Cabin", ContainerId = "Cabin Domain" };
        [ObservableProperty] private TelematicsContainerItem c2 = new() { Name = "Drivetrain", ContainerId = "Drivetrain Domain" };
        [ObservableProperty] private TelematicsContainerItem c3 = new() { Name = "Powertrain", ContainerId = "Powertrain Domain" };
        [ObservableProperty] private TelematicsContainerItem c4 = new() { Name = "Body", ContainerId = "Body Domain" };
        [ObservableProperty] private TelematicsContainerItem c5 = new() { Name = "Chassis", ContainerId = "Chassis Domain" };
        [ObservableProperty] private TelematicsContainerItem c6 = new() { Name = "Trip", ContainerId = "Trip Domain" };
        [ObservableProperty] private TelematicsContainerItem c7 = new() { Name = "Status", ContainerId = "Status Domain" };
        [ObservableProperty] private TelematicsContainerItem c8 = new() { Name = "Channel", ContainerId = "Channel Domain" };
        [ObservableProperty] private TelematicsContainerItem c9 = new() { Name = "ElectricalSystem", ContainerId = "Electrical System Domain" };
        [ObservableProperty] private TelematicsContainerItem c10 = new() { Name = "Vehicle", ContainerId = "Vehicle Domain" };

        [ObservableProperty]
        private int freeRequestsRemaining = 10;

        [ObservableProperty]
        private string premiumToken = string.Empty;

        [ObservableProperty]
        private bool isPremiumUnlocked = false;

        private bool CanMakeRequest()
        {
            if (IsPremiumUnlocked) return true;
            
            if (FreeRequestsRemaining > 0)
            {
                FreeRequestsRemaining--;
                // TODO: Save FreeRequestsRemaining to local settings/Preferences to persist across restarts
                return true;
            }
            
            return false;
        }

        partial void OnPremiumTokenChanged(string value)
        {
            // Basic hardcoded validation for MVP (can be improved later)
            if (value == "SUPER-SECRET-PREMIUM-KEY")
            {
                IsPremiumUnlocked = true;
                Log.Information("Premium Token accepted. Features unlocked.");
            }
        }

        public async Task LoadAllContainerIdsAsync()
        {
            var token = _oauthService!.LoadCachedToken();
            if (token == null || string.IsNullOrEmpty(token.access_token)) return;

            try
            {
                // Una sola chiamata per prendere tutta la lista
                string containersJson = await _restService!.GetContainersAsync(token.access_token, _restApiEndpoint);
                
                using var doc = JsonDocument.Parse(containersJson);
                if (doc.RootElement.TryGetProperty("containers", out var containersArray))
                {
                    foreach (var container in containersArray.EnumerateArray())
                    {
                        string name = container.GetProperty("name").GetString() ?? string.Empty;
                        string id = container.GetProperty("containerId").GetString() ?? string.Empty;
                        string state = container.GetProperty("state").GetString() ?? string.Empty;

                        if (state == "ACTIVE")
                        {
                            // Assegna istantaneamente l'ID al pacchetto corretto nella UI
                            MapIdToContainerItem(name, id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore durante il pre-caricamento dei Container ID.");
            }
        }

        private void MapIdToContainerItem(string bmwName, string id)
        {
            if (bmwName.StartsWith("Cabin", StringComparison.OrdinalIgnoreCase)) C1.ContainerId = id;
            else if (bmwName.StartsWith("Drivetrain", StringComparison.OrdinalIgnoreCase)) C2.ContainerId = id;
            else if (bmwName.StartsWith("Powertrain", StringComparison.OrdinalIgnoreCase)) C3.ContainerId = id;
            else if (bmwName.StartsWith("Body", StringComparison.OrdinalIgnoreCase)) C4.ContainerId = id;
            else if (bmwName.StartsWith("Chassis", StringComparison.OrdinalIgnoreCase)) C5.ContainerId = id;
            else if (bmwName.StartsWith("Trip", StringComparison.OrdinalIgnoreCase)) C6.ContainerId = id;
            else if (bmwName.StartsWith("Status", StringComparison.OrdinalIgnoreCase)) C7.ContainerId = id;
            else if (bmwName.StartsWith("Channel", StringComparison.OrdinalIgnoreCase)) C8.ContainerId = id;
            else if (bmwName.StartsWith("ElectricalSystem", StringComparison.OrdinalIgnoreCase)) C9.ContainerId = id;
            else if (bmwName.StartsWith("Vehicle", StringComparison.OrdinalIgnoreCase)) C10.ContainerId = id;
        }

        private async Task ToggleContainerExecutionAsync(TelematicsContainerItem item)
        {
            if (!item.IsActive)
            {
                item.Data = "Pacchetto disattivato.";
                return;
            }

            if (!CanMakeRequest())
            {
                item.Data = "⚠️ Crediti esauriti. Sblocca la versione Premium.";
                item.IsActive = false;
                return;
            }

            if (string.IsNullOrEmpty(item.ContainerId) || item.ContainerId == "Non caricato" || item.ContainerId == "NON TROVATO" || item.ContainerId.EndsWith(" Domain"))
            {
                item.Data = "⚠️ Container ID non disponibile. Prova a ricaricare i dati iniziali.";
                item.IsActive = false;
                return;
            }

            var token = _oauthService!.LoadCachedToken();
            if (token == null || string.IsNullOrEmpty(token.access_token)) return;

            try
            {
                item.Data = "Scaricamento dati in corso...";
                // Usa direttamente l'ID già presente nell'header!
                item.Data = await _restService!.GetTelematicDataForContainerAsync(Vin, item.ContainerId, token.access_token, _restApiEndpoint);
            }
            catch (Exception ex)
            {
                item.Data = $"❌ Errore: {ex.Message}";
                item.IsActive = false;
            }
        }
    }



    public class VehicleProperty
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public partial class TelematicsContainerItem : ObservableObject
    {
        [ObservableProperty] private string name = string.Empty;
        [ObservableProperty] private string containerId = string.Empty;
        [ObservableProperty] private string data = string.Empty;
        [ObservableProperty] private bool isActive;
        
        public Action<TelematicsContainerItem, bool>? OnActiveChanged { get; set; }

        partial void OnIsActiveChanged(bool value)
        {
            OnActiveChanged?.Invoke(this, value);
        }
    }
}
