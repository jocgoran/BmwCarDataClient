using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace BmwCarDataClient
{
    public class ProtectedTokenStore
    {
        private readonly string _filePath;
        private readonly IDataProtector? _protector;

        public ProtectedTokenStore(string filePath)
        {
            _filePath = filePath;
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                var provider = DataProtectionProvider.Create("BmwCarDataClient");
                _protector = provider.CreateProtector("ProtectedTokenStore.v1");
            }
            catch
            {
                _protector = null;
            }
        }

        public void Save<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            try
            {
                if (_protector != null)
                {
                    var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(json));
                    File.WriteAllBytes(_filePath, protectedBytes);
                    return;
                }

                // If no data protection provider is available, fall back to plaintext (best-effort)
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Fallback to plaintext if protection fails (best-effort)
                try
                {
                    File.WriteAllText(_filePath, json);
                }
                catch
                {
                    // ignore
                }
            }
        }

        public T? Load<T>() where T : class
        {
            if (!File.Exists(_filePath)) return null;
            try
            {
                // Try DataProtection provider first if available
                if (_protector != null)
                {
                    try
                    {
                        var protectedBytes = File.ReadAllBytes(_filePath);
                        var unprotected = _protector.Unprotect(protectedBytes);
                        var json = Encoding.UTF8.GetString(unprotected);
                        return JsonSerializer.Deserialize<T>(json);
                    }
                    catch
                    {
                        // fallthrough to try DPAPI/plaintext
                    }
                }

                // If no data protector succeeded, read plaintext
                var plain = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<T>(plain);
            }
            catch
            {
                return null;
            }
        }
    }
}
