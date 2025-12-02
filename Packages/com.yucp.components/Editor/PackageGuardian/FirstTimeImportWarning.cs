using UnityEditor;
using UnityEngine;
using YUCP.Components.PackageGuardian.Editor.Settings;

namespace YUCP.Components.PackageGuardian
{
    [InitializeOnLoad]
    public static class FirstTimeImportWarning
    {
        private const string PREF_KEY = "YUCP.Components.FirstImportWarningShown";
        private const string PACKAGE_NAME = "YUCP Components";
        
        static FirstTimeImportWarning()
        {
            // Use EditorApplication.delayCall
            EditorApplication.delayCall += ShowFirstTimeWarning;
        }
        
        private static void ShowFirstTimeWarning()
        {
            // Remove the callback
            EditorApplication.delayCall -= ShowFirstTimeWarning;
            
            // Check if the warning has already been shown
            if (EditorPrefs.GetBool(PREF_KEY, false))
            {
                return;
            }
            
            // Check if the user has disabled the warning in settings
            var settings = PackageGuardianSettings.Instance;
            if (!settings.showFirstImportWarning)
            {
                // Mark as shown so it doesn't show even if they re-enable the setting
                EditorPrefs.SetBool(PREF_KEY, true);
                return;
            }
            
            // Show a friendly welcome dialog with option to disable
            string title = "Welcome to Package Guardian";
            string message = "Thanks for installing " + PACKAGE_NAME + "!\n\n" +
                           "Package Guardian helps protect your project by tracking changes to your files. " +
                           "It's setting up in the background right now - you can keep working normally.\n\n" +
                           "This setup usually takes a few minutes, but you won't notice any slowdown.\n\n" +
                           "You can open Package Guardian from the Tools menu or Project Settings if you'd like to learn more.\n\n" +
                           "Would you like to enable Package Guardian?";
            
            // Use DisplayDialogComplex to offer disable option
            int result = EditorUtility.DisplayDialogComplex(
                title,
                message,
                "Enable (Recommended)",
                "Disable Package Guardian",
                "Open Settings"
            );
            
            // Handle user choice
            if (result == 1) // Disable button
            {
                // User chose to disable
                settings.enabled = false;
                settings.Save();
                EditorUtility.DisplayDialog(
                    "Package Guardian Disabled",
                    "Package Guardian has been disabled. All automatic monitoring and protection features are now turned off.\n\n" +
                    "You can re-enable it anytime from Project Settings > Package Guardian.",
                    "OK"
                );
                Debug.Log("[Package Guardian] Package Guardian has been disabled by user from welcome dialog");
            }
            else if (result == 2) // Open Settings button
            {
                // Open settings window
                SettingsService.OpenProjectSettings("Project/Package Guardian");
            }
            // result == 0 means "Enable" was clicked, which is the default behavior
            
            // Mark the warning as shown
            EditorPrefs.SetBool(PREF_KEY, true);
        }
    }
}

