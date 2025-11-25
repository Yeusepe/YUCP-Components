using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Windows;

namespace YUCP.Components.PackageGuardian.Editor.Settings
{
    /// <summary>
    /// Unity Project Settings provider for Package Guardian
    /// </summary>
    public class PackageGuardianSettingsProvider : SettingsProvider
    {
        private PackageGuardianSettings _settings;
        private SerializedObject _serializedSettings;
        
        // UI Elements
        private Toggle _enabledToggle;
        private Toggle _autoSnapshotToggle;
        private Toggle _autoStashUPMToggle;
        private Toggle _autoStashAssetToggle;
        private Toggle _autoStashSceneToggle;
        private TextField _authorNameField;
        private TextField _authorEmailField;
        private Toggle _largeFileToggle;
        private TextField _largeFileThresholdField;
        private Toggle _fsyncToggle;
        private Toggle _showFirstImportToggle;
        private ListView _trackedDirsListView;
        private List<string> _trackedDirsList;

        public PackageGuardianSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes)
        {
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _settings = PackageGuardianSettings.Instance;
            _serializedSettings = new SerializedObject(_settings);
            
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/PackageGuardian/Styles/PackageGuardian.uss");
            if (styleSheet != null)
            {
                rootElement.styleSheets.Add(styleSheet);
            }
            
            CreateUI(rootElement);
        }

        private void CreateUI(VisualElement root)
        {
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;

            // Header
            var header = new VisualElement();
            header.AddToClassList("pg-section");
            
            var title = new Label("Package Guardian Settings");
            title.AddToClassList("pg-title");
            title.style.marginBottom = 8;
            header.Add(title);

            var description = new Label("Configure Package Guardian VCS behavior, automatic snapshots, and project tracking.");
            description.AddToClassList("pg-label-secondary");
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = 8;
            header.Add(description);

            scrollView.Add(header);

            // Enable/Disable Section (most prominent)
            scrollView.Add(CreateEnableDisableSection());

            // Automatic Snapshots Section
            scrollView.Add(CreateAutomaticSnapshotsSection());

            // Author Information Section
            scrollView.Add(CreateAuthorSection());

            // Tracked Directories Section
            scrollView.Add(CreateTrackedDirectoriesSection());

            // Large Files Section
            scrollView.Add(CreateLargeFilesSection());

            // Performance Section
            scrollView.Add(CreatePerformanceSection());
            
            // First Import Warning Section
            scrollView.Add(CreateFirstImportSection());

            // Action Buttons
            scrollView.Add(CreateActionsSection());

            root.Add(scrollView);
        }

        private VisualElement CreateEnableDisableSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");
            section.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            section.style.paddingTop = 16;
            section.style.paddingBottom = 16;
            section.style.paddingLeft = 16;
            section.style.paddingRight = 16;
            section.style.marginBottom = 20;

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;

            _enabledToggle = new Toggle("Enable Package Guardian");
            _enabledToggle.value = _settings.enabled;
            _enabledToggle.tooltip = "When disabled, all Package Guardian features are turned off. You can re-enable it anytime.";
            _enabledToggle.style.fontSize = 16;
            _enabledToggle.style.marginRight = 12;
            
            var statusLabel = new Label(_settings.enabled ? "Active" : "Disabled");
            statusLabel.style.color = _settings.enabled ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.8f, 0.3f, 0.3f);
            statusLabel.style.fontSize = 14;
            statusLabel.style.marginLeft = 8;
            
            _enabledToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.enabled = evt.newValue;
                _settings.Save();
                
                statusLabel.text = evt.newValue ? "Active" : "Disabled";
                statusLabel.style.color = evt.newValue ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.8f, 0.3f, 0.3f);
                
                if (evt.newValue)
                {
                    Debug.Log("[Package Guardian] Package Guardian has been enabled");
                }
                else
                {
                    Debug.Log("[Package Guardian] Package Guardian has been disabled. All monitoring and protection features are now off.");
                }
            });
            
            container.Add(_enabledToggle);
            container.Add(statusLabel);
            section.Add(container);

            var description = new Label("When disabled, Package Guardian will not monitor imports, create stashes, or perform any automatic operations. You can still manually use Package Guardian features if needed.");
            description.AddToClassList("pg-label-small");
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginTop = 12;
            description.style.color = new Color(0.7f, 0.7f, 0.7f);
            section.Add(description);

            return section;
        }

        private VisualElement CreateAutomaticSnapshotsSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");

            var sectionTitle = new Label("AUTOMATIC SNAPSHOTS & STASHES");
            sectionTitle.AddToClassList("pg-section-title");
            section.Add(sectionTitle);

            var sectionDesc = new Label("Configure when Package Guardian automatically creates snapshots or stashes.");
            sectionDesc.AddToClassList("pg-label-small");
            sectionDesc.style.whiteSpace = WhiteSpace.Normal;
            sectionDesc.style.marginBottom = 12;
            section.Add(sectionDesc);

            _autoSnapshotToggle = new Toggle("Auto-snapshot on file save");
            _autoSnapshotToggle.value = _settings.autoSnapshotOnSave;
            _autoSnapshotToggle.tooltip = "Automatically create snapshots when files are saved (may slow down editor)";
            _autoSnapshotToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.autoSnapshotOnSave = evt.newValue;
                _settings.Save();
            });
            section.Add(_autoSnapshotToggle);

            _autoStashUPMToggle = new Toggle("Auto-stash on Unity Package Manager events");
            _autoStashUPMToggle.value = _settings.autoStashOnUPM;
            _autoStashUPMToggle.tooltip = "Create stashes when UPM packages are added/removed/updated";
            _autoStashUPMToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.autoStashOnUPM = evt.newValue;
                _settings.Save();
            });
            section.Add(_autoStashUPMToggle);

            _autoStashAssetToggle = new Toggle("Auto-stash on asset imports");
            _autoStashAssetToggle.value = _settings.autoStashOnAssetImport;
            _autoStashAssetToggle.tooltip = "Create stashes after importing .unitypackage or asset store packages";
            _autoStashAssetToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.autoStashOnAssetImport = evt.newValue;
                _settings.Save();
            });
            section.Add(_autoStashAssetToggle);

            _autoStashSceneToggle = new Toggle("Auto-stash on scene save");
            _autoStashSceneToggle.value = _settings.autoStashOnSceneSave;
            _autoStashSceneToggle.tooltip = "Create stashes when saving scene files";
            _autoStashSceneToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.autoStashOnSceneSave = evt.newValue;
                _settings.Save();
            });
            section.Add(_autoStashSceneToggle);

            return section;
        }

        private VisualElement CreateAuthorSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");

            var sectionTitle = new Label("AUTHOR INFORMATION");
            sectionTitle.AddToClassList("pg-section-title");
            section.Add(sectionTitle);

            var sectionDesc = new Label("This information is included in all snapshots and commits.");
            sectionDesc.AddToClassList("pg-label-small");
            sectionDesc.style.whiteSpace = WhiteSpace.Normal;
            sectionDesc.style.marginBottom = 12;
            section.Add(sectionDesc);

            var nameLabel = new Label("Author Name");
            nameLabel.AddToClassList("pg-label");
            nameLabel.style.marginTop = 8;
            nameLabel.style.marginBottom = 4;
            section.Add(nameLabel);

            _authorNameField = new TextField();
            _authorNameField.value = _settings.authorName;
            _authorNameField.AddToClassList("pg-input");
            _authorNameField.RegisterValueChangedCallback(evt =>
            {
                _settings.authorName = evt.newValue;
                _settings.Save();
            });
            section.Add(_authorNameField);

            var emailLabel = new Label("Author Email");
            emailLabel.AddToClassList("pg-label");
            emailLabel.style.marginTop = 12;
            emailLabel.style.marginBottom = 4;
            section.Add(emailLabel);

            _authorEmailField = new TextField();
            _authorEmailField.value = _settings.authorEmail;
            _authorEmailField.AddToClassList("pg-input");
            _authorEmailField.RegisterValueChangedCallback(evt =>
            {
                _settings.authorEmail = evt.newValue;
                _settings.Save();
            });
            section.Add(_authorEmailField);

            return section;
        }

        private VisualElement CreateTrackedDirectoriesSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");

            var sectionTitle = new Label("TRACKED DIRECTORIES");
            sectionTitle.AddToClassList("pg-section-title");
            section.Add(sectionTitle);

            var sectionDesc = new Label("Specify which root directories Package Guardian should track and include in snapshots.");
            sectionDesc.AddToClassList("pg-label-small");
            sectionDesc.style.whiteSpace = WhiteSpace.Normal;
            sectionDesc.style.marginBottom = 12;
            section.Add(sectionDesc);

            // Initialize tracked dirs list
            _trackedDirsList = new List<string>(_settings.trackedRoots);

            var listContainer = new VisualElement();
            listContainer.style.minHeight = 120;
            listContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            listContainer.style.borderLeftWidth = 1;
            listContainer.style.borderRightWidth = 1;
            listContainer.style.borderTopWidth = 1;
            listContainer.style.borderBottomWidth = 1;
            listContainer.style.borderLeftColor = new Color(0.2f, 0.2f, 0.2f);
            listContainer.style.borderRightColor = new Color(0.2f, 0.2f, 0.2f);
            listContainer.style.borderTopColor = new Color(0.2f, 0.2f, 0.2f);
            listContainer.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
            listContainer.style.paddingLeft = 8;
            listContainer.style.paddingRight = 8;
            listContainer.style.paddingTop = 8;
            listContainer.style.paddingBottom = 8;

            foreach (var dir in _trackedDirsList)
            {
                var dirItem = new VisualElement();
                dirItem.style.flexDirection = FlexDirection.Row;
                dirItem.style.alignItems = Align.Center;
                dirItem.style.marginBottom = 4;

                var dirLabel = new Label(dir);
                dirLabel.AddToClassList("pg-label");
                dirLabel.style.flexGrow = 1;
                dirItem.Add(dirLabel);

                var removeBtn = new Button(() => RemoveTrackedDirectory(dir, listContainer));
                removeBtn.text = "Remove";
                removeBtn.AddToClassList("pg-button");
                removeBtn.AddToClassList("pg-button-small");
                dirItem.Add(removeBtn);

                listContainer.Add(dirItem);
            }

            section.Add(listContainer);

            var addContainer = new VisualElement();
            addContainer.style.flexDirection = FlexDirection.Row;
            addContainer.style.marginTop = 8;

            var newDirField = new TextField();
            newDirField.value = "";
            newDirField.AddToClassList("pg-input");
            newDirField.style.flexGrow = 1;
            newDirField.style.marginRight = 8;
            addContainer.Add(newDirField);

            var addBtn = new Button(() =>
            {
                if (!string.IsNullOrWhiteSpace(newDirField.value) && !_trackedDirsList.Contains(newDirField.value))
                {
                    _trackedDirsList.Add(newDirField.value);
                    _settings.trackedRoots = new List<string>(_trackedDirsList);
                    _settings.Save();
                    
                    var dirItem = new VisualElement();
                    dirItem.style.flexDirection = FlexDirection.Row;
                    dirItem.style.alignItems = Align.Center;
                    dirItem.style.marginBottom = 4;

                    var dirLabel = new Label(newDirField.value);
                    dirLabel.AddToClassList("pg-label");
                    dirLabel.style.flexGrow = 1;
                    dirItem.Add(dirLabel);

                    var removeBtn = new Button(() => RemoveTrackedDirectory(newDirField.value, listContainer));
                    removeBtn.text = "Remove";
                    removeBtn.AddToClassList("pg-button");
                    removeBtn.AddToClassList("pg-button-small");
                    dirItem.Add(removeBtn);

                    listContainer.Add(dirItem);
                    newDirField.value = "";
                }
            });
            addBtn.text = "Add Directory";
            addBtn.AddToClassList("pg-button");
            addBtn.AddToClassList("pg-button-primary");
            addContainer.Add(addBtn);

            section.Add(addContainer);

            return section;
        }

        private void RemoveTrackedDirectory(string dir, VisualElement listContainer)
        {
            _trackedDirsList.Remove(dir);
            _settings.trackedRoots = new List<string>(_trackedDirsList);
            _settings.Save();
            
            // Rebuild list
            listContainer.Clear();
            foreach (var d in _trackedDirsList)
            {
                var dirItem = new VisualElement();
                dirItem.style.flexDirection = FlexDirection.Row;
                dirItem.style.alignItems = Align.Center;
                dirItem.style.marginBottom = 4;

                var dirLabel = new Label(d);
                dirLabel.AddToClassList("pg-label");
                dirLabel.style.flexGrow = 1;
                dirItem.Add(dirLabel);

                var removeBtn = new Button(() => RemoveTrackedDirectory(d, listContainer));
                removeBtn.text = "Remove";
                removeBtn.AddToClassList("pg-button");
                removeBtn.AddToClassList("pg-button-small");
                dirItem.Add(removeBtn);

                listContainer.Add(dirItem);
            }
        }

        private VisualElement CreateLargeFilesSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");

            var sectionTitle = new Label("LARGE FILE SUPPORT");
            sectionTitle.AddToClassList("pg-section-title");
            section.Add(sectionTitle);

            var sectionDesc = new Label("Configure how Package Guardian handles large files to optimize storage and performance.");
            sectionDesc.AddToClassList("pg-label-small");
            sectionDesc.style.whiteSpace = WhiteSpace.Normal;
            sectionDesc.style.marginBottom = 12;
            section.Add(sectionDesc);

            _largeFileToggle = new Toggle("Enable large file support");
            _largeFileToggle.value = _settings.enableLargeFileSupport;
            _largeFileToggle.tooltip = "Use chunked storage for large files to improve performance";
            _largeFileToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.enableLargeFileSupport = evt.newValue;
                _settings.Save();
            });
            section.Add(_largeFileToggle);

            var thresholdLabel = new Label("Large file threshold (MB)");
            thresholdLabel.AddToClassList("pg-label");
            thresholdLabel.style.marginTop = 12;
            thresholdLabel.style.marginBottom = 4;
            section.Add(thresholdLabel);

            _largeFileThresholdField = new TextField();
            _largeFileThresholdField.value = (_settings.largeFileThreshold / 1048576).ToString("F1");
            _largeFileThresholdField.AddToClassList("pg-input");
            _largeFileThresholdField.RegisterValueChangedCallback(evt =>
            {
                if (float.TryParse(evt.newValue, out float mb))
                {
                    _settings.largeFileThreshold = (long)(mb * 1048576);
                    _settings.Save();
                }
            });
            section.Add(_largeFileThresholdField);

            var hint = new Label("Files larger than this threshold will use optimized storage");
            hint.AddToClassList("pg-label-small");
            hint.style.marginTop = 4;
            section.Add(hint);

            return section;
        }

        private VisualElement CreatePerformanceSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");

            var sectionTitle = new Label("PERFORMANCE & RELIABILITY");
            sectionTitle.AddToClassList("pg-section-title");
            section.Add(sectionTitle);

            var sectionDesc = new Label("Advanced settings for data integrity and performance tuning.");
            sectionDesc.AddToClassList("pg-label-small");
            sectionDesc.style.whiteSpace = WhiteSpace.Normal;
            sectionDesc.style.marginBottom = 12;
            section.Add(sectionDesc);

            _fsyncToggle = new Toggle("Enable fsync for critical writes");
            _fsyncToggle.value = _settings.enableFsync;
            _fsyncToggle.tooltip = "Force filesystem sync after critical operations (slower but safer)";
            _fsyncToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.enableFsync = evt.newValue;
                _settings.Save();
            });
            section.Add(_fsyncToggle);

            var warning = new Label("Warning: Enabling fsync may reduce performance but increases data safety.");
            warning.AddToClassList("pg-label-small");
            warning.style.color = new Color(0.89f, 0.65f, 0.29f);
            warning.style.marginTop = 4;
            section.Add(warning);

            return section;
        }
        
        private VisualElement CreateFirstImportSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");

            var sectionTitle = new Label("FIRST IMPORT WELCOME MESSAGE");
            sectionTitle.AddToClassList("pg-section-title");
            section.Add(sectionTitle);

            var sectionDesc = new Label("Configure the welcome message shown when importing Package Guardian for the first time.");
            sectionDesc.AddToClassList("pg-label-small");
            sectionDesc.style.whiteSpace = WhiteSpace.Normal;
            sectionDesc.style.marginBottom = 12;
            section.Add(sectionDesc);

            _showFirstImportToggle = new Toggle("Show welcome message on first import");
            _showFirstImportToggle.value = _settings.showFirstImportWarning;
            _showFirstImportToggle.tooltip = "When enabled, shows a friendly welcome message when Package Guardian is first imported";
            _showFirstImportToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.showFirstImportWarning = evt.newValue;
                _settings.Save();
            });
            section.Add(_showFirstImportToggle);

            var statusLabel = new Label();
            statusLabel.AddToClassList("pg-label");
            statusLabel.style.marginTop = 12;
            bool hasShown = EditorPrefs.GetBool("YUCP.Components.FirstImportWarningShown", false);
            statusLabel.text = hasShown ? "Status: Welcome message has been shown" : "Status: Welcome message has not been shown yet";
            statusLabel.style.marginBottom = 8;
            section.Add(statusLabel);

            var resetBtn = new Button(() =>
            {
                EditorPrefs.DeleteKey("YUCP.Components.FirstImportWarningShown");
                statusLabel.text = "Status: Welcome message has not been shown yet";
                Debug.Log("First import welcome message has been reset. It will show again on next Unity restart if enabled.");
            });
            resetBtn.text = "Reset Welcome Message";
            resetBtn.AddToClassList("pg-button");
            resetBtn.tooltip = "Reset the welcome message so it shows again on next Unity launch (if enabled)";
            section.Add(resetBtn);

            return section;
        }

        private VisualElement CreateActionsSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");

            var sectionTitle = new Label("ACTIONS");
            sectionTitle.AddToClassList("pg-section-title");
            section.Add(sectionTitle);

            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.marginTop = 8;

            var resetBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog(
                    "Reset Settings",
                    "Are you sure you want to reset all Package Guardian settings to defaults? This cannot be undone.",
                    "Reset",
                    "Cancel"))
                {
                    // Reset to defaults
                    _settings.enabled = false;
                    _settings.autoSnapshotOnSave = false;
                    _settings.autoStashOnUPM = true;
                    _settings.autoStashOnAssetImport = true;
                    _settings.autoStashOnSceneSave = true;
                    _settings.authorName = "Unity User";
                    _settings.authorEmail = "user@unity.com";
                    _settings.trackedRoots = new List<string> { "Assets", "Packages" };
                    _settings.enableLargeFileSupport = false;
                    _settings.largeFileThreshold = 52428800;
                    _settings.enableFsync = false;
                    _settings.showFirstImportWarning = true;
                    _settings.Save();

                    // Refresh UI
                    RefreshAllFields();
                }
            });
            resetBtn.text = "Reset to Defaults";
            resetBtn.AddToClassList("pg-button");
            resetBtn.AddToClassList("pg-button-danger");
            resetBtn.style.marginRight = 8;
            buttonContainer.Add(resetBtn);

            var dashboardBtn = new Button(() =>
            {
                PackageGuardianWindow.ShowWindow();
            });
            dashboardBtn.text = "Open Package Guardian Dashboard";
            dashboardBtn.AddToClassList("pg-button");
            dashboardBtn.AddToClassList("pg-button-primary");
            buttonContainer.Add(dashboardBtn);

            section.Add(buttonContainer);

            return section;
        }

        private void RefreshAllFields()
        {
            if (_enabledToggle != null) _enabledToggle.value = _settings.enabled;
            if (_autoSnapshotToggle != null) _autoSnapshotToggle.value = _settings.autoSnapshotOnSave;
            if (_autoStashUPMToggle != null) _autoStashUPMToggle.value = _settings.autoStashOnUPM;
            if (_autoStashAssetToggle != null) _autoStashAssetToggle.value = _settings.autoStashOnAssetImport;
            if (_autoStashSceneToggle != null) _autoStashSceneToggle.value = _settings.autoStashOnSceneSave;
            if (_authorNameField != null) _authorNameField.value = _settings.authorName;
            if (_authorEmailField != null) _authorEmailField.value = _settings.authorEmail;
            if (_largeFileToggle != null) _largeFileToggle.value = _settings.enableLargeFileSupport;
            if (_largeFileThresholdField != null) _largeFileThresholdField.value = (_settings.largeFileThreshold / 1048576).ToString("F1");
            if (_fsyncToggle != null) _fsyncToggle.value = _settings.enableFsync;
            if (_showFirstImportToggle != null) _showFirstImportToggle.value = _settings.showFirstImportWarning;
        }

        [SettingsProvider]
        public static SettingsProvider CreatePackageGuardianSettingsProvider()
        {
            var provider = new PackageGuardianSettingsProvider("Project/Package Guardian", SettingsScope.Project)
            {
                label = "Package Guardian",
                keywords = new HashSet<string>(new[] { "package", "guardian", "vcs", "version", "snapshot", "stash", "yucp" })
            };

            return provider;
        }

        [MenuItem("Tools/Package Guardian/Settings", priority = 300)]
        public static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/Package Guardian");
        }
    }
}

