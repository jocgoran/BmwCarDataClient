using System;
using System.IO;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;

namespace BmwCarDataClient
{
    public class BmwMqttService
    {
        private readonly string _logFilePath;
        private IMqttClient? _mqttClient;
        private readonly MqttFactory _mqttFactory = new MqttFactory();
        private MqttClientOptions? _currentOptions;
        private string? _currentTopic;

        /// <summary>Optional progress reporter for GUI log integration.</summary>
        public IProgress<string>? Log { get; set; }

        /// <summary>Raised on the thread-pool whenever a new MQTT message arrives.</summary>
        public event Action<string, string>? MessageReceived; // (topic, payload)

        public BmwMqttService()
        {
            // Log file consolidated into bmw_application.log (same as OAuth and Serilog)
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            _logFilePath = Path.Combine(logDir, "bmw_application.log");
        }

        private void WriteLog(string message)
        {
            Console.WriteLine(message);
            Log?.Report(message);
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(_logFilePath, $"[{timestamp}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private void LogMqttConnectionAttempt(string broker, int port, string gcid, string password, string clientId, MqttProtocolVersion protocolVersion)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("========================================================================");
            sb.AppendLine("[MQTT CONNECTION REQUEST]");
            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine($"Broker Address: {broker}:{port}");
            sb.AppendLine($"Protocol: {protocolVersion}");
            sb.AppendLine($"Client ID: {clientId}");
            sb.AppendLine($"Username (GCID): {gcid}");
            var maskedPassword = string.IsNullOrEmpty(password) ? "" : (password.Length > 20 ? password.Substring(0, 15) + "..." : "[MASKED]");
            sb.AppendLine($"Password (JWT Token): {maskedPassword}");
            sb.AppendLine("Clean Session: True");
            sb.AppendLine("Keep Alive: 60 seconds");
            sb.AppendLine("TLS Configuration: Enabled (Secure Defaults)");
            sb.AppendLine("========================================================================");
            WriteLog(sb.ToString());
        }

        private void LogMqttSubscriptionAttempt(string topic)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("========================================================================");
            sb.AppendLine("[MQTT SUBSCRIPTION REQUEST]");
            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine($"Topic: {topic}");
            sb.AppendLine("QoS Level: QoS 0 (AtMostOnce)");
            sb.AppendLine("========================================================================");
            WriteLog(sb.ToString());
        }

        private void SetupMqttEventHandlers()
        {
            // Set up message handler
            _mqttClient!.ApplicationMessageReceivedAsync += async e =>
            {
                var msgTopic = e.ApplicationMessage.Topic;
                var payloadBytes = e.ApplicationMessage.PayloadSegment.ToArray();
                var payload = Encoding.UTF8.GetString(payloadBytes);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[{timestamp}] [MQTT Message Received]");
                Console.ResetColor();
                Console.WriteLine($"Topic: {msgTopic}");
                Console.WriteLine($"Payload:\n{payload}");

                // Notify GUI via progress and event
                Log?.Report($"[MQTT] {timestamp} | {msgTopic}");
                Log?.Report($"  {payload}");
                MessageReceived?.Invoke(msgTopic, payload);

                // Log to file in structured format
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("========================================================================");
                sb.AppendLine($"[MQTT MESSAGE RECEIVED] Topic: {msgTopic}");
                sb.AppendLine("------------------------------------------------------------------------");
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var formattedPayload = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                    sb.AppendLine(formattedPayload);
                }
                catch
                {
                    sb.AppendLine(payload);
                }
                sb.AppendLine("========================================================================");

                try
                {
                    await File.AppendAllTextAsync(_logFilePath, $"[{timestamp}] {sb.ToString()}{Environment.NewLine}");
                }
                catch { }
            };

            // Set up disconnection handler
            _mqttClient.DisconnectedAsync += async e =>
            {
                WriteLog($"[Warning] MQTT Disconnected! Reason: {e.Reason}");
                if (e.Exception != null) WriteLog($"[Debug] Exception: {e.Exception.Message}");
                await Task.CompletedTask;
            };
        }

        public async Task<(bool IsSuccess, string Message)> ConnectToStreamWithResultAsync(MqttClientOptions options, string topic, CancellationToken cancellationToken)
        {
            if (_mqttClient == null)
            {
                _mqttClient = _mqttFactory.CreateMqttClient();
                SetupMqttEventHandlers();
            }

            try
            {
                WriteLog("[Info] Tentativo di connessione al Broker MQTT BMW...");
                var result = await _mqttClient.ConnectAsync(options, cancellationToken);

                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    _currentOptions = options;
                    _currentTopic = topic;

                    // Sottoscrizione al topic
                    var subscribeOptions = _mqttFactory.CreateSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic(topic).WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce))
                        .Build();
                    
                    await _mqttClient.SubscribeAsync(subscribeOptions, cancellationToken);
                    return (true, "🟢 Connesso con successo allo stream live BMW.");
                }

                // Il server ha risposto esplicitamente rifiutando la connessione
                string detailedReason = $"Codice: {result.ResultCode}";
                if (!string.IsNullOrEmpty(result.ReasonString))
                {
                    detailedReason += $" - Motivazione: {result.ReasonString}";
                }

                string userFriendlyMessage = result.ResultCode switch
                {
                    MqttClientConnectResultCode.BadUserNameOrPassword => "🔴 Errore 401: ClientID o Token non validi.",
                    MqttClientConnectResultCode.NotAuthorized => "🔴 Errore 403: Accesso non autorizzato allo stream di questo veicolo.",
                    MqttClientConnectResultCode.ServerUnavailable => "🔴 Server MQTT BMW temporaneamente non raggiungibile.",
                    _ => $"🔴 Connessione rifiutata dal server BMW ({detailedReason})"
                };

                WriteLog($"[Error] Connessione MQTT fallita: {userFriendlyMessage}");
                return (false, userFriendlyMessage);
            }
            catch (Exception ex)
            {
                WriteLog($"[Exception] Errore di rete durante la connessione MQTT: {ex.Message}");
                return (false, $"❌ Errore di rete o configurazione: {ex.Message}");
            }
        }

        // Update credentials and reconnect using the existing client instance
        public async Task<bool> UpdateCredentialsAsync(string broker, int port, string gcid, string newPassword, CancellationToken cancellationToken)
        {
            if (_mqttClient == null)
            {
                WriteLog("[Warning] MQTT client not started yet. Call StartStreamingAsync first.");
                return false;
            }

            if (_currentOptions == null)
            {
                WriteLog("[Warning] Current MQTT options unknown. Reconnect not possible.");
                return false;
            }

            try
            {
                var clientId = $"BmwCarDataClient-{Guid.NewGuid().ToString().Substring(0, 8)}";
                LogMqttConnectionAttempt(broker, port, gcid, newPassword, clientId, MqttProtocolVersion.V500);

                var builder = new MqttClientOptionsBuilder()
                    .WithTcpServer(broker, port)
                    .WithCredentials(gcid, newPassword)
                    .WithProtocolVersion(MqttProtocolVersion.V500)
                    .WithClientId(clientId)
                    .WithCleanSession(true)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                    .WithTimeout(TimeSpan.FromSeconds(10));

                builder.WithTlsOptions(o => o.UseTls());

                var newOptions = builder.Build();

                WriteLog("[Info] Reconnecting MQTT client with updated credentials...");
                try { await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None); } catch { }

                var result = await _mqttClient.ConnectAsync(newOptions, cancellationToken);
                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    WriteLog("[Info] MQTT client reconnected successfully with updated credentials.");
                    _currentOptions = newOptions;

                    // Re-subscribe to the same topic after reconnect
                    if (!string.IsNullOrEmpty(_currentTopic))
                    {
                        var resubOptions = _mqttFactory.CreateSubscribeOptionsBuilder()
                            .WithTopicFilter(f => f.WithTopic(_currentTopic).WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce))
                            .Build();
                        await _mqttClient.SubscribeAsync(resubOptions, cancellationToken);
                        WriteLog($"[Info] Re-subscribed to topic: {_currentTopic}");
                    }

                    return true;
                }
                WriteLog($"[Warning] MQTT reconnect failed. ResultCode={result.ResultCode}");
            }
            catch (Exception ex)
            {
                WriteLog($"[Warning] Exception while reconnecting MQTT: {ex.Message}");
            }

            return false;
        }
    }
}
