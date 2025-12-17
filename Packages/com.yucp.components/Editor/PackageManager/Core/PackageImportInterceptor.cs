#define YUCP_PACKAGE_MANAGER_DISABLED
#if !YUCP_PACKAGE_MANAGER_DISABLED
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Intercepts Unity's package import flow to show custom PackageManagerWindow for packages with YUCP metadata.
    /// </summary>
    [InitializeOnLoad]
    internal static class PackageImportInterceptor
    {
        private static Type _packageImportWizardType;
        private static MethodInfo _originalStartImportMethod;
        private static MethodInfo _showImportWindowMethod;
        private static PropertyInfo _instanceProperty;
        private static FieldInfo _mImportWindowField;
        private static FieldInfo _mInitialImportItemsField;
        private static FieldInfo _mAssetContentItemsField;
        private static FieldInfo _mProjectSettingItemsField;
        private static FieldInfo _mPackagePathField;
        private static FieldInfo _mPackageIconPathField;
        private static FieldInfo _mIsProjectSettingStepField;
        private static bool _isInitialized = false;
        private static bool _customWindowShown = false;
        private static object _harmonyInstance;
        private static bool _harmonyPatched = false;
        private static Type _harmonyType;
        private static Type _harmonyMethodType;

        static PackageImportInterceptor()
        {
            try
            {
                if (!PackageManagerRuntimeSettings.IsEnabled())
                {
                    Debug.Log("[YUCP PackageManager] Package Manager is disabled in settings; import interception will not initialize.");
                    return;
                }

                Initialize();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Error in static constructor: {ex.Message}\n{ex.StackTrace}");
                EditorApplication.delayCall += Initialize;
            }
        }

        private static void Initialize()
        {
            if (_isInitialized) return;
            if (!PackageManagerRuntimeSettings.IsEnabled())
                return;

            try
            {
                _packageImportWizardType = Type.GetType("UnityEditor.PackageImportWizard, UnityEditor.CoreModule");
                if (_packageImportWizardType == null)
                {
                    Debug.LogWarning("[YUCP PackageManager] PackageImportWizard type not found");
                    return;
                }

                var importPackageItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
                if (importPackageItemType == null)
                {
                    Debug.LogError("[YUCP PackageManager] ImportPackageItem type not found!");
                    return;
                }

                _originalStartImportMethod = _packageImportWizardType.GetMethod("StartImport",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), importPackageItemType.MakeArrayType(), typeof(string) },
                    null);

                if (_originalStartImportMethod == null)
                {
                    Debug.LogError("[YUCP PackageManager] StartImport method not found!");
                    return;
                }

                _showImportWindowMethod = _packageImportWizardType.GetMethod("ShowImportWindow",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { importPackageItemType.MakeArrayType() },
                    null);

                _mImportWindowField = _packageImportWizardType.GetField("m_ImportWindow", BindingFlags.NonPublic | BindingFlags.Instance);
                _mInitialImportItemsField = _packageImportWizardType.GetField("m_InitialImportItems", BindingFlags.NonPublic | BindingFlags.Instance);
                _mAssetContentItemsField = _packageImportWizardType.GetField("m_AssetContentItems", BindingFlags.NonPublic | BindingFlags.Instance);
                _mProjectSettingItemsField = _packageImportWizardType.GetField("m_ProjectSettingItems", BindingFlags.NonPublic | BindingFlags.Instance);
                _mPackagePathField = _packageImportWizardType.GetField("m_PackagePath", BindingFlags.NonPublic | BindingFlags.Instance);
                _mPackageIconPathField = _packageImportWizardType.GetField("m_PackageIconPath", BindingFlags.NonPublic | BindingFlags.Instance);
                _mIsProjectSettingStepField = _packageImportWizardType.GetField("m_IsProjectSettingStep", BindingFlags.NonPublic | BindingFlags.Instance);

                var scriptableSingletonType = Type.GetType("UnityEditor.ScriptableSingleton`1, UnityEditor.CoreModule");
                if (scriptableSingletonType != null)
                {
                    var genericType = scriptableSingletonType.MakeGenericType(_packageImportWizardType);
                    _instanceProperty = genericType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                }

                ApplyHarmonyPatch();
                AssetDatabase.importPackageStarted += OnImportPackageStarted;

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to initialize interceptor: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void OnImportPackageStarted(string packageName)
        {
            _customWindowShown = false;
            
            if (!_harmonyPatched)
            {
                Debug.Log("[YUCP PackageManager] Harmony not available, using early polling fallback (CheckAndIntercept)");
                int attempts = 0;
                EditorApplication.update += CheckAndIntercept;

                void CheckAndIntercept()
                {
                    attempts++;
                    
                    object wizardInstance = GetWizardInstance();
                    if (wizardInstance == null)
                    {
                        if (attempts > 20)
                        {
                            EditorApplication.update -= CheckAndIntercept;
                        }
                        return;
                    }
                    
                    string packagePath = GetFieldValue<string>(wizardInstance, _mPackagePathField);
                    System.Array allItems = GetFieldValue<System.Array>(wizardInstance, _mInitialImportItemsField);
                    
                    if (!string.IsNullOrEmpty(packagePath) && allItems != null && allItems.Length > 0)
                    {
                        object window = GetFieldValue<object>(wizardInstance, _mImportWindowField);
                        if (window != null)
                        {
                            try
                            {
                                var windowType = window.GetType();
                                var closeMethod = windowType.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance);
                                closeMethod?.Invoke(window, null);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[YUCP PackageManager] Error closing Unity window: {ex.Message}");
                            }
                            _mImportWindowField?.SetValue(wizardInstance, null);
                        }
                        
                        if (!_customWindowShown)
                        {
                            if (TryInterceptImportEarly())
                            {
                                _customWindowShown = true;
                                EditorApplication.update -= CheckAndIntercept;
                                return;
                            }
                        }
                    }
                    
                    if (attempts > 20)
                    {
                        EditorApplication.update -= CheckAndIntercept;
                    }
                }
            }
            else
            {
            }
        }
        private static bool TryInterceptImportEarly()
        {
            try
            {
                object wizardInstance = GetWizardInstance();
                if (wizardInstance == null) return false;

                System.Array allItems = GetFieldValue<System.Array>(wizardInstance, _mInitialImportItemsField);
                if (allItems == null || allItems.Length == 0) return false;

                object existingWindow = GetFieldValue<object>(wizardInstance, _mImportWindowField);
                if (existingWindow != null)
                {
                    var windowType = existingWindow.GetType();
                    var closeMethod = windowType.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance);
                    closeMethod?.Invoke(existingWindow, null);
                    _mImportWindowField?.SetValue(wizardInstance, null);
                }

                string packagePath = GetFieldValue<string>(wizardInstance, _mPackagePathField) ?? "";
                string iconPath = GetFieldValue<string>(wizardInstance, _mPackageIconPathField) ?? "";
                
                bool isProjectStep = GetFieldValue<bool>(wizardInstance, _mIsProjectSettingStepField);
                System.Array stepItems = null;
                
                if (isProjectStep && _mProjectSettingItemsField != null)
                {
                    var list = _mProjectSettingItemsField.GetValue(wizardInstance) as System.Collections.IList;
                    if (list != null && list.Count > 0)
                    {
                        var itemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
                        if (itemType != null)
                        {
                            stepItems = System.Array.CreateInstance(itemType, list.Count);
                            list.CopyTo(stepItems, 0);
                        }
                    }
                }
                else if (_mAssetContentItemsField != null)
                {
                    var list = _mAssetContentItemsField.GetValue(wizardInstance) as System.Collections.IList;
                    if (list != null && list.Count > 0)
                    {
                        var itemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
                        if (itemType != null)
                        {
                            stepItems = System.Array.CreateInstance(itemType, list.Count);
                            list.CopyTo(stepItems, 0);
                        }
                    }
                }
                
                if (stepItems == null || stepItems.Length == 0)
                {
                    stepItems = allItems;
                }

                ShowCustomImportWindow(wizardInstance, stepItems, allItems, packagePath, iconPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Early interception failed: {ex.Message}");
                return false;
            }
        }

        private static bool HasYUCPMetadata(System.Array importItems)
        {
            if (importItems == null || importItems.Length == 0)
                return false;

            var itemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            if (itemType == null) return false;

            var destinationPathField = itemType.GetField("destinationAssetPath");
            if (destinationPathField == null) return false;

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = destinationPathField.GetValue(item) as string;
                if (destinationPath != null && destinationPath.Equals("Assets/YUCP_PackageInfo.json", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ShowCustomImportWindow(object wizardInstance, System.Array items, System.Array allItems, string packagePath, string iconPath)
        {
            try
            {
                object wizard = wizardInstance ?? GetWizardInstance();
                
                bool isProjectStep = false;
                if (wizard != null)
                {
                    isProjectStep = PackageUtilityReflection.IsProjectSettingStep(wizard);
                }

                var existingWindows = UnityEngine.Resources.FindObjectsOfTypeAll<PackageManagerWindow>();
                foreach (var existingWindow in existingWindows)
                {
                    if (existingWindow != null)
                    {
                        existingWindow.Close();
                    }
                }
                
                try
                {
                    var packageImportType = Type.GetType("UnityEditor.PackageImport, UnityEditor.CoreModule");
                    if (packageImportType != null)
                    {
                        var allWindows = UnityEngine.Resources.FindObjectsOfTypeAll(packageImportType);
                        foreach (var win in allWindows)
                        {
                            if (win != null)
                            {
                                var closeMethod = packageImportType.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance);
                                closeMethod?.Invoke(win, null);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP PackageManager] Failed to close Unity's window: {ex.Message}");
                }
                
                if (wizard != null)
                {
                    object unityWindow = GetFieldValue<object>(wizard, _mImportWindowField);
                    if (unityWindow != null)
                    {
                        try
                        {
                            var windowType = unityWindow.GetType();
                            var closeMethod = windowType.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance);
                            closeMethod?.Invoke(unityWindow, null);
                        }
                        catch { }
                        _mImportWindowField?.SetValue(wizard, null);
                    }
                }
                
                // Create as a utility window (required for ShowModalUtility to work correctly)
                var window = EditorWindow.GetWindow<PackageManagerWindow>(true, "Import Unity Package");
                window.InitializeForImport(packagePath, items, allItems, iconPath, wizard, isProjectStep);
                window.Focus();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to show custom import window: {ex.Message}\n{ex.StackTrace}");
                CallOriginalStartImport(wizardInstance, packagePath, items, iconPath);
            }
        }

        private static void CallOriginalStartImport(object wizardInstance, string packagePath, System.Array items, string iconPath)
        {
            try
            {
                if (_originalStartImportMethod == null || wizardInstance == null)
                {
                    Debug.LogError("[YUCP PackageManager] Cannot call original StartImport - method or instance is null");
                    return;
                }

                _originalStartImportMethod.Invoke(wizardInstance, new object[] { packagePath, items, iconPath });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to call original StartImport: {ex.Message}");
            }
        }

        private static object GetWizardInstance()
        {
            try
            {
                var scriptableSingletonType = Type.GetType("UnityEditor.ScriptableSingleton`1, UnityEditor.CoreModule");
                if (scriptableSingletonType != null && _packageImportWizardType != null)
                {
                    var genericType = scriptableSingletonType.MakeGenericType(_packageImportWizardType);
                    var instanceProperty = genericType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProperty != null)
                    {
                        return instanceProperty.GetValue(null);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to get wizard instance: {ex.Message}");
            }
            return null;
        }

        private static T GetFieldValue<T>(object obj, FieldInfo field)
        {
            if (field == null || obj == null) return default(T);
            try
            {
                object value = field.GetValue(obj);
                if (value == null) return default(T);
                if (value is T)
                    return (T)value;
                // Try to convert if possible
                if (typeof(T).IsAssignableFrom(value.GetType()))
                    return (T)value;
            }
            catch { }
            return default(T);
        }

        private static void ApplyHarmonyPatch()
        {
            try
            {
                if (_harmonyPatched) return;

                bool harmonyAvailable = false;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.FullName.StartsWith("0Harmony"))
                    {
                        harmonyAvailable = true;
                        break;
                    }
                }

                if (!harmonyAvailable) return;
                if (_originalStartImportMethod == null) return;

                _harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony");
                _harmonyMethodType = Type.GetType("HarmonyLib.HarmonyMethod, 0Harmony");
                
                if (_harmonyType == null || _harmonyMethodType == null) return;

                var harmonyConstructor = _harmonyType.GetConstructor(new[] { typeof(string) });
                if (harmonyConstructor == null) return;
                
                _harmonyInstance = harmonyConstructor.Invoke(new object[] { "com.yucp.components.packageimport" });
                
                var prefixMethod = typeof(PackageImportInterceptor).GetMethod(
                    nameof(StartImportPrefix),
                    BindingFlags.NonPublic | BindingFlags.Static
                );

                if (prefixMethod == null) return;
                
                var harmonyMethodConstructor = _harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
                if (harmonyMethodConstructor == null) return;
                
                var harmonyMethodInstance = harmonyMethodConstructor.Invoke(new object[] { prefixMethod });
                
                try
                {
                    var patchMethod = _harmonyType.GetMethod("Patch", 
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(MethodBase), _harmonyMethodType, _harmonyMethodType, _harmonyMethodType, _harmonyMethodType },
                        null);
                    
                    if (patchMethod == null) return;
                    
                    patchMethod.Invoke(_harmonyInstance, new object[] { 
                        _originalStartImportMethod,
                        harmonyMethodInstance,
                        null,
                        null,
                        null
                    });
                    _harmonyPatched = true;
                    Debug.Log("[YUCP PackageManager] Harmony patch applied successfully. Using StartImportPrefix for interception.");
                }
                catch (Exception patchEx)
                {
                    Debug.LogError($"[YUCP PackageManager] Harmony patch failed: {patchEx.Message}\n{patchEx.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to apply Harmony patch: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static bool StartImportPrefix(object __instance, string packagePath, object items, string packageIconPath)
        {
            try
            {
                System.Array itemsArray = items as System.Array;
                if (itemsArray == null && items != null)
                {
                    return true;
                }
                
                if (itemsArray == null)
                {
                    return true;
                }

                _mPackagePathField?.SetValue(__instance, packagePath);
                _mPackageIconPathField?.SetValue(__instance, packageIconPath);
                _mInitialImportItemsField?.SetValue(__instance, itemsArray);
                
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (_customWindowShown)
                        {
                            Debug.LogWarning("[YUCP PackageManager] Custom window already shown, skipping duplicate call");
                            return;
                        }
                        
                        bool isProjectStep = GetFieldValue<bool>(__instance, _mIsProjectSettingStepField);
                        System.Array stepItems = itemsArray;
                        
                        _customWindowShown = true;
                        ShowCustomImportWindow(__instance, stepItems, itemsArray, packagePath, packageIconPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[YUCP PackageManager] Error showing custom window: {ex.Message}\n{ex.StackTrace}");
                    }
                };
                
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Harmony prefix error: {ex.Message}\n{ex.StackTrace}");
                return true;
            }
        }

    }
}
#endif

