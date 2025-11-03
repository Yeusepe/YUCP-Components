using UnityEditor;
using UnityEngine;

namespace YUCP.Components.PackageGuardian
{
    [InitializeOnLoad]
    public static class FirstTimeImportWarning
    {
        private const string PREF_KEY = "YUCP.Components.FirstImportWarningShown";
        private const string PACKAGE_NAME = "YUCP Components (@com.yucp.components)";
        
        static FirstTimeImportWarning()
        {
            // Use EditorApplication.delayCall to ensure Unity is fully loaded
            EditorApplication.delayCall += ShowFirstTimeWarning;
        }
        
        private static void ShowFirstTimeWarning()
        {
            // Remove the callback to prevent multiple calls
            EditorApplication.delayCall -= ShowFirstTimeWarning;
            
            // Check if the warning has already been shown
            if (EditorPrefs.GetBool(PREF_KEY, false))
            {
                return;
            }
            
            // Show the warning dialog
            string title = "First Time Import - Package Guardian";
            string message = "This is the first time you are importing " + PACKAGE_NAME + ".\n\n" +
                           "Package Guardian will now index your project files. " +
                           "This process may take 3-6 minutes depending on your machine's performance.\n\n" +
                           "You can continue working while the indexing completes in the background.";
            
            EditorUtility.DisplayDialog(title, message, "OK");
            
            // Mark the warning as shown
            EditorPrefs.SetBool(PREF_KEY, true);
        }
    }
}

