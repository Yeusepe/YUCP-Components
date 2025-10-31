using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Services;

namespace YUCP.Components.PackageGuardian.Editor.Settings
{
    /// <summary>
    /// Settings window for Health Monitor configuration
    /// </summary>
    public class HealthMonitorSettings : EditorWindow
    {
        private Toggle _enabledToggle;
        private Toggle _notificationsToggle;
        private Slider _intervalSlider;
        private Label _intervalLabel;
        private Label _lastCheckLabel;
        private Label _issueCountLabel;
        
        [MenuItem("Tools/Package Guardian/Health Monitor Settings", priority = 102)]
        public static void ShowWindow()
        {
            var window = GetWindow<HealthMonitorSettings>();
            window.titleContent = new GUIContent("Health Monitor Settings");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }
        
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 15;
            root.style.paddingRight = 15;
            root.style.paddingTop = 15;
            root.style.paddingBottom = 15;
            
            // Title
            var title = new Label("Health Monitor Settings");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 20;
            root.Add(title);
            
            // Description
            var desc = new Label("Configure automated health monitoring for your Unity project. The health monitor runs periodic checks to detect issues early and notify you of problems.");
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.marginBottom = 20;
            desc.style.color = new Color(0.8f, 0.8f, 0.8f);
            root.Add(desc);
            
            // Enabled toggle
            _enabledToggle = new Toggle("Enable Automated Health Monitoring");
            _enabledToggle.value = HealthMonitorService.IsEnabled();
            _enabledToggle.RegisterValueChangedCallback(evt => {
                HealthMonitorService.SetEnabled(evt.newValue);
                UpdateUI();
            });
            _enabledToggle.style.marginBottom = 15;
            root.Add(_enabledToggle);
            
            // Notifications toggle
            _notificationsToggle = new Toggle("Show Notifications for Critical Issues");
            _notificationsToggle.value = HealthMonitorService.GetShowNotifications();
            _notificationsToggle.RegisterValueChangedCallback(evt => {
                HealthMonitorService.SetShowNotifications(evt.newValue);
            });
            _notificationsToggle.style.marginBottom = 20;
            root.Add(_notificationsToggle);
            
            // Interval settings
            var intervalContainer = new VisualElement();
            intervalContainer.style.marginBottom = 20;
            
            var intervalTitle = new Label("Check Interval");
            intervalTitle.style.fontSize = 14;
            intervalTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            intervalTitle.style.marginBottom = 10;
            intervalContainer.Add(intervalTitle);
            
            _intervalSlider = new Slider("Minutes:", 1f, 60f);
            _intervalSlider.value = (float)(HealthMonitorService.GetCheckInterval() / 60.0);
            _intervalSlider.RegisterValueChangedCallback(evt => {
                HealthMonitorService.SetCheckInterval(evt.newValue * 60.0);
                UpdateIntervalLabel();
            });
            _intervalSlider.style.marginBottom = 5;
            intervalContainer.Add(_intervalSlider);
            
            _intervalLabel = new Label();
            _intervalLabel.style.fontSize = 11;
            _intervalLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            UpdateIntervalLabel();
            intervalContainer.Add(_intervalLabel);
            
            root.Add(intervalContainer);
            
            // Separator
            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
            separator.style.marginTop = 10;
            separator.style.marginBottom = 20;
            root.Add(separator);
            
            // Status section
            var statusTitle = new Label("Current Status");
            statusTitle.style.fontSize = 14;
            statusTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            statusTitle.style.marginBottom = 10;
            root.Add(statusTitle);
            
            _issueCountLabel = new Label();
            _issueCountLabel.style.marginBottom = 10;
            root.Add(_issueCountLabel);
            
            _lastCheckLabel = new Label("No health check has been performed yet.");
            _lastCheckLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            _lastCheckLabel.style.marginBottom = 20;
            root.Add(_lastCheckLabel);
            
            // Action buttons
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.marginBottom = 20;
            
            var checkNowButton = new Button(() => {
                HealthMonitorService.ForceHealthCheck();
                UpdateStatusLabels();
            });
            checkNowButton.text = "Run Health Check Now";
            checkNowButton.style.flexGrow = 1;
            checkNowButton.style.marginRight = 5;
            checkNowButton.style.paddingTop = 10;
            checkNowButton.style.paddingBottom = 10;
            checkNowButton.style.backgroundColor = new Color(0.06f, 0.72f, 0.51f);
            checkNowButton.style.color = Color.white;
            buttonContainer.Add(checkNowButton);
            
            var generateReportButton = new Button(() => {
                HealthReportGenerator.GenerateReport();
            });
            generateReportButton.text = "Generate Health Report";
            generateReportButton.style.flexGrow = 1;
            generateReportButton.style.marginLeft = 5;
            generateReportButton.style.paddingTop = 10;
            generateReportButton.style.paddingBottom = 10;
            generateReportButton.style.backgroundColor = new Color(0.36f, 0.47f, 1.0f);
            generateReportButton.style.color = Color.white;
            buttonContainer.Add(generateReportButton);
            
            root.Add(buttonContainer);
            
            // Info box
            var infoBox = new VisualElement();
            infoBox.style.backgroundColor = new Color(0.1f, 0.15f, 0.2f);
            infoBox.style.borderTopLeftRadius = 5;
            infoBox.style.borderTopRightRadius = 5;
            infoBox.style.borderBottomLeftRadius = 5;
            infoBox.style.borderBottomRightRadius = 5;
            infoBox.style.paddingLeft = 15;
            infoBox.style.paddingRight = 15;
            infoBox.style.paddingTop = 15;
            infoBox.style.paddingBottom = 15;
            
            var infoTitle = new Label("What is Health Monitoring?");
            infoTitle.style.fontSize = 13;
            infoTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            infoTitle.style.marginBottom = 10;
            infoBox.Add(infoTitle);
            
            var infoText = new Label(
                "Health Monitoring continuously checks your project for:\n\n" +
                "• Compilation errors and warnings\n" +
                "• Missing or duplicate asset GUIDs\n" +
                "• Package conflicts and dependency issues\n" +
                "• Broken references in scenes and prefabs\n" +
                "• Unity API compatibility problems\n" +
                "• Memory usage and performance warnings\n" +
                "• Project settings best practices\n\n" +
                "When critical issues are detected, you'll be notified immediately so you can fix them before they cause problems."
            );
            infoText.style.whiteSpace = WhiteSpace.Normal;
            infoText.style.color = new Color(0.8f, 0.85f, 0.9f);
            infoBox.Add(infoText);
            
            root.Add(infoBox);
            
            // Initial update
            UpdateUI();
            UpdateStatusLabels();
        }
        
        private void OnFocus()
        {
            UpdateStatusLabels();
        }
        
        private void UpdateUI()
        {
            if (_notificationsToggle != null)
            {
                _notificationsToggle.SetEnabled(HealthMonitorService.IsEnabled());
            }
            
            if (_intervalSlider != null)
            {
                _intervalSlider.SetEnabled(HealthMonitorService.IsEnabled());
            }
        }
        
        private void UpdateIntervalLabel()
        {
            if (_intervalLabel != null)
            {
                int minutes = (int)_intervalSlider.value;
                _intervalLabel.text = $"Health checks will run every {minutes} minute{(minutes != 1 ? "s" : "")}.";
            }
        }
        
        private void UpdateStatusLabels()
        {
            if (_issueCountLabel != null)
            {
                var lastResults = HealthMonitorService.GetLastResults();
                
                if (lastResults.Count == 0)
                {
                    _issueCountLabel.text = "Project Health: EXCELLENT - No issues detected";
                    _issueCountLabel.style.color = new Color(0.06f, 0.72f, 0.51f);
                }
                else
                {
                    var critical = lastResults.FindAll(i => i.Severity == global::PackageGuardian.Core.Validation.IssueSeverity.Critical).Count;
                    var errors = lastResults.FindAll(i => i.Severity == global::PackageGuardian.Core.Validation.IssueSeverity.Error).Count;
                    var warnings = lastResults.FindAll(i => i.Severity == global::PackageGuardian.Core.Validation.IssueSeverity.Warning).Count;
                    
                    _issueCountLabel.text = $"Project Health: {critical} critical, {errors} errors, {warnings} warnings";
                    
                    if (critical > 0)
                    {
                        _issueCountLabel.style.color = new Color(0.8f, 0.1f, 0.1f);
                    }
                    else if (errors > 0)
                    {
                        _issueCountLabel.style.color = new Color(0.89f, 0.29f, 0.29f);
                    }
                    else if (warnings > 0)
                    {
                        _issueCountLabel.style.color = new Color(0.96f, 0.62f, 0.04f);
                    }
                    else
                    {
                        _issueCountLabel.style.color = new Color(0.36f, 0.47f, 1.0f);
                    }
                }
            }
            
            if (_lastCheckLabel != null)
            {
                _lastCheckLabel.text = "Last check performed recently. Click 'Run Health Check Now' to refresh.";
            }
        }
    }
}

