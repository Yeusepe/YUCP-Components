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
        private readonly List<string> _pendingAssets = new List<string>();
        private Action _pendingCallback;
        private double _executeAtTime = -1f;
        private bool _updateSubscribed;
        
        public ImportEventDebouncer(float debounceSeconds = 0.5f)
        {
            _debounceSeconds = debounceSeconds;
        }
        
        /// <summary>
        /// Queue an event. If no more events arrive within the debounce window, execute the callback.
        /// </summary>
        public void QueueEvent(string[] assets, Action callback)
        {
            var now = EditorApplication.timeSinceStartup;
            _executeAtTime = now + _debounceSeconds; // coalesce: push deadline forward on each event

            if (assets != null && assets.Length > 0)
                _pendingAssets.AddRange(assets);

            // Always keep the latest callback (caller already filters significance)
            _pendingCallback = callback;

            if (!_updateSubscribed)
            {
                _updateSubscribed = true;
                EditorApplication.update += OnEditorUpdate;
            }
        }

        private void OnEditorUpdate()
        {
            // Avoid executing during heavy editor states; lightly reschedule
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _executeAtTime = EditorApplication.timeSinceStartup + 0.2f;
                return;
            }

            if (_executeAtTime < 0)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now < _executeAtTime)
                return;

            // Time reached: unsubscribe first to minimize time on main thread
            EditorApplication.update -= OnEditorUpdate;
            _updateSubscribed = false;

            var callback = _pendingCallback;
            _pendingCallback = null;
            _executeAtTime = -1f;
            _pendingAssets.Clear();

            try
            {
                callback?.Invoke();
            }
            catch (Exception)
            {
                // Swallow to avoid breaking editor loop; callers handle logging
            }
        }
    }
}

