using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Gesture Manager Input Emulator components during avatar build.
    /// Creates runtime input handlers for direct parameter control without VRCFury.
    /// </summary>
    public class GestureManagerInputEmulatorProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 200; // Run after other YUCP components

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<GestureManagerInputEmulator>(true);
            
            if (dataList.Length > 0)
            {
                var progressWindow = YUCPProgressWindow.Create();
                progressWindow.Progress(0, "Processing Gesture Manager Input Emulators...");
                
                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];
                    
                    if (!ValidateData(data))
                    {
                        Debug.LogError($"[GestureManagerInputEmulatorProcessor] Validation failed for '{data.name}'", data);
                        continue;
                    }

                    ProcessInputEmulator(data);
                    
                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed input emulator {i + 1}/{dataList.Length}");
                }
                
                progressWindow.CloseWindow();
            }

            return true;
        }

        private bool ValidateData(GestureManagerInputEmulator data)
        {
            if (data == null)
            {
                Debug.LogError("[GestureManagerInputEmulatorProcessor] Data component is null");
                return false;
            }

            if (!data.enableInputEmulation)
            {
                Debug.Log($"[GestureManagerInputEmulatorProcessor] Input emulation disabled for '{data.name}'", data);
                return true; // Not an error, just skip processing
            }

            var activeMappings = data.GetActiveMappings();
            if (activeMappings.Count == 0)
            {
                Debug.LogWarning($"[GestureManagerInputEmulatorProcessor] No active input mappings for '{data.name}'", data);
                return true; // Not an error, just no mappings to process
            }

            // Validate Gesture Manager - get the GameObject and find the GestureManager component
            GameObject gestureManagerObject = data.gestureManager;
            
            if (gestureManagerObject == null)
            {
                // Auto-detect if not assigned
                gestureManagerObject = FindGestureManagerGameObject(data);
            }
            
            if (gestureManagerObject == null)
            {
                Debug.LogError($"[GestureManagerInputEmulatorProcessor] No Gesture Manager GameObject found for '{data.name}'", data);
                return false;
            }
            
            // Get the GestureManager component from the GameObject
            Component gestureManager = null;
            var components = gestureManagerObject.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp.GetType().Name == "GestureManager")
                {
                    gestureManager = comp;
                    break;
                }
            }
            
            if (gestureManager == null)
            {
                Debug.LogError($"[GestureManagerInputEmulatorProcessor] No GestureManager component found on GameObject '{gestureManagerObject.name}'", data);
                return false;
            }
            
            Debug.Log($"[GestureManagerInputEmulatorProcessor] Found Gesture Manager: {gestureManager.GetType().Name} on GameObject '{gestureManagerObject.name}'");

            return true;
        }

        private GameObject FindGestureManagerGameObject(GestureManagerInputEmulator data)
        {
            // Look for the exact GestureManager component
            var allComponents = data.GetComponents<Component>();
            
            foreach (var comp in allComponents)
            {
                // Look for the exact GestureManager component
                if (comp.GetType().Name == "GestureManager")
                {
                    return comp.gameObject;
                }
            }

            // Look in parent objects
            Transform current = data.transform.parent;
            while (current != null)
            {
                var components = current.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp.GetType().Name == "GestureManager")
                    {
                        return comp.gameObject;
                    }
                }
                current = current.parent;
            }

            // Look in children
            var childComponents = data.GetComponentsInChildren<Component>();
            foreach (var comp in childComponents)
            {
                if (comp.GetType().Name == "GestureManager")
                {
                    return comp.gameObject;
                }
            }

            // Fallback: look for any component with "gesture" and "manager" but not "input" or "emulator"
            foreach (var comp in allComponents)
            {
                string typeName = comp.GetType().Name.ToLower();
                if ((typeName.Contains("gesture") && typeName.Contains("manager")) && 
                    !typeName.Contains("input") && !typeName.Contains("emulator"))
                {
                    return comp.gameObject;
                }
            }

            return null;
        }

        private void ProcessInputEmulator(GestureManagerInputEmulator data)
        {
            var activeMappings = data.GetActiveMappings();
            GameObject gestureManagerObject = data.gestureManager;


            // Get the GestureManager component from the GameObject
            Component gestureManager = null;
            if (gestureManagerObject != null)
            {
                // Look for the actual GestureManager component
                var components = gestureManagerObject.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp.GetType().Name == "GestureManager")
                    {
                        gestureManager = comp;
                        break;
                    }
                }
            }
            
            // Determine Gesture Manager mode
            GestureManagerMode detectedMode = DetermineGestureManagerMode(data, gestureManager);
            Debug.Log($"[GestureManagerInputEmulatorProcessor] Detected Gesture Manager mode: {detectedMode}", data);

            // Create runtime input handler
            var inputHandler = data.gameObject.GetComponent<GestureManagerInputHandler>();
            if (inputHandler == null)
            {
                inputHandler = data.gameObject.AddComponent<GestureManagerInputHandler>();
                Debug.Log($"[GestureManagerInputEmulatorProcessor] Created runtime input handler for '{data.name}'", inputHandler);
            }
            else
            {
                Debug.Log($"[GestureManagerInputEmulatorProcessor] Found existing runtime input handler for '{data.name}'", inputHandler);
            }
            
            // Preserve the runtime handler during build
            inputHandler.hideFlags = HideFlags.None;

            // Configure input handler
            inputHandler.enableInputCapture = data.enableInputEmulation;
            inputHandler.debugMode = data.debugMode;
            inputHandler.showInSceneView = data.debugMode; // Show in Scene view when debug mode is enabled
            inputHandler.controllerDeadzone = data.controllerDeadzone;
            inputHandler.axisSensitivity = data.axisSensitivity;
            inputHandler.gestureManagerMode = detectedMode;
            
            // Set the Gesture Manager reference that was already found
            if (gestureManager == null)
            {
                Debug.LogError($"[GestureManagerInputEmulatorProcessor] GestureManager component is null when trying to set it on input handler", data);
            }
            else
            {
                Debug.Log($"[GestureManagerInputEmulatorProcessor] Setting GestureManager component: {gestureManager.GetType().Name}", data);
            }
            inputHandler.SetGestureManager(gestureManager);
                        
                        Debug.Log($"[GestureManagerInputEmulatorProcessor] Set debug mode to: {data.debugMode}, Scene view: {inputHandler.showInSceneView}");

            // Add runtime mappings to handler
            foreach (var mapping in activeMappings)
            {
                inputHandler.AddRuntimeMapping(mapping);
            }

            // No VRCFury integration needed - direct parameter control via runtime handler
            Debug.Log($"[GestureManagerInputEmulatorProcessor] Configured input mappings for direct parameter control", data);

            // Set build statistics
            data.SetBuildStats(activeMappings.Count, 1);
            
            Debug.Log($"[GestureManagerInputEmulatorProcessor] Created input mapping components for '{data.name}'", data);
        }

        private GestureManagerMode DetermineGestureManagerMode(GestureManagerInputEmulator data, Component gestureManager)
        {
            if (data.gestureManagerMode != GestureManagerMode.AutoDetect)
            {
                return data.gestureManagerMode;
            }

            // Auto-detect using Gesture Manager type
            if (gestureManager != null)
            {
                string typeName = gestureManager.GetType().Name.ToLower();
                if (typeName.Contains("modulevrc3") || typeName.Contains("gesturemanager"))
                {
                    return GestureManagerMode.GestureManagerMode;
                }
                else if (typeName.Contains("animator"))
                {
                    return GestureManagerMode.AnimatorMode;
                }
            }

            // Default to Gesture Manager mode
            return GestureManagerMode.GestureManagerMode;
        }
    }
}