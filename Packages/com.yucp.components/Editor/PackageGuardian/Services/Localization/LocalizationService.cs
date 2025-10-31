using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.PackageGuardian.Editor.Services.Localization
{
    /// <summary>
    /// Simple localization service for Package Guardian.
    /// </summary>
    public class LocalizationService : ILocalizationService
    {
        private static LocalizationService _instance;
        private Dictionary<string, string> _strings;
        private string _currentLanguage;
        
        public string CurrentLanguage => _currentLanguage;
        
        public static LocalizationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LocalizationService();
                }
                return _instance;
            }
        }
        
        private LocalizationService()
        {
            _currentLanguage = "en";
            LoadLanguage(_currentLanguage);
        }
        
        public void SetLanguage(string language)
        {
            _currentLanguage = language;
            LoadLanguage(language);
        }
        
        private void LoadLanguage(string language)
        {
            _strings = new Dictionary<string, string>();
            
            // Try to load from Resources
            var textAsset = UnityEngine.Resources.Load<UnityEngine.TextAsset>($"Localization/{language}");
            if (textAsset != null)
            {
                try
                {
                    var json = textAsset.text;
                    var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        _strings = dict;
                    }
                }
                catch
                {
                    Debug.LogWarning($"[Package Guardian] Failed to load localization for language: {language}");
                }
            }
            
            // Fallback to English if no strings loaded
            if (_strings.Count == 0)
            {
                LoadDefaultStrings();
            }
        }
        
        private void LoadDefaultStrings()
        {
            _strings = new Dictionary<string, string>
            {
                { "dashboard.title", "Package Guardian" },
                { "dashboard.status", "Repository Status" },
                { "dashboard.current_branch", "Current Branch" },
                { "dashboard.last_snapshot", "Last Snapshot" },
                { "dashboard.pending_changes", "Pending Changes" },
                { "dashboard.quick_actions", "Quick Actions" },
                { "dashboard.recent_activity", "Recent Activity" },
                { "actions.snapshot_now", "Snapshot Now" },
                { "actions.open_graph", "Open Graph" },
                { "actions.view_stashes", "View Stashes" },
                { "actions.settings", "Settings" },
                { "actions.rollback", "Rollback" },
                { "graph.title", "Commit Graph" },
                { "graph.no_commits", "No commits yet. Create your first snapshot!" },
                { "stash.title", "Stash Manager" },
                { "stash.no_stashes", "No stashes yet" },
                { "stash.apply", "Apply" },
                { "stash.drop", "Drop" },
                { "rollback.title", "Rollback Wizard" },
                { "rollback.select_commit", "Select a commit to restore:" },
                { "rollback.confirm", "This will restore your project. Continue?" },
                { "settings.title", "Package Guardian Settings" },
                { "settings.auto_snapshot_save", "Auto Snapshot on Save" },
                { "settings.auto_snapshot_upm", "Auto Snapshot on UPM Events" },
                { "settings.author_name", "Author Name" },
                { "settings.author_email", "Author Email" }
            };
        }
        
        public string GetString(string key)
        {
            if (_strings.TryGetValue(key, out string value))
            {
                return value;
            }
            return key; // Fallback to key if not found
        }
    }
}

