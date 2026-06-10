using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace BmwCarDataClient
{
    public partial class SettingsSetupWindow : Window
    {
        public bool IsConfigurationSaved { get; private set; } = false;

        public SettingsSetupWindow()
        {
            InitializeComponent();
            LoadTemplateDefaults();
        }

        private void LoadTemplateDefaults()
        {
            var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.template.json");
            if (File.Exists(templatePath))
            {
                try
                {
                    var json = File.ReadAllText(templatePath);
                    var node = JsonNode.Parse(json);
                    var bmwNode = node?["BmwCarData"];
                    if (bmwNode != null)
                    {
                        if (bmwNode["ClientId"] != null) txtClientId.Text = bmwNode["ClientId"]?.ToString();
                        if (bmwNode["Vin"] != null) txtVin.Text = bmwNode["Vin"]?.ToString();
                        if (bmwNode["Broker"] != null) txtBroker.Text = bmwNode["Broker"]?.ToString();
                        if (bmwNode["Port"] != null) txtPort.Text = bmwNode["Port"]?.ToString();
                        if (bmwNode["Username"] != null) txtUsername.Text = bmwNode["Username"]?.ToString();
                        if (bmwNode["Topic"] != null) txtTopic.Text = bmwNode["Topic"]?.ToString();
                        if (bmwNode["AuthEndpoint"] != null) txtAuthEndpoint.Text = bmwNode["AuthEndpoint"]?.ToString();
                        if (bmwNode["RestApiEndpoint"] != null) txtRestApiEndpoint.Text = bmwNode["RestApiEndpoint"]?.ToString();
                    }
                }
                catch { /* Ignore template load errors, defaults from XAML will be used */ }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Senza configurazione l'applicazione non può avviarsi. Sicuro di voler uscire?",
                "Conferma Uscita", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Close();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtClientId.Text) || string.IsNullOrWhiteSpace(txtVin.Text))
            {
                MessageBox.Show("Client ID e VIN sono obbligatori.", "Errore di validazione", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.template.json");
                JsonNode rootNode;

                if (File.Exists(templatePath))
                {
                    rootNode = JsonNode.Parse(File.ReadAllText(templatePath));
                }
                else
                {
                    rootNode = JsonNode.Parse("{\"BmwCarData\": {}}");
                }

                var bmwNode = rootNode["BmwCarData"];
                if (bmwNode == null)
                {
                    bmwNode = new JsonObject();
                    rootNode["BmwCarData"] = bmwNode;
                }

                // Update values
                bmwNode["ClientId"] = txtClientId.Text.Trim();
                bmwNode["Vin"] = txtVin.Text.Trim();
                bmwNode["Broker"] = txtBroker.Text.Trim();
                bmwNode["MqttBroker"] = txtBroker.Text.Trim(); // Retro-compatibility
                if (int.TryParse(txtPort.Text.Trim(), out int port))
                {
                    bmwNode["Port"] = port;
                    bmwNode["MqttPort"] = port;
                }
                bmwNode["Username"] = txtUsername.Text.Trim();
                bmwNode["Topic"] = txtTopic.Text.Trim();
                bmwNode["AuthEndpoint"] = txtAuthEndpoint.Text.Trim();
                bmwNode["RestApiEndpoint"] = txtRestApiEndpoint.Text.Trim();
                bmwNode["UseTls"] = true; // Default

                // Save to appsettings.json
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(settingsPath, rootNode.ToJsonString(options));

                IsConfigurationSaved = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante il salvataggio: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
