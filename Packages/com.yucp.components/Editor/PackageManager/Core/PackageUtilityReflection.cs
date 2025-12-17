using System;
using System.Reflection;
using UnityEngine;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Reflection utilities for accessing Unity's internal PackageUtility and PackageImportWizard classes.
    /// </summary>
    internal static class PackageUtilityReflection
    {
        private static Type _packageUtilityType;
        private static MethodInfo _importPackageAssetsMethod;
        private static MethodInfo _importPackageAssetsCancelledMethod;
        private static Type _packageImportWizardType;
        private static MethodInfo _doNextStepMethod;
        private static MethodInfo _finishImportMethod;
        private static MethodInfo _cancelImportMethod;
        private static MethodInfo _doPreviousStepMethod;
        private static PropertyInfo _isMultiStepWizardProperty;
        private static PropertyInfo _isProjectSettingStepProperty;
        private static bool _isInitialized = false;

        static PackageUtilityReflection()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                _packageUtilityType = Type.GetType("UnityEditor.PackageUtility, UnityEditor.CoreModule");
                if (_packageUtilityType != null)
                {
                    _importPackageAssetsMethod = _packageUtilityType.GetMethod("ImportPackageAssets",
                        BindingFlags.Public | BindingFlags.Static);
                    _importPackageAssetsCancelledMethod = _packageUtilityType.GetMethod("ImportPackageAssetsCancelledFromGUI",
                        BindingFlags.Public | BindingFlags.Static);
                }

                _packageImportWizardType = Type.GetType("UnityEditor.PackageImportWizard, UnityEditor.CoreModule");
                if (_packageImportWizardType != null)
                {
                    _doNextStepMethod = _packageImportWizardType.GetMethod("DoNextStep",
                        BindingFlags.Public | BindingFlags.Instance);
                    _finishImportMethod = _packageImportWizardType.GetMethod("FinishImport",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _cancelImportMethod = _packageImportWizardType.GetMethod("CancelImport",
                        BindingFlags.Public | BindingFlags.Instance);
                    _doPreviousStepMethod = _packageImportWizardType.GetMethod("DoPreviousStep",
                        BindingFlags.Public | BindingFlags.Instance);
                    _isMultiStepWizardProperty = _packageImportWizardType.GetProperty("IsMultiStepWizard",
                        BindingFlags.Public | BindingFlags.Instance);
                    _isProjectSettingStepProperty = _packageImportWizardType.GetProperty("IsProjectSettingStep",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to initialize reflection: {ex.Message}");
            }
        }

        public static void ImportPackageAssets(string packageName, Array items)
        {
            try
            {
                if (_importPackageAssetsMethod == null)
                {
                    Debug.LogError("[YUCP PackageManager] ImportPackageAssets method not found");
                    return;
                }

                _importPackageAssetsMethod.Invoke(null, new object[] { packageName, items });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to call ImportPackageAssets: {ex.Message}");
            }
        }

        public static void ImportPackageAssetsCancelled(string packageName, Array items)
        {
            try
            {
                if (_importPackageAssetsCancelledMethod == null)
                {
                    Debug.LogError("[YUCP PackageManager] ImportPackageAssetsCancelledFromGUI method not found");
                    return;
                }

                _importPackageAssetsCancelledMethod.Invoke(null, new object[] { packageName, items });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to call ImportPackageAssetsCancelledFromGUI: {ex.Message}");
            }
        }

        public static void DoNextStep(object wizardInstance, Array items)
        {
            try
            {
                if (_doNextStepMethod == null || wizardInstance == null)
                {
                    Debug.LogError("[YUCP PackageManager] DoNextStep method not found or wizard instance is null");
                    return;
                }

                _doNextStepMethod.Invoke(wizardInstance, new object[] { items });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to call DoNextStep: {ex.Message}");
            }
        }

        public static void DoPreviousStep(object wizardInstance, Array items)
        {
            try
            {
                if (_doPreviousStepMethod == null || wizardInstance == null)
                {
                    Debug.LogError("[YUCP PackageManager] DoPreviousStep method not found or wizard instance is null");
                    return;
                }

                _doPreviousStepMethod.Invoke(wizardInstance, new object[] { items });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to call DoPreviousStep: {ex.Message}");
            }
        }

        public static void FinishImport(object wizardInstance)
        {
            try
            {
                if (_finishImportMethod == null || wizardInstance == null)
                {
                    Debug.LogError("[YUCP PackageManager] FinishImport method not found or wizard instance is null");
                    return;
                }

                _finishImportMethod.Invoke(wizardInstance, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to call FinishImport: {ex.Message}");
            }
        }

        public static void CancelImport(object wizardInstance)
        {
            try
            {
                if (_cancelImportMethod == null || wizardInstance == null)
                {
                    Debug.LogError("[YUCP PackageManager] CancelImport method not found or wizard instance is null");
                    return;
                }

                _cancelImportMethod.Invoke(wizardInstance, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to call CancelImport: {ex.Message}");
            }
        }

        public static bool IsMultiStepWizard(object wizardInstance)
        {
            try
            {
                if (_isMultiStepWizardProperty == null || wizardInstance == null)
                {
                    return false;
                }

                return (bool)_isMultiStepWizardProperty.GetValue(wizardInstance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to get IsMultiStepWizard: {ex.Message}");
                return false;
            }
        }

        public static bool IsProjectSettingStep(object wizardInstance)
        {
            try
            {
                if (_isProjectSettingStepProperty == null || wizardInstance == null)
                {
                    return false;
                }

                return (bool)_isProjectSettingStepProperty.GetValue(wizardInstance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to get IsProjectSettingStep: {ex.Message}");
                return false;
            }
        }
    }
}




























