using System;
using System.Collections.Generic;
using UnityEditor;

namespace YUCP.Components.PackageGuardian.Editor.Integration.ImportMonitor
{
    /// <summary>
    /// Debounces rapid import events to avoid creating too many stashes.
    /// </summary>
    public sealed class ImportEventDebouncer
    {
        private readonly float _debounceSeconds;
        private double _lastEventTime;
        private readonly List<string> _pendingAssets = new List<string>();
        private Action _pendingCallback;
        
        public ImportEventDebouncer(float debounceSeconds = 0.5f)
        {
            _debounceSeconds = debounceSeconds;
        }
        
        /// <summary>
        /// Queue an event. If no more events arrive within the debounce window, execute the callback.
        /// </summary>
        public void QueueEvent(string[] assets, Action callback)
        {
            _lastEventTime = EditorApplication.timeSinceStartup;
            _pendingAssets.AddRange(assets);
            _pendingCallback = callback;
            
            // Schedule check
            EditorApplication.delayCall += CheckExecute;
        }
        
        private void CheckExecute()
        {
            double elapsed = EditorApplication.timeSinceStartup - _lastEventTime;
            
            if (elapsed >= _debounceSeconds)
            {
                // Debounce period has passed, execute
                if (_pendingCallback != null)
                {
                    var callback = _pendingCallback;
                    _pendingCallback = null;
                    _pendingAssets.Clear();
                    
                    callback?.Invoke();
                }
            }
            else
            {
                // Still within debounce period, check again
                EditorApplication.delayCall += CheckExecute;
            }
        }
    }
}

