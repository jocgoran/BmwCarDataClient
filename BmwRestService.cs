using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BmwCarDataClient
{
    /// <summary>
    /// REST client for BMW CarData API with proper Bearer token handling and error management.
    /// </summary>
    public class BmwRestService
    {
        private readonly HttpClient _httpClient;
        private readonly string _outputDirectory;
        private readonly Action<string> _logger;

        public BmwRestService(HttpClient httpClient, string outputDirectory, Action<string> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Ensure output directory exists
            Directory.CreateDirectory(_outputDirectory);
        }

        /// <summary>
        /// Fetches vehicle data from multiple endpoints and saves to files.
        /// </summary>
        public async Task<bool> FetchAndSaveVehicleDataAsync(string vin, string accessToken, string apiEndpoint)
        {
            if (string.IsNullOrWhiteSpace(vin))
                throw new ArgumentException("VIN cannot be null or empty.", nameof(vin));
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ArgumentException("API endpoint cannot be null or empty.", nameof(apiEndpoint));

            _logger($"[Info] Starting REST API queries for VIN: {vin}");

            bool success = true;

            // Fetch vehicle info
            var vehicleUrl = $"{apiEndpoint.TrimEnd('/')}/customers/vehicles/{vin}";
            success &= await FetchEndpointAndSaveAsync(vehicleUrl, "vehicle_info.json", accessToken);

            // Fetch telematic data
            var telematicUrl = $"{apiEndpoint.TrimEnd('/')}/customers/vehicles/{vin}/telematicData";
            success &= await FetchEndpointAndSaveAsync(telematicUrl, "telematic_data.json", accessToken);

            // Fetch vehicles list
            var listUrl = $"{apiEndpoint.TrimEnd('/')}/customers/vehicles";
            success &= await FetchEndpointAndSaveAsync(listUrl, "vehicles_list.json", accessToken);

            return success;
        }

        /// <summary>
        /// Fetches a single endpoint and saves the response to a file.
        /// Uses HttpRequestMessage with Bearer token (not global headers).
        /// Validates response and properly disposes resources.
        /// </summary>
        private async Task<bool> FetchEndpointAndSaveAsync(string url, string fileName, string accessToken)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or empty.", nameof(url));
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be null or empty.", nameof(fileName));

            try
            {
                var destinationPath = Path.Combine(_outputDirectory, fileName);

                // Create request with Bearer token per-request (not global headers)
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Send request and ensure proper disposal of response
                using var response = await _httpClient.SendAsync(request);

                // Check for BMW API errors before reading content
                await HandleApiErrorAsync(response);

                var content = await response.Content.ReadAsStringAsync();

                _logger($"[REST API] GET {url} Status: {(int)response.StatusCode} {response.StatusCode}");

                // Validate JSON content before saving (optional but recommended)
                ValidateJsonContent(content, fileName);

                // Save to file with UTF-8 encoding
                await File.WriteAllTextAsync(destinationPath, content, Encoding.UTF8);
                _logger($"[REST API] ✓ Successfully saved to {destinationPath}");

                return true;
            }
            catch (HttpRequestException ex)
            {
                _logger($"[ERROR] HTTP request failed for {url}: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger($"[ERROR] Request timeout for {url}: {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                _logger($"[ERROR] File save failed for {fileName}: {ex.Message}");
                return false;
            }
            catch (JsonException ex)
            {
                _logger($"[ERROR] Invalid JSON response from {url}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger($"[ERROR] Unexpected error while fetching {url}: {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates that the response content is valid JSON.
        /// </summary>
        private void ValidateJsonContent(string content, string fileName)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new JsonException($"Response for {fileName} is empty.");
            }

            try
            {
                using var jsonDoc = JsonDocument.Parse(content);
                if (jsonDoc.RootElement.ValueKind == JsonValueKind.Undefined)
                {
                    throw new JsonException($"Invalid JSON structure in {fileName}.");
                }
            }
            catch (JsonException ex)
            {
                throw new JsonException($"Failed to parse JSON from {fileName}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generic method to fetch a single endpoint with Bearer token.
        /// Can be used for custom API calls.
        /// </summary>
        public async Task<string> GetAsync(string url, string accessToken)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or empty.", nameof(url));
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _httpClient.SendAsync(request);
                await HandleApiErrorAsync(response);

                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException ex)
            {
                _logger($"[ERROR] GET {url} failed: {ex.Message}");
                throw;
            }
        }

        private void LogGetRequest(string url, string accessToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("========================================================================");
            sb.AppendLine($"[REQUEST] GET {url}");
            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine("curl -X 'GET' \\");
            sb.AppendLine($"  '{url}' \\");
            sb.AppendLine("  -H 'Accept: application/json' \\");
            var maskedToken = string.IsNullOrEmpty(accessToken) ? "" : (accessToken.Length > 20 ? accessToken.Substring(0, 15) + "..." : "[MASKED]");
            sb.AppendLine($"  -H 'Authorization: Bearer {maskedToken}' \\");
            sb.AppendLine("  -H 'x-version: v1'");
            sb.AppendLine("========================================================================");
            _logger(sb.ToString());
        }

        private void LogPostRequest(string url, string accessToken, string jsonPayload)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("========================================================================");
            sb.AppendLine($"[REQUEST] POST {url}");
            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine("curl -X 'POST' \\");
            sb.AppendLine($"  '{url}' \\");
            sb.AppendLine("  -H 'Accept: application/json' \\");
            var maskedToken = string.IsNullOrEmpty(accessToken) ? "" : (accessToken.Length > 20 ? accessToken.Substring(0, 15) + "..." : "[MASKED]");
            sb.AppendLine($"  -H 'Authorization: Bearer {maskedToken}' \\");
            sb.AppendLine("  -H 'x-version: v1' \\");
            sb.AppendLine("  -H 'Content-Type: application/json; charset=utf-8' \\");
            sb.AppendLine($"  -d '{jsonPayload}'");
            sb.AppendLine("========================================================================");
            _logger(sb.ToString());
        }

        private void LogRestResponse(string url, HttpMethod method, HttpResponseMessage response, string responseBody)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("========================================================================");
            sb.AppendLine($"[RESPONSE] {method} {url}");
            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine($"Status Code: {(int)response.StatusCode} {response.StatusCode}");
            sb.AppendLine("------------------------------------------------------------------------");
            sb.AppendLine("Response Body:");
            
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var formattedJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                sb.AppendLine(formattedJson);
            }
            catch
            {
                sb.AppendLine(responseBody);
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
            _logger(sb.ToString());
        }

        /// <summary>
        /// Fetches the list of containers.
        /// </summary>
        public async Task<string> GetContainersAsync(string accessToken, string apiEndpoint)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ArgumentException("API endpoint cannot be null or empty.", nameof(apiEndpoint));

            var url = $"{apiEndpoint.TrimEnd('/')}/customers/containers";
            
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("x-version", "v1");

                LogGetRequest(url, accessToken);

                using var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                LogRestResponse(url, HttpMethod.Get, response, content);
                
                await HandleApiErrorAsync(response, content);

                return content;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger($"[ERROR] GET {url} failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Fetches the Smart Maintenance Tyre Diagnosis data for a specific vehicle.
        /// </summary>
        public async Task<string> GetSmartMaintenanceTyreDiagnosisAsync(string vin, string accessToken, string apiEndpoint)
        {
            if (string.IsNullOrWhiteSpace(vin))
                throw new ArgumentException("VIN cannot be null or empty.", nameof(vin));
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ArgumentException("API endpoint cannot be null or empty.", nameof(apiEndpoint));

            var url = $"{apiEndpoint.TrimEnd('/')}/customers/vehicles/{vin}/smartMaintenanceTyreDiagnosis";
            
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("x-version", "v1");

                LogGetRequest(url, accessToken);

                using var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                LogRestResponse(url, HttpMethod.Get, response, content);
                
                await HandleApiErrorAsync(response, content);

                return content;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger($"[ERROR] GET {url} failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates a virtual container on the BMW API.
        /// </summary>
        public async Task<string> CreateContainerAsync(string accessToken, string apiEndpoint, string containerJson)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ArgumentException("API endpoint cannot be null or empty.", nameof(apiEndpoint));
            if (string.IsNullOrWhiteSpace(containerJson))
                throw new ArgumentException("Container JSON cannot be null or empty.", nameof(containerJson));

            var url = $"{apiEndpoint.TrimEnd('/')}/customers/containers";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("x-version", "v1");
                request.Content = new StringContent(containerJson, Encoding.UTF8, "application/json");

                LogPostRequest(url, accessToken, containerJson);

                using var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                LogRestResponse(url, HttpMethod.Post, response, content);
                
                await HandleApiErrorAsync(response, content);

                return content;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger($"[ERROR] POST {url} failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Deletes a specific container from the BMW API.
        /// </summary>
        public async Task<bool> DeleteContainerAsync(string containerName, string token, string endpoint)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{endpoint.TrimEnd('/')}/customers/containers/{containerName}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("x-version", "v1");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Fetches the basic (static) vehicle data for a specific VIN.
        /// Explicitly catches and formats HTTP errors (400, 403, 404, etc.) for UI display.
        /// </summary>
        public async Task<string> GetBasicVehicleDataAsync(string vin, string token, string endpoint)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.TrimEnd('/')}/customers/vehicles/{vin}/basicData");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("x-version", "v1");

            var response = await _httpClient.SendAsync(request);
            await HandleApiErrorAsync(response);
            
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<byte[]> GetVehicleImageBytesAsync(string vin, string token, string endpoint)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.TrimEnd('/')}/customers/vehicles/{vin}/image");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("x-version", "v1");
            
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("image/jpeg"));
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("image/png"));

            var response = await _httpClient.SendAsync(request);
            await HandleApiErrorAsync(response);

            return await response.Content.ReadAsByteArrayAsync();
        }

        public async Task<string> GetTelematicDataForContainerAsync(string vin, string containerId, string token, string endpoint)
        {
            // Corretto: Ora utilizza il parametro query containerId richiesto dai server BMW
            string requestUrl = $"{endpoint.TrimEnd('/')}/customers/vehicles/{vin}/telematicData?containerId={Uri.EscapeDataString(containerId)}";
            
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("x-version", "v1");

            var response = await _httpClient.SendAsync(request);
            await HandleApiErrorAsync(response);
            
            return await response.Content.ReadAsStringAsync();
        }


        /// <summary>
        /// Centralized handler for BMW API error responses.
        /// Parses the CustomerErrorResponse JSON structure (exveErrorId, exveErrorMsg, etc.)
        /// and throws an HttpRequestException with a human-readable message.
        /// </summary>
        private async Task HandleApiErrorAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            string rawContent = await response.Content.ReadAsStringAsync();
            
            // Usa esattamente i testi della documentazione BMW
            string statusCodeDesc = response.StatusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "400 Bad request. Please check API specification",
                System.Net.HttpStatusCode.Unauthorized => "401 Authentication Failed",
                System.Net.HttpStatusCode.Forbidden => "403 Access to resource is forbidden",
                System.Net.HttpStatusCode.NotFound => "404 Not found",
                System.Net.HttpStatusCode.InternalServerError => "500 A permanent server error occurred. Report this error if it occurs",
                System.Net.HttpStatusCode.ServiceUnavailable => "503 A temporary server error occurred. Retry again later or report this error if it persists.",
                _ => $"{(int)response.StatusCode} {response.ReasonPhrase}"
            };

            // Estrai i dettagli se presenti e lancia l'eccezione
            string errorDetails = rawContent;
            try
            {
                var apiError = JsonSerializer.Deserialize<BmwApiError>(rawContent);
                if (apiError != null && !string.IsNullOrEmpty(apiError.exveErrorMsg))
                {
                    errorDetails = $"Dettaglio: {apiError.exveErrorMsg} (ID: {apiError.exveErrorId})";
                }
            }
            catch { /* Mantieni il rawContent in caso di parsing fallito */ }

            throw new HttpRequestException($"{statusCodeDesc}\n{errorDetails}");
        }

        /// <summary>
        /// Overload for cases where response content has already been read.
        /// Avoids double-reading the response stream.
        /// </summary>
        private Task HandleApiErrorAsync(HttpResponseMessage response, string alreadyReadContent)
        {
            if (response.IsSuccessStatusCode) return Task.CompletedTask;

            // Usa esattamente i testi della documentazione BMW
            string statusCodeDesc = response.StatusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "400 Bad request. Please check API specification",
                System.Net.HttpStatusCode.Unauthorized => "401 Authentication Failed",
                System.Net.HttpStatusCode.Forbidden => "403 Access to resource is forbidden",
                System.Net.HttpStatusCode.NotFound => "404 Not found",
                System.Net.HttpStatusCode.InternalServerError => "500 A permanent server error occurred. Report this error if it occurs",
                System.Net.HttpStatusCode.ServiceUnavailable => "503 A temporary server error occurred. Retry again later or report this error if it persists.",
                _ => $"{(int)response.StatusCode} {response.ReasonPhrase}"
            };

            // Estrai i dettagli se presenti e lancia l'eccezione
            string errorDetails = alreadyReadContent;
            try
            {
                var apiError = JsonSerializer.Deserialize<BmwApiError>(alreadyReadContent);
                if (apiError != null && !string.IsNullOrEmpty(apiError.exveErrorMsg))
                {
                    errorDetails = $"Dettaglio: {apiError.exveErrorMsg} (ID: {apiError.exveErrorId})";
                }
            }
            catch { /* Mantieni il rawContent in caso di parsing fallito */ }

            throw new HttpRequestException($"{statusCodeDesc}\n{errorDetails}");
        }
    }

    /// <summary>
    /// Represents a parsed BMW CarData API error response (CustomerErrorResponse schema).
    /// </summary>
    public class BmwApiError
    {
        public string? exveErrorId { get; set; }
        public string? exveErrorRef { get; set; }
        public string? exveErrorMsg { get; set; }
        public string? exveNote { get; set; }
    }
}
