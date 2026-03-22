using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager
{
    /// <summary>
    /// Generates a stable machine fingerprint for license binding.
    /// The fingerprint is a SHA-256 hash of CPU and device identifiers.
    /// It never leaves the machine in raw form — only the hash is transmitted.
    /// </summary>
    internal static class MachineFingerprintService
    {
        private const string CacheKey = "yucp.machine_fingerprint";
        private static string _cached;

        /// <summary>
        /// Returns a stable hex fingerprint for this machine.
        /// Consistent across Unity Editor restarts.
        /// </summary>
        public static string GetFingerprint()
        {
            if (_cached != null) return _cached;

            // Use SessionState to survive domain reloads within the same Editor session
            string cached = UnityEditor.SessionState.GetString(CacheKey, null);
            if (!string.IsNullOrEmpty(cached))
            {
                _cached = cached;
                return _cached;
            }

            _cached = Compute();
            UnityEditor.SessionState.SetString(CacheKey, _cached);
            return _cached;
        }

        private static string Compute()
        {
            // Combine: processorType + deviceUniqueIdentifier
            // deviceUniqueIdentifier is stable per-machine (OS-provided, e.g. MachineGuid on Windows)
            string raw = SystemInfo.processorType + "|" + SystemInfo.deviceUniqueIdentifier;

            byte[] bytes = Encoding.UTF8.GetBytes(raw);
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(bytes);

            var sb = new StringBuilder(64);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
