using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Net.Http;
using System.IO;

namespace BmwCarDataClient
{
    public class BmwTokenResponse
    {
        public string? access_token { get; set; }
        public string? refresh_token { get; set; }
        public string? id_token { get; set; }
        public string? token_type { get; set; }
        public int expires_in { get; set; }
        public string? gcid { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsExpired => DateTime.UtcNow >= CreatedAt.AddSeconds(expires_in - 60);
    }

    /// <summary>Raised when the Device Code flow obtains a user-code and verification URI.</summary>
    public class DeviceCodeEventArgs : EventArgs
    {
        public string UserCode { get; init; } = string.Empty;
        public string VerificationUri { get; init; } = string.Empty;
        public int ExpiresInSeconds { get; init; }
    }

    /// <summary>Result from the device code endpoint (codes + PKCE verifier for later token request).</summary>
    public class DeviceCodeResponse
    {
        public string DeviceCode { get; init; } = string.Empty;
        public string UserCode { get; init; } = string.Empty;
        public string VerificationUri { get; init; } = string.Empty;
        public int ExpiresInSeconds { get; init; }
        public string CodeVerifier { get; init; } = string.Empty;   // keep in-memory only
    }

    public class BmwOAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly string _cacheFilePath;
        private readonly ProtectedTokenStore _protectedStore;
        private CancellationTokenSource? _refreshCts;

        // Event raised when a token is refreshed by the background auto-refresh loop.
        public event Action<BmwTokenResponse>? TokenRefreshed;

        // Event raised when the refresh token is permanently invalid (user must re-authenticate).
        public event Action<string>? RefreshFailed;

        // Event raised when Device Code flow is ready for user to authenticate.
        public event EventHandler<DeviceCodeEventArgs>? DeviceCodeReady;

        // Optional progress reporter for GUI log integration.
        public IProgress<string>? Log { get; set; }
        private readonly string _appLogFilePath;

        public BmwOAuthService()
        {
            _httpClient = new HttpClient();
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            _appLogFilePath = Path.Combine(logDir, "bmw_application.log");

            _cacheFilePath = Path.Combine(logDir, "bmw_tokens.json");
            _protectedStore = new ProtectedTokenStore(_cacheFilePath);
        }

        private void WriteLog(string message)
        {
            Console.WriteLine(message);
            Log?.Report(message);
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(_appLogFilePath, $"[{timestamp}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private string MaskSensitiveData(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    using var stream = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                    {
                        writer.WriteStartObject();
                        foreach (var prop in root.EnumerateObject())
                        {
                            if (prop.Name == "id_token" || prop.Name == "access_token" || prop.Name == "refresh_token")
                            {
                                writer.WriteString(prop.Name, "[MASKED]");
                            }
                            else
                            {
                                prop.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                    }
                    return System.Text.Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            catch { }
            return json;
        }

        private void LogCurlRequest(string url, Dictionary<string, string> formParams)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("========================================================================");
            sb.AppendLine($"[REQUEST] POST {url}");
            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine("curl -X 'POST' \\");
            sb.AppendLine($"  '{url}' \\");
            sb.AppendLine("  -H 'Accept: application/json' \\");
            sb.AppendLine("  -H 'Content-Type: application/x-www-form-urlencoded' \\");

            var paramPairs = new List<string>();
            foreach (var kvp in formParams)
            {
                var displayVal = (kvp.Key == "refresh_token") ? "[MASKED]" : kvp.Value;
                paramPairs.Add($"{kvp.Key}={displayVal}");
            }
            sb.AppendLine($"  -d '{string.Join("&", paramPairs)}'");
            sb.AppendLine("========================================================================");

            WriteLog(sb.ToString());
        }

        private void LogDetailedResponse(string url, HttpResponseMessage response, string responseBody)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("========================================================================");
            sb.AppendLine($"[RESPONSE] POST {url}");
            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine($"Status Code: {(int)response.StatusCode} {response.StatusCode}");
            sb.AppendLine("------------------------------------------------------------------------");

            sb.AppendLine("Response Body Nodes:");
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string displayValue;
                    if (prop.Name == "id_token" || prop.Name == "access_token" || prop.Name == "refresh_token")
                    {
                        displayValue = "[MASKED]";
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var strVal = prop.Value.GetString() ?? "";
                        displayValue = strVal.Length > 120 ? $"\"{strVal.Substring(0, 120)}...\"" : $"\"{strVal}\"";
                    }
                    else
                    {
                        displayValue = prop.Value.ToString();
                    }
                    sb.AppendLine($"  {prop.Name}: {displayValue}");
                }
            }
            catch
            {
                sb.AppendLine($"  (raw): {MaskSensitiveData(responseBody)}");
            }

            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine("Response Headers:");
            foreach (var header in response.Headers)
            {
                sb.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }
            if (response.Content?.Headers != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    sb.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
                }
            }
            sb.AppendLine("========================================================================");

            WriteLog(sb.ToString());
        }

        public BmwTokenResponse? LoadCachedToken()
        {
            var token = _protectedStore.Load<BmwTokenResponse>();
            if (token == null)
            {
                WriteLog("[Info] No cached token found (encrypted store).");
                return null;
            }

            WriteLog($"[Info] Loaded cached token from protected store: {_cacheFilePath}");
            var at = string.IsNullOrEmpty(token.access_token) ? "(none)" : token.access_token.Substring(0, Math.Min(8, token.access_token.Length)) + "...";
            var rt = string.IsNullOrEmpty(token.refresh_token) ? "(none)" : token.refresh_token.Substring(0, Math.Min(8, token.refresh_token.Length)) + "...";
            var idt = string.IsNullOrEmpty(token.id_token) ? "(none)" : token.id_token.Substring(0, Math.Min(8, token.id_token.Length)) + "...";
            WriteLog($"[Debug] Token summary -> access_token={at} refresh_token={rt} id_token={idt} gcid={token.gcid} expires_in={token.expires_in}");
            return token;
        }

        public void SaveToken(BmwTokenResponse token)
        {
            try
            {
                _protectedStore.Save(token);
                WriteLog($"[Info] Tokens saved successfully to protected cache at: {_cacheFilePath}");

                var at = string.IsNullOrEmpty(token.access_token) ? "(none)" : token.access_token.Substring(0, Math.Min(8, token.access_token.Length)) + "...";
                var rt = string.IsNullOrEmpty(token.refresh_token) ? "(none)" : token.refresh_token.Substring(0, Math.Min(8, token.refresh_token.Length)) + "...";
                var idt = string.IsNullOrEmpty(token.id_token) ? "(none)" : token.id_token.Substring(0, Math.Min(8, token.id_token.Length)) + "...";
                WriteLog($"[Debug] Token summary -> access_token={at} refresh_token={rt} id_token={idt} gcid={token.gcid} expires_in={token.expires_in}");
            }
            catch (Exception ex)
            {
                WriteLog($"[Error] Failed to cache tokens: {ex.Message}");
            }
        }

        public async Task<BmwTokenResponse?> GetOrRefreshTokenAsync(string clientId, string authEndpoint)
        {
            var token = LoadCachedToken();
            if (token != null)
            {
                if (!token.IsExpired)
                {
                    WriteLog("[Info] Using cached access token (still valid).");
                    // Start auto-refresh loop if not already running
                    StartAutoRefreshLoop(token, clientId, authEndpoint);
                    return token;
                }

                if (!string.IsNullOrEmpty(token.refresh_token))
                {
                    WriteLog("[Info] Access token expired. Attempting refresh token flow...");
                    var refreshedToken = await RefreshTokenAsync(clientId, authEndpoint, token.refresh_token);
                    if (refreshedToken != null)
                    {
                        SaveToken(refreshedToken);
                        StartAutoRefreshLoop(refreshedToken, clientId, authEndpoint);
                        return refreshedToken;
                    }
                    WriteLog("[Warning] Refresh token flow failed. Falling back to Device Authorization flow.");
                }
            }

            WriteLog("[Info] Starting OAuth 2.0 Device Authorization Grant flow...");
            var newToken = await StartDeviceFlowAsync(clientId, authEndpoint);
            if (newToken != null)
            {
                SaveToken(newToken);
                StartAutoRefreshLoop(newToken, clientId, authEndpoint);
                return newToken;
            }

            return null;
        }

        private async Task<BmwTokenResponse?> RefreshTokenAsync(string clientId, string authEndpoint, string refreshToken)
        {
            var url = $"{authEndpoint.TrimEnd('/')}/gcdm/oauth/token";
            var requestData = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            };

            try
            {
                LogCurlRequest(url, requestData);
                var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(requestData));
                var responseString = await response.Content.ReadAsStringAsync();
                LogDetailedResponse(url, response, responseString);

                if (response.IsSuccessStatusCode)
                {
                    var token = JsonSerializer.Deserialize<BmwTokenResponse>(responseString);
                    if (token != null)
                    {
                        token.CreatedAt = DateTime.UtcNow;
                        ExtractAndInjectGcid(token);
                        return token;
                    }
                }
                else
                {
                    WriteLog($"[Error] Token refresh failed. Status: {response.StatusCode}. Details: {responseString}");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[Error] Exception during token refresh: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Calls /gcdm/oauth/device/code and returns codes + PKCE verifier.
        /// Does NOT start any polling or token exchange.
        /// </summary>
        public async Task<DeviceCodeResponse?> RequestDeviceCodeAsync(string clientId, string authEndpoint)
        {
            var deviceCodeUrl = $"{authEndpoint.TrimEnd('/')}/gcdm/oauth/device/code";

            // Generate PKCE values (keep code_verifier in memory only)
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = ComputeS256CodeChallenge(codeVerifier);

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var requestData = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["response_type"] = "device_code",
                ["scope"] = "authenticate_user openid cardata:api:read cardata:streaming:read",
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            };

            try
            {
                LogCurlRequest(deviceCodeUrl, requestData);
                var response = await _httpClient.PostAsync(deviceCodeUrl, new FormUrlEncodedContent(requestData));
                var responseString = await response.Content.ReadAsStringAsync();
                LogDetailedResponse(deviceCodeUrl, response, responseString);

                if (!response.IsSuccessStatusCode)
                {
                    WriteLog($"[Error] Failed to request device code. Status: {response.StatusCode}. Details: {responseString}");
                    return null;
                }

                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;

                var deviceCode = root.GetProperty("device_code").GetString();
                var userCode = root.GetProperty("user_code").GetString();
                var verificationUri = root.GetProperty("verification_uri").GetString();
                var expiresIn = root.GetProperty("expires_in").GetInt32();

                if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(userCode) || string.IsNullOrEmpty(verificationUri))
                {
                    WriteLog("[Error] Incomplete device authorization response received.");
                    return null;
                }

                WriteLog($"[Info] Device code received. User code: {userCode}, Verification URI: {verificationUri}");

                // Raise DeviceCodeReady event for GUI consumers
                DeviceCodeReady?.Invoke(this, new DeviceCodeEventArgs
                {
                    UserCode = userCode,
                    VerificationUri = verificationUri,
                    ExpiresInSeconds = expiresIn
                });

                return new DeviceCodeResponse
                {
                    DeviceCode = deviceCode,
                    UserCode = userCode,
                    VerificationUri = verificationUri,
                    ExpiresInSeconds = expiresIn,
                    CodeVerifier = codeVerifier
                };
            }
            catch (Exception ex)
            {
                WriteLog($"[Error] Exception during device code request: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Single-shot token exchange using a previously obtained device_code.
        /// Returns null on any non-success response.
        /// </summary>
        public async Task<BmwTokenResponse?> RequestTokenWithDeviceCodeAsync(
            string clientId, string authEndpoint, string deviceCode, string codeVerifier)
        {
            var tokenUrl = $"{authEndpoint.TrimEnd('/')}/gcdm/oauth/token";
            var requestData = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["code_verifier"] = codeVerifier
            };

            try
            {
                LogCurlRequest(tokenUrl, requestData);
                var response = await _httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(requestData));
                var responseString = await response.Content.ReadAsStringAsync();
                LogDetailedResponse(tokenUrl, response, responseString);

                if (response.IsSuccessStatusCode)
                {
                    WriteLog("[Info] Token exchange successful!");
                    var token = JsonSerializer.Deserialize<BmwTokenResponse>(responseString);
                    if (token != null)
                    {
                        token.CreatedAt = DateTime.UtcNow;
                        ExtractAndInjectGcid(token);
                        return token;
                    }
                }
                else
                {
                    // Parse error response for informative message
                    try
                    {
                        using var errDoc = JsonDocument.Parse(responseString);
                        var errRoot = errDoc.RootElement;
                        if (errRoot.TryGetProperty("error", out var errProp))
                        {
                            var error = errProp.GetString();
                            WriteLog($"[Warning] Token request denied: {error}");
                        }
                        else
                        {
                            WriteLog($"[Error] Token request failed. Status: {response.StatusCode}");
                        }
                    }
                    catch
                    {
                        WriteLog($"[Error] Token request failed. Status: {response.StatusCode}. Body: {responseString}");
                    }
                    
                    throw new HttpRequestException($"Token request failed. Status: {response.StatusCode}", null, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[Error] Exception during token exchange: {ex.Message}");
                if (ex is HttpRequestException) throw;
            }

            return null;
        }

        public async Task<BmwTokenResponse?> StartDeviceFlowAsync(string clientId, string authEndpoint)
        {
            var deviceCodeUrl = $"{authEndpoint.TrimEnd('/')}/gcdm/oauth/device/code";

            // Generate PKCE values (keep code_verifier in memory only)
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = ComputeS256CodeChallenge(codeVerifier);

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var requestData = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["response_type"] = "device_code",
                ["scope"] = "authenticate_user openid cardata:api:read cardata:streaming:read",
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            };

            try
            {
                LogCurlRequest(deviceCodeUrl, requestData);
                var response = await _httpClient.PostAsync(deviceCodeUrl, new FormUrlEncodedContent(requestData));
                var responseString = await response.Content.ReadAsStringAsync();
                LogDetailedResponse(deviceCodeUrl, response, responseString);

                if (!response.IsSuccessStatusCode)
                {
                    WriteLog($"[Error] Failed to request device code. Status: {response.StatusCode}. Details: {responseString}");
                    return null;
                }

                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;

                var deviceCode = root.GetProperty("device_code").GetString();
                var userCode = root.GetProperty("user_code").GetString();
                var verificationUri = root.GetProperty("verification_uri").GetString();
                var expiresIn = root.GetProperty("expires_in").GetInt32();
                var interval = root.TryGetProperty("interval", out var intProp) ? intProp.GetInt32() : 5;

                if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(userCode) || string.IsNullOrEmpty(verificationUri))
                {
                    WriteLog("[Error] Incomplete device authorization response received.");
                    return null;
                }

                Console.WriteLine("\n======================================================================");
                Console.WriteLine("ACTION REQUIRED: AUTHORIZE APPLICATION");
                Console.WriteLine("======================================================================");
                Console.WriteLine($"1. Open your browser and go to:  {verificationUri}");
                Console.WriteLine($"2. Enter the following User Code: {userCode}");
                Console.WriteLine($"3. Log in with your BMW ConnectedDrive account and approve the request.");
                Console.WriteLine($"Note: This code expires in {expiresIn / 60} minutes.");
                Console.WriteLine("======================================================================\n");

                // Raise DeviceCodeReady event for GUI consumers
                DeviceCodeReady?.Invoke(this, new DeviceCodeEventArgs
                {
                    UserCode = userCode,
                    VerificationUri = verificationUri,
                    ExpiresInSeconds = expiresIn
                });

                // Try opening the verification URI in the default browser and include user_code if possible
                try
                {
                    var uriWithCode = verificationUri;
                    try
                    {
                        // Append user_code as query param if the endpoint supports it (best-effort)
                        uriWithCode = verificationUri.Contains("?") ? $"{verificationUri}&user_code={Uri.EscapeDataString(userCode)}" : $"{verificationUri}?user_code={Uri.EscapeDataString(userCode)}";
                    }
                    catch { }

                    WriteLog($"[Info] Attempting to open verification URI in default browser: {uriWithCode}");
                    var psi = new ProcessStartInfo { FileName = uriWithCode, UseShellExecute = true };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    WriteLog($"[Warning] Failed to open browser automatically: {ex.Message}");
                    WriteLog($"[Info] Please open the URL manually: {verificationUri}");
                }

                WriteLog("[Info] Waiting for user authorization (polling token endpoint)...");

                var tokenUrl = $"{authEndpoint.TrimEnd('/')}/gcdm/oauth/token";
                var pollData = new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["code_verifier"] = codeVerifier
                };
                
                var random = new Random();
                var expiryTime = DateTime.UtcNow.AddSeconds(expiresIn);
                while (DateTime.UtcNow < expiryTime)
                {
                    var humanDelay = (interval * 1000) + random.Next(500, 1500);
                    await Task.Delay(humanDelay);

                    LogCurlRequest(tokenUrl, pollData);
                    var pollResponse = await _httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(pollData));
                    var pollResponseString = await pollResponse.Content.ReadAsStringAsync();
                    LogDetailedResponse(tokenUrl, pollResponse, pollResponseString);

                    if (pollResponse.IsSuccessStatusCode)
                    {
                        WriteLog("[Info] Authorization successful!");
                        var token = JsonSerializer.Deserialize<BmwTokenResponse>(pollResponseString);
                        if (token != null)
                        {
                            token.CreatedAt = DateTime.UtcNow;
                            ExtractAndInjectGcid(token);
                            return token;
                        }
                    }
                    else
                    {
                        using var errDoc = JsonDocument.Parse(pollResponseString);
                        var errRoot = errDoc.RootElement;
                        if (errRoot.TryGetProperty("error", out var errProp))
                        {
                            var error = errProp.GetString();
                            if (error == "authorization_pending")
                            {
                                Console.Write("."); // Still waiting (CLI)
                                Log?.Report("[Info] Waiting for browser authorization...");
                                continue;
                            }
                            else if (error == "slow_down")
                            {
                                interval += 2; // Increase polling interval
                                WriteLog($"[Info] Rate-limited. Increasing polling interval to {interval} seconds.");
                                continue;
                            }
                            else
                            {
                                WriteLog($"[Error] Authorization failed: {error}");
                                break;
                            }
                        }
                        else
                        {
                            WriteLog($"[Error] Token request failed. Status: {pollResponse.StatusCode}");
                            break;
                        }
                    }
                }

                WriteLog("[Error] Device code expired or authorization flow cancelled.");
            }
            catch (Exception ex)
            {
                WriteLog($"[Error] Exception during device flow: {ex.Message}");
            }

            return null;
        }

        private void ExtractAndInjectGcid(BmwTokenResponse token)
        {
            // First check if gcid is already in response
            if (!string.IsNullOrEmpty(token.gcid)) return;

            // Otherwise, decode ID Token JWT and search for gcid claim
            if (string.IsNullOrEmpty(token.id_token)) return;

            try
            {
                var payloadJson = DecodeJwtPayload(token.id_token);
                if (string.IsNullOrEmpty(payloadJson)) return;

                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                // Log the JWT payload in console for user awareness (useful for debug and mapping)
                WriteLog("[Debug] ID Token Claims:");
                foreach (var prop in root.EnumerateObject())
                {
                    WriteLog($"  {prop.Name}: {prop.Value}");
                }

                if (root.TryGetProperty("gcid", out var gcidProp))
                {
                    token.gcid = gcidProp.GetString();
                    WriteLog($"[Info] Extracted GCID from ID Token: {token.gcid}");
                }
                else if (root.TryGetProperty("sub", out var subProp))
                {
                    // Fallback to subject if gcid not explicitly named
                    token.gcid = subProp.GetString();
                    WriteLog($"[Info] Fallback: Extracted GCID (sub claim) from ID Token: {token.gcid}");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[Warning] Failed to extract GCID from ID Token: {ex.Message}");
            }
        }

        private static string DecodeJwtPayload(string token)
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return string.Empty;

            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var bytes = Convert.FromBase64String(payload);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static string GenerateCodeVerifier()
        {
            // Generate 64 bytes and base64url-encode without padding
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[64];
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string ComputeS256CodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            var bytes = System.Text.Encoding.ASCII.GetBytes(codeVerifier);
            var hash = sha256.ComputeHash(bytes);
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] input)
        {
            var s = Convert.ToBase64String(input) // Regular base64
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return s;
        }

        private void StartAutoRefreshLoop(BmwTokenResponse initialToken, string clientId, string authEndpoint)
        {
            // Cancel previous loop if any
            try
            {
                _refreshCts?.Cancel();
            }
            catch { }

            _refreshCts = new CancellationTokenSource();
            var ct = _refreshCts.Token;

            Task.Run(async () =>
            {
                var token = initialToken;
                int consecutiveFailures = 0;
                const int maxConsecutiveFailures = 3;
                WriteLog($"[Info] Auto-refresh loop started. Initial token expires_in: {token.expires_in}s, CreatedAt: {token.CreatedAt:O}");

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Calculate delay until refresh time (refresh 600s / 10 minutes before expiry)
                        var refreshAt = token.CreatedAt.AddSeconds(Math.Max(60, token.expires_in - 600));
                        var delay = refreshAt - DateTime.UtcNow;
                        if (delay <= TimeSpan.Zero)
                        {
                            delay = TimeSpan.FromSeconds(5);
                            WriteLog("[Debug] Refresh time already passed, using minimum 5-second delay");
                        }

                        WriteLog($"[Debug] Auto-refresh scheduled in {delay.TotalMinutes:F1} minutes (at {refreshAt:O})");
                        await Task.Delay(delay, ct);

                        if (ct.IsCancellationRequested)
                        {
                            WriteLog("[Info] Auto-refresh loop cancelled");
                            break;
                        }

                        if (string.IsNullOrEmpty(token.refresh_token))
                        {
                            WriteLog("[Warning] No refresh_token available; session expired.");
                            RefreshFailed?.Invoke("Nessun refresh_token disponibile. Sessione scaduta.");
                            break;
                        }

                        WriteLog("[Info] Attempting background token refresh...");
                        var refreshed = await RefreshTokenAsync(clientId, authEndpoint, token.refresh_token);
                        if (refreshed != null)
                        {
                            refreshed.CreatedAt = DateTime.UtcNow;
                            SaveToken(refreshed);
                            token = refreshed;
                            consecutiveFailures = 0; // reset on success
                            WriteLog($"[Info] Token refreshed successfully. New expires_in: {refreshed.expires_in}s");
                            try
                            {
                                TokenRefreshed?.Invoke(refreshed);
                            }
                            catch (Exception ex)
                            {
                                WriteLog($"[Warning] Exception while invoking TokenRefreshed handlers: {ex.Message}");
                            }
                        }
                        else
                        {
                            consecutiveFailures++;
                            WriteLog($"[Warning] Background token refresh failed (attempt {consecutiveFailures}/{maxConsecutiveFailures}).");

                            if (consecutiveFailures >= maxConsecutiveFailures)
                            {
                                WriteLog("[Error] Max consecutive refresh failures reached. Session expired — user must re-authenticate.");
                                RefreshFailed?.Invoke("Sessione scaduta dopo 3 tentativi falliti. Riavviare l'autenticazione.");
                                break;
                            }

                            await Task.Delay(TimeSpan.FromSeconds(30), ct);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        WriteLog("[Debug] Auto-refresh loop task cancelled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"[Warning] Exception in auto-refresh loop: {ex.Message}");
                        consecutiveFailures++;
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            WriteLog("[Error] Max consecutive refresh failures reached. Session expired.");
                            RefreshFailed?.Invoke($"Errore critico nel refresh: {ex.Message}");
                            break;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    }
                }
                WriteLog("[Info] Auto-refresh loop exited.");
            }, ct);
        }
    }
}
