using System;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    /// <summary>
    /// Circuit breaker pattern to prevent infinite failure loops
    /// </summary>
    public static class CircuitBreakerService
    {
        private const int MAX_CONSECUTIVE_FAILURES = 3;
        private const string PREF_KEY_FAILURES = "PackageGuardian_ConsecutiveFailures";
        private const string PREF_KEY_LAST_FAILURE = "PackageGuardian_LastFailureTime";
        private const int RESET_AFTER_HOURS = 24;
        
        private static int _consecutiveFailures = -1;
        
        /// <summary>
        /// Gets the current number of consecutive failures
        /// </summary>
        public static int GetConsecutiveFailures()
        {
            if (_consecutiveFailures == -1)
            {
                _consecutiveFailures = EditorPrefs.GetInt(PREF_KEY_FAILURES, 0);
                
                // Auto-reset if last failure was more than 24 hours ago
                string lastFailureStr = EditorPrefs.GetString(PREF_KEY_LAST_FAILURE, "");
                if (!string.IsNullOrEmpty(lastFailureStr))
                {
                    if (DateTime.TryParse(lastFailureStr, out DateTime lastFailure))
                    {
                        if ((DateTime.UtcNow - lastFailure).TotalHours > RESET_AFTER_HOURS)
                        {
                            Debug.Log($"[Package Guardian] Auto-resetting circuit breaker (last failure was {(DateTime.UtcNow - lastFailure).TotalHours:F1} hours ago)");
                            ResetCircuitBreaker();
                        }
                    }
                }
            }
            
            return _consecutiveFailures;
        }
        
        /// <summary>
        /// Checks if the circuit breaker is active (too many failures)
        /// </summary>
        public static bool IsCircuitBroken()
        {
            return GetConsecutiveFailures() >= MAX_CONSECUTIVE_FAILURES;
        }
        
        /// <summary>
        /// Records a failure
        /// </summary>
        public static void RecordFailure(string operationName, Exception ex = null)
        {
            _consecutiveFailures = GetConsecutiveFailures() + 1;
            EditorPrefs.SetInt(PREF_KEY_FAILURES, _consecutiveFailures);
            EditorPrefs.SetString(PREF_KEY_LAST_FAILURE, DateTime.UtcNow.ToString("O"));
            
            string message = $"[Package Guardian] Operation '{operationName}' failed ({_consecutiveFailures}/{MAX_CONSECUTIVE_FAILURES})";
            if (ex != null)
            {
                message += $": {ex.Message}";
            }
            
            Debug.LogError(message);
            
            if (IsCircuitBroken())
            {
                Debug.LogError($"[Package Guardian] CIRCUIT BREAKER ACTIVATED - Protection disabled after {MAX_CONSECUTIVE_FAILURES} consecutive failures!");
                Debug.LogError("[Package Guardian] Use Tools > Package Guardian > Reset Circuit Breaker to re-enable protection");
                
                EditorUtility.DisplayDialog(
                    "Package Guardian - Circuit Breaker Activated",
                    $"Package Guardian has been disabled after {MAX_CONSECUTIVE_FAILURES} consecutive failures.\n\n" +
                    "This prevents infinite failure loops. Check the Console for error details.\n\n" +
                    "Use 'Tools > Package Guardian > Reset Circuit Breaker' to re-enable protection once you've resolved the issues.",
                    "OK"
                );
            }
        }
        
        /// <summary>
        /// Records a success
        /// </summary>
        public static void RecordSuccess()
        {
            if (_consecutiveFailures > 0)
            {
                Debug.Log("[Package Guardian] Operation successful - resetting failure counter");
            }
            
            _consecutiveFailures = 0;
            EditorPrefs.SetInt(PREF_KEY_FAILURES, 0);
            EditorPrefs.DeleteKey(PREF_KEY_LAST_FAILURE);
        }
        
        /// <summary>
        /// Manually resets the circuit breaker
        /// </summary>
        [MenuItem("Tools/Package Guardian/Reset Circuit Breaker", priority = 200)]
        public static void ResetCircuitBreaker()
        {
            int previousFailures = GetConsecutiveFailures();
            
            _consecutiveFailures = 0;
            EditorPrefs.SetInt(PREF_KEY_FAILURES, 0);
            EditorPrefs.DeleteKey(PREF_KEY_LAST_FAILURE);
            
            if (previousFailures > 0)
            {
                Debug.Log($"[Package Guardian] Circuit breaker reset (was: {previousFailures} failures). Protection re-enabled.");
                EditorUtility.DisplayDialog(
                    "Package Guardian",
                    "Circuit breaker has been reset.\n\nProtection is now re-enabled.",
                    "OK"
                );
            }
            else
            {
                Debug.Log("[Package Guardian] Circuit breaker reset (no previous failures)");
            }
        }
        
        /// <summary>
        /// Gets the status message
        /// </summary>
        public static string GetStatusMessage()
        {
            int failures = GetConsecutiveFailures();
            
            if (failures == 0)
            {
                return "Circuit Breaker: OK (no failures)";
            }
            else if (failures < MAX_CONSECUTIVE_FAILURES)
            {
                return $"Circuit Breaker: Warning ({failures}/{MAX_CONSECUTIVE_FAILURES} failures)";
            }
            else
            {
                return $"Circuit Breaker: ACTIVE (protection disabled after {failures} failures)";
            }
        }
    }
}











