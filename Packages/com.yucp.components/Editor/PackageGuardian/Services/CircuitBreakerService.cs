using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    public static class CircuitBreakerService
    {
        private const int MAX_CONSECUTIVE_FAILURES = 3;
        private const string PREF_KEY_FAILURES = "PackageGuardian_ConsecutiveFailures";
        private const string PREF_KEY_LAST_FAILURE = "PackageGuardian_LastFailureTime";
        private const int RESET_AFTER_HOURS = 24;
        private const string LOCK_KEY = "pg_cb";
        private const string STATE_SALT = "pg.cb.v2";

        private static int _consecutiveFailures = -1;

        [Serializable]
        private sealed class State
        {
            public int f;
            public string t;
            public string h;
        }

        public static int GetConsecutiveFailures()
        {
            if (_consecutiveFailures != -1)
                return _consecutiveFailures;

            int fromPrefs = EditorPrefs.GetInt(PREF_KEY_FAILURES, 0);
            string prefLast = EditorPrefs.GetString(PREF_KEY_LAST_FAILURE, string.Empty);

            bool tamper;
            int fromState;
            string stateLast;
            bool hasState = TryReadState(out fromState, out stateLast, out tamper);

            if (tamper)
            {
                _consecutiveFailures = MAX_CONSECUTIVE_FAILURES;
                fromState = _consecutiveFailures;
                stateLast = DateTime.UtcNow.ToString("O");
                ProtectionLatchService.Set(LOCK_KEY, "state_tamper");
            }
            else
            {
                _consecutiveFailures = Math.Max(fromPrefs, hasState ? fromState : 0);
                stateLast = string.IsNullOrEmpty(stateLast) ? prefLast : stateLast;
            }

            if (_consecutiveFailures > 0 && DateTime.TryParse(stateLast, out var lastFailureUtc))
            {
                if ((DateTime.UtcNow - lastFailureUtc).TotalHours > RESET_AFTER_HOURS)
                {
                    _consecutiveFailures = 0;
                    stateLast = string.Empty;
                }
            }

            PersistState(_consecutiveFailures, stateLast);
            EditorPrefs.SetInt(PREF_KEY_FAILURES, _consecutiveFailures);
            if (string.IsNullOrEmpty(stateLast))
                EditorPrefs.DeleteKey(PREF_KEY_LAST_FAILURE);
            else
                EditorPrefs.SetString(PREF_KEY_LAST_FAILURE, stateLast);

            if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                ProtectionLatchService.Set(LOCK_KEY, "threshold_reached");
            else
                ProtectionLatchService.Clear(LOCK_KEY);

            return _consecutiveFailures;
        }

        public static bool IsCircuitBroken()
        {
            return GetConsecutiveFailures() >= MAX_CONSECUTIVE_FAILURES;
        }

        public static void RecordFailure(string operationName, Exception ex = null)
        {
            _consecutiveFailures = GetConsecutiveFailures() + 1;
            string now = DateTime.UtcNow.ToString("O");

            EditorPrefs.SetInt(PREF_KEY_FAILURES, _consecutiveFailures);
            EditorPrefs.SetString(PREF_KEY_LAST_FAILURE, now);
            PersistState(_consecutiveFailures, now);

            string message = $"[Package Guardian] Operation '{operationName}' failed ({_consecutiveFailures}/{MAX_CONSECUTIVE_FAILURES})";
            if (ex != null)
                message += $": {ex.Message}";
            Debug.LogError(message);

            if (IsCircuitBroken())
            {
                ProtectionLatchService.Set(LOCK_KEY, "threshold_reached");
                Debug.LogError($"[Package Guardian] CIRCUIT BREAKER ACTIVE - recovery mode engaged after {MAX_CONSECUTIVE_FAILURES} consecutive failures.");
                Debug.LogError("[Package Guardian] Use Tools > Package Guardian > Reset Circuit Breaker after resolving underlying errors.");
            }
        }

        public static void RecordSuccess()
        {
            if (_consecutiveFailures > 0)
                Debug.Log("[Package Guardian] Operation successful - resetting failure counter");

            _consecutiveFailures = 0;
            EditorPrefs.SetInt(PREF_KEY_FAILURES, 0);
            EditorPrefs.DeleteKey(PREF_KEY_LAST_FAILURE);
            PersistState(0, string.Empty);
            ProtectionLatchService.Clear(LOCK_KEY);
        }

        [MenuItem("Tools/Package Guardian/Reset Circuit Breaker", priority = 200)]
        public static void ResetCircuitBreaker()
        {
            int previousFailures = GetConsecutiveFailures();
            _consecutiveFailures = 0;

            EditorPrefs.SetInt(PREF_KEY_FAILURES, 0);
            EditorPrefs.DeleteKey(PREF_KEY_LAST_FAILURE);
            PersistState(0, string.Empty);
            ProtectionLatchService.Clear(LOCK_KEY);

            if (previousFailures > 0)
            {
                Debug.Log($"[Package Guardian] Circuit breaker reset (was: {previousFailures} failures).");
                EditorUtility.DisplayDialog("Package Guardian", "Circuit breaker has been reset.", "OK");
            }
            else
            {
                Debug.Log("[Package Guardian] Circuit breaker reset (no previous failures)");
            }
        }

        public static string GetStatusMessage()
        {
            int failures = GetConsecutiveFailures();
            if (failures == 0)
                return "Circuit Breaker: OK (no failures)";
            if (failures < MAX_CONSECUTIVE_FAILURES)
                return $"Circuit Breaker: Warning ({failures}/{MAX_CONSECUTIVE_FAILURES} failures)";
            return $"Circuit Breaker: ACTIVE (bounded recovery mode; {failures} failures)";
        }

        private static string GetStatePath()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "YUCP", "PackageGuardian"));
            return Path.Combine(root, "cbs.dat");
        }

        private static string ComputeHash(int failures, string lastFailureIso)
        {
            string proj = string.Empty;
            try { proj = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').ToLowerInvariant(); } catch { }
            string device = string.Empty;
            try { device = SystemInfo.deviceUniqueIdentifier ?? string.Empty; } catch { }

            string material = STATE_SALT + "|" + proj + "|" + device + "|" + failures + "|" + (lastFailureIso ?? string.Empty);
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static void PersistState(int failures, string lastFailureIso)
        {
            try
            {
                var state = new State
                {
                    f = Math.Max(0, failures),
                    t = lastFailureIso ?? string.Empty,
                    h = ComputeHash(Math.Max(0, failures), lastFailureIso ?? string.Empty)
                };

                string path = GetStatePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonUtility.ToJson(state, false), Encoding.UTF8);
            }
            catch { }
        }

        private static bool TryReadState(out int failures, out string lastFailureIso, out bool tamperDetected)
        {
            failures = 0;
            lastFailureIso = string.Empty;
            tamperDetected = false;

            try
            {
                string path = GetStatePath();
                if (!File.Exists(path))
                    return false;

                var raw = File.ReadAllText(path, Encoding.UTF8);
                var state = JsonUtility.FromJson<State>(raw);
                if (state == null)
                {
                    tamperDetected = true;
                    return false;
                }

                string expected = ComputeHash(Math.Max(0, state.f), state.t ?? string.Empty);
                if (!string.Equals(expected, state.h ?? string.Empty, StringComparison.Ordinal))
                {
                    tamperDetected = true;
                    return false;
                }

                failures = Math.Max(0, state.f);
                lastFailureIso = state.t ?? string.Empty;
                return true;
            }
            catch
            {
                tamperDetected = true;
                return false;
            }
        }
    }
}










