using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;
using YUCP.Components.Editor.UI;
using com.vrcfury.api;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Rotation Counter components during avatar build.
    /// Generates Animator Controller with sector-based rotation detection and cardinal direction flick detection.
    /// </summary>
    public class RotationCounterProcessor : IVRCSDKPreprocessAvatarCallback
    {
        private const int DriverSet = 0;
        private const int DriverAdd = 1;

        public int callbackOrder => int.MinValue + 150;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<RotationCounterData>(true);

            if (dataList.Length == 0) return true;

            var progressWindow = YUCPProgressWindow.Create();
            progressWindow.Progress(0, "Processing Rotation Counters...");

            try
            {
                var animator = avatarRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[RotationCounterProcessor] No Animator found on avatar");
                    return true;
                }

                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];

                    if (!ValidateData(data)) continue;

                    try
                    {
                        ProcessRotationCounter(data, animator);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[RotationCounterProcessor] Error processing '{data.name}': {ex.Message}", data);
                        Debug.LogException(ex);
                    }

                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed rotation counter {i + 1}/{dataList.Length}");
                }
            }
            finally
            {
                progressWindow.CloseWindow();
            }

            return true;
        }

        private bool ValidateData(RotationCounterData data)
        {
            if (data == null)
            {
                Debug.LogError("[RotationCounterProcessor] Data component is null");
                return false;
            }

            if (string.IsNullOrEmpty(data.xParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] X parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (string.IsNullOrEmpty(data.yParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Y parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (string.IsNullOrEmpty(data.angleParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Angle parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (string.IsNullOrEmpty(data.rotationStepParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Rotation step parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (string.IsNullOrEmpty(data.flickEventParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Flick event parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (data.numberOfSectors < 4 || data.numberOfSectors > 24)
            {
                Debug.LogError($"[RotationCounterProcessor] Number of sectors must be between 4 and 24 for '{data.name}'", data);
                return false;
            }

            if (data.innerDeadzone < 0f || data.innerDeadzone >= 1f)
            {
                Debug.LogError($"[RotationCounterProcessor] Inner deadzone must be between 0 and 1 for '{data.name}'", data);
                return false;
            }

            if (data.flickMinRadius <= data.innerDeadzone)
            {
                Debug.LogError($"[RotationCounterProcessor] Flick min radius must be greater than inner deadzone for '{data.name}'", data);
                return false;
            }

            if (data.releaseRadius < 0f || data.releaseRadius >= 1f)
            {
                Debug.LogError($"[RotationCounterProcessor] Release radius must be between 0 and 1 for '{data.name}'", data);
                return false;
            }

            if (data.angleToleranceDeg < 0f || data.angleToleranceDeg > 90f)
            {
                Debug.LogError($"[RotationCounterProcessor] Angle tolerance must be between 0 and 90 degrees for '{data.name}'", data);
                return false;
            }

            if (data.maxFlickFrames < 1 || data.maxFlickFrames > 30)
            {
                Debug.LogError($"[RotationCounterProcessor] Max flick frames must be between 1 and 30 for '{data.name}'", data);
                return false;
            }

            return true;
        }

        private void ProcessRotationCounter(RotationCounterData data, Animator animator)
        {
            // Find VRCFury component by name (it's internal, can't use type directly)
            Component existingVRCFury = null;
            var components = data.gameObject.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp != null && comp.GetType().Name == "VRCFury")
                {
                    existingVRCFury = comp;
                    break;
                }
            }
            
            // Check if it's a FullController
            bool isFullController = false;
            if (existingVRCFury != null)
            {
                var contentField = existingVRCFury.GetType().GetField("content", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (contentField != null)
                {
                    var content = contentField.GetValue(existingVRCFury);
                    if (content != null && content.GetType().Name == "FullController")
                    {
                        isFullController = true;
                        Debug.Log($"[RotationCounterProcessor] Found existing VRCFury FullController, will add rotation counter to it", data);
                    }
                }
            }
            
            // Generate controller
            AnimatorController controller = GenerateInMemoryController(data);
            
            if (isFullController)
            {
                // Get the existing FullController content
                var contentField = existingVRCFury.GetType().GetField("content", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var content = contentField.GetValue(existingVRCFury);
                
                // Access the controllers list via reflection
                var controllersField = content.GetType().GetField("controllers", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (controllersField == null)
                {
                    Debug.LogError($"[RotationCounterProcessor] Could not find controllers field on FullController", data);
                    return;
                }
                
                // Create controller entry and add it
                var controllerEntryType = controllersField.FieldType.GetGenericArguments()[0];
                var entry = System.Activator.CreateInstance(controllerEntryType);
                controllerEntryType.GetField("controller").SetValue(entry, controller);
                controllerEntryType.GetField("type").SetValue(entry, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);
                
                var controllersList = controllersField.GetValue(content) as System.Collections.IList;
                controllersList.Add(entry);
                
                // Add parameters as global parameters
                var globalParamsField = content.GetType().GetField("globalParams", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (globalParamsField != null)
                {
                    var globalParamsList = globalParamsField.GetValue(content) as System.Collections.IList;
                    if (globalParamsList != null)
                    {
                        // Add angle parameter if not already present
                        bool Has(string paramName)
                        {
                            if (string.IsNullOrEmpty(paramName)) return true;
                            foreach (string entry in globalParamsList)
                            {
                                if (entry == paramName) return true;
                            }
                            return false;
                        }

                        void AddIfMissing(string paramName)
                        {
                            if (string.IsNullOrEmpty(paramName)) return;
                            if (!Has(paramName)) globalParamsList.Add(paramName);
                        }

                        AddIfMissing(data.xParameterName);
                        AddIfMissing(data.yParameterName);
                        AddIfMissing(data.angleParameterName);
                        AddIfMissing(data.rotationStepParameterName);
                        AddIfMissing(data.flickEventParameterName);
                        if (data.createDebugPhaseParameter)
                        {
                            AddIfMissing(data.debugPhaseParameterName);
                        }
                    }
                }
                
                EditorUtility.SetDirty(existingVRCFury);
                
                Debug.Log($"[RotationCounterProcessor] Added rotation counter controller and global parameters to existing VRCFury FullController", data);
            }
            else
            {
                // Create new FullController
                var fullController = FuryComponents.CreateFullController(data.gameObject);
                
                // Add controller to VRCFury FullController (will integrate at build time)
                fullController.AddController(controller, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);
                
                // Add parameters as global parameters
                fullController.AddGlobalParam(data.xParameterName);
                fullController.AddGlobalParam(data.yParameterName);
                fullController.AddGlobalParam(data.angleParameterName);
                fullController.AddGlobalParam(data.rotationStepParameterName);
                fullController.AddGlobalParam(data.flickEventParameterName);
                if (data.createDebugPhaseParameter && !string.IsNullOrEmpty(data.debugPhaseParameterName))
                {
                    fullController.AddGlobalParam(data.debugPhaseParameterName);
                }
                
                Debug.Log($"[RotationCounterProcessor] Created new VRCFury FullController and added rotation counter controller with global parameters", data);
            }

            // Mark as generated
            data.controllerGenerated = true;
            data.generatedSectorCount = data.numberOfSectors;

            Debug.Log($"[RotationCounterProcessor] Generated rotation counter controller with {data.numberOfSectors} sectors and integrated via VRCFury", data);
        }

        private AnimatorController GenerateInMemoryController(RotationCounterData data)
        {
            string tempPath = "Assets/Temp_RotationCounter.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(tempPath);
            
            controller.AddParameter(data.xParameterName, AnimatorControllerParameterType.Float);
            controller.AddParameter(data.yParameterName, AnimatorControllerParameterType.Float);
            controller.AddParameter(data.angleParameterName, AnimatorControllerParameterType.Float);
            controller.AddParameter(data.rotationStepParameterName, AnimatorControllerParameterType.Int);
            controller.AddParameter(data.flickEventParameterName, AnimatorControllerParameterType.Int);

            // Internal state parameters
            controller.AddParameter("_PrevSector", AnimatorControllerParameterType.Int);
            controller.AddParameter("_FlickState", AnimatorControllerParameterType.Int);
            controller.AddParameter("_FlickDir", AnimatorControllerParameterType.Int);
            controller.AddParameter("_FlickTimer", AnimatorControllerParameterType.Int);

            if (data.createDebugPhaseParameter && !string.IsNullOrEmpty(data.debugPhaseParameterName))
            {
                controller.AddParameter(data.debugPhaseParameterName, AnimatorControllerParameterType.Int);
            }

            GenerateRotationTrackingLayer(controller, data);

            return controller;
        }

        private void GenerateRotationTrackingLayer(AnimatorController controller, RotationCounterData data)
        {
            var layers = controller.layers;
            if (layers.Length == 0)
            {
                layers = new[]
                {
                    new AnimatorControllerLayer
                    {
                        name = data.layerName,
                        stateMachine = new AnimatorStateMachine()
                    }
                };
            }

            var layer = layers[0];
            layer.name = string.IsNullOrEmpty(data.layerName) ? "SpinFlick_New" : data.layerName;
            var stateMachine = layer.stateMachine ?? new AnimatorStateMachine();

            ClearStateMachine(stateMachine);

            // Generate rotation detection states (sector tracking)
            var sectorStates = GenerateRotationDetection(data, stateMachine);
            
            // Generate flick detection states
            var flickStates = GenerateFlickDetection(data, stateMachine);

            // Set default state to first sector state
            stateMachine.defaultState = sectorStates[0];
            
            layer.stateMachine = stateMachine;
            layers[0] = layer;
            controller.layers = layers;
        }

        private AnimatorState[] GenerateRotationDetection(RotationCounterData data, AnimatorStateMachine stateMachine)
        {
            int numSectors = data.numberOfSectors;
            float sectorWidthDeg = 360f / numSectors;
            int halfSectors = numSectors / 2;
            
            var sectorStates = new AnimatorState[numSectors];
            
            // Create sector states
            for (int i = 0; i < numSectors; i++)
            {
                float sectorStartDeg = i * sectorWidthDeg;
                float sectorEndDeg = (i + 1) * sectorWidthDeg;
                
                var pos = GetSectorStatePosition(i, 0);
                var state = stateMachine.AddState($"Sector_{i}", pos);
                state.writeDefaultValues = false;
                
                // Set previous sector to current sector
                SetStateDriver(state, data, (float)i,
                    ("_PrevSector", DriverSet, (float)i));
                
                sectorStates[i] = state;
            }
            
            // Create transitions between sectors with rotation step detection
            for (int i = 0; i < numSectors; i++)
            {
                for (int j = 0; j < numSectors; j++)
                {
                    if (i == j) continue;
                    
                    int rawStep = j - i;
                    int correctedStep = rawStep;
                    
                    // Wraparound correction
                    if (rawStep > halfSectors)
                    {
                        correctedStep = rawStep - numSectors;
                    }
                    else if (rawStep < -halfSectors)
                    {
                        correctedStep = rawStep + numSectors;
                    }
                    
                    // Only create transitions for valid rotation steps (+1, -1, or wraparound equivalents)
                    // Don't create transitions for same sector (correctedStep == 0)
                    bool isValidStep = false;
                    int rotationStep = 0;
                    
                    if (correctedStep == 1 || correctedStep == -numSectors + 1)
                    {
                        isValidStep = true;
                        rotationStep = 1;
                    }
                    else if (correctedStep == -1 || correctedStep == numSectors - 1)
                    {
                        isValidStep = true;
                        rotationStep = -1;
                    }
                    
                    if (isValidStep)
                    {
                        var transition = sectorStates[i].AddTransition(sectorStates[j]);
                        ConfigureInstantTransition(transition);
                        
                        // Add angle range condition for target sector
                        float sectorStartDeg = j * sectorWidthDeg;
                        float sectorEndDeg = (j + 1) * sectorWidthDeg;
                        
                        if (sectorEndDeg >= 360f)
                        {
                            // Last sector wraps around - angle is in [sectorStartDeg, 360) OR [0, sectorEndDeg-360)
                            // Since Unity doesn't support OR, we check the main range
                            transition.AddCondition(AnimatorConditionMode.Greater, sectorStartDeg, data.angleParameterName);
                        }
                        else
                        {
                            transition.AddCondition(AnimatorConditionMode.Greater, sectorStartDeg, data.angleParameterName);
                            transition.AddCondition(AnimatorConditionMode.Less, sectorEndDeg, data.angleParameterName);
                        }
                        
                        // Set rotation step output on the target state
                        if (rotationStep != 0)
                        {
                            SetStateDriver(sectorStates[j], data, null,
                                (data.rotationStepParameterName, DriverSet, (float)rotationStep));
                        }
                        else
                        {
                            SetStateDriver(sectorStates[j], data, null,
                                (data.rotationStepParameterName, DriverSet, 0f));
                        }
                    }
                }
            }
            
            return sectorStates;
        }

        private Dictionary<string, AnimatorState> GenerateFlickDetection(RotationCounterData data, AnimatorStateMachine stateMachine)
        {
            var flickStates = new Dictionary<string, AnimatorState>();
            
            // Create IDLE state
            var idleState = stateMachine.AddState("Flick_Idle", new Vector3(0, 400, 0));
            idleState.writeDefaultValues = false;
            SetStateDriver(idleState, data, null,
                ("_FlickState", DriverSet, 0f),
                ("_FlickDir", DriverSet, 0f),
                ("_FlickTimer", DriverSet, 0f),
                (data.flickEventParameterName, DriverSet, 0f));
            flickStates["IDLE"] = idleState;
            
            // Create ACTIVE states for each cardinal direction
            string[] directions = { "RIGHT", "UP", "LEFT", "DOWN" };
            float[] directionAngles = { 0f, 90f, 180f, 270f };
            int[] directionValues = { 1, 2, 3, 4 };
            
            for (int i = 0; i < directions.Length; i++)
            {
                var activeState = stateMachine.AddState($"Flick_Active_{directions[i]}", new Vector3((i - 1.5f) * 200f, 600, 0));
                activeState.writeDefaultValues = false;
                flickStates[$"ACTIVE_{directions[i]}"] = activeState;
            }
            
            // IDLE -> ACTIVE transitions (for each direction)
            for (int i = 0; i < directions.Length; i++)
            {
                float dirAngle = directionAngles[i];
                float angleMin = NormalizeAngle(dirAngle - data.angleToleranceDeg);
                float angleMax = NormalizeAngle(dirAngle + data.angleToleranceDeg);
                bool isWraparound = angleMin > angleMax;
                
                // Create transitions to handle angle range (may need two for wraparound)
                if (isWraparound)
                {
                    // Wraparound case: create two transitions for [angleMin, 360) and [0, angleMax]
                    // First transition: angle >= angleMin
                    if (i == 0) // RIGHT
                    {
                        var toActive1 = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive1);
                        toActive1.AddCondition(AnimatorConditionMode.Greater, data.flickMinRadius, data.xParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Greater, data.innerDeadzone, data.xParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        
                        var toActive2 = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive2);
                        toActive2.AddCondition(AnimatorConditionMode.Greater, data.flickMinRadius, data.xParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Greater, data.innerDeadzone, data.xParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                    else if (i == 1) // UP
                    {
                        var toActive1 = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive1);
                        toActive1.AddCondition(AnimatorConditionMode.Greater, data.flickMinRadius, data.yParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Greater, data.innerDeadzone, data.yParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        
                        var toActive2 = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive2);
                        toActive2.AddCondition(AnimatorConditionMode.Greater, data.flickMinRadius, data.yParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Greater, data.innerDeadzone, data.yParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                    else if (i == 2) // LEFT
                    {
                        var toActive1 = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive1);
                        toActive1.AddCondition(AnimatorConditionMode.Less, 0f, data.xParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Less, -data.flickMinRadius, data.xParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Less, -data.innerDeadzone, data.xParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        
                        var toActive2 = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive2);
                        toActive2.AddCondition(AnimatorConditionMode.Less, 0f, data.xParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Less, -data.flickMinRadius, data.xParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Less, -data.innerDeadzone, data.xParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                    else // DOWN
                    {
                        var toActive1 = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive1);
                        toActive1.AddCondition(AnimatorConditionMode.Less, 0f, data.yParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Less, -data.flickMinRadius, data.yParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Less, -data.innerDeadzone, data.yParameterName);
                        toActive1.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        
                        var toActive2 = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive2);
                        toActive2.AddCondition(AnimatorConditionMode.Less, 0f, data.yParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Less, -data.flickMinRadius, data.yParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Less, -data.innerDeadzone, data.yParameterName);
                        toActive2.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                }
                else
                {
                    // Normal case: single transition
                    if (i == 0) // RIGHT
                    {
                        var toActive = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive);
                        toActive.AddCondition(AnimatorConditionMode.Greater, data.flickMinRadius, data.xParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, data.innerDeadzone, data.xParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                    else if (i == 1) // UP
                    {
                        var toActive = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive);
                        toActive.AddCondition(AnimatorConditionMode.Greater, data.flickMinRadius, data.yParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, data.innerDeadzone, data.yParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                    else if (i == 2) // LEFT
                    {
                        var toActive = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive);
                        toActive.AddCondition(AnimatorConditionMode.Less, 0f, data.xParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, -data.flickMinRadius, data.xParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, -data.innerDeadzone, data.xParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                    else // DOWN
                    {
                        var toActive = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive);
                        toActive.AddCondition(AnimatorConditionMode.Less, 0f, data.yParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, -data.flickMinRadius, data.yParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, -data.innerDeadzone, data.yParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                }
                
                // Set ACTIVE state variables
                SetStateDriver(flickStates[$"ACTIVE_{directions[i]}"], data, null,
                    ("_FlickState", DriverSet, 1f),
                    ("_FlickDir", DriverSet, (float)directionValues[i]),
                    ("_FlickTimer", DriverSet, 0f));
            }
            
            // ACTIVE -> IDLE transitions (cancel conditions and success)
            for (int i = 0; i < directions.Length; i++)
            {
                var activeState = flickStates[$"ACTIVE_{directions[i]}"];
                float dirAngle = directionAngles[i];
                float angleMin = NormalizeAngle(dirAngle - data.angleToleranceDeg);
                float angleMax = NormalizeAngle(dirAngle + data.angleToleranceDeg);
                
                // Cancel: Rotation detected
                var cancelRotation = activeState.AddTransition(idleState);
                ConfigureInstantTransition(cancelRotation);
                cancelRotation.AddCondition(AnimatorConditionMode.NotEqual, 0f, data.rotationStepParameterName);
                SetStateDriver(idleState, data, null,
                    ("_FlickState", DriverSet, 0f),
                    ("_FlickDir", DriverSet, 0f),
                    ("_FlickTimer", DriverSet, 0f),
                    (data.flickEventParameterName, DriverSet, 0f));
                
                // Cancel: Angle drift (angle outside tolerance)
                // Create separate transitions for angle < min and angle > max
                var cancelDriftLow = activeState.AddTransition(idleState);
                ConfigureInstantTransition(cancelDriftLow);
                if (angleMin > angleMax) // Wraparound case
                {
                    // For wraparound, angle is outside if it's between angleMax and angleMin
                    cancelDriftLow.AddCondition(AnimatorConditionMode.Greater, angleMax, data.angleParameterName);
                    cancelDriftLow.AddCondition(AnimatorConditionMode.Less, angleMin, data.angleParameterName);
                }
                else
                {
                    cancelDriftLow.AddCondition(AnimatorConditionMode.Less, angleMin, data.angleParameterName);
                }
                SetStateDriver(idleState, data, null,
                    ("_FlickState", DriverSet, 0f),
                    ("_FlickDir", DriverSet, 0f),
                    ("_FlickTimer", DriverSet, 0f),
                    (data.flickEventParameterName, DriverSet, 0f));
                
                if (!(angleMin > angleMax)) // Only add high cancel if not wraparound
                {
                    var cancelDriftHigh = activeState.AddTransition(idleState);
                    ConfigureInstantTransition(cancelDriftHigh);
                    cancelDriftHigh.AddCondition(AnimatorConditionMode.Greater, angleMax, data.angleParameterName);
                    SetStateDriver(idleState, data, null,
                        ("_FlickState", DriverSet, 0f),
                        ("_FlickDir", DriverSet, 0f),
                        ("_FlickTimer", DriverSet, 0f),
                        (data.flickEventParameterName, DriverSet, 0f));
                }
                
                // Success: Release detected (radius <= releaseRadius)
                // Release means both |X| and |Y| are small
                var success = activeState.AddTransition(idleState);
                ConfigureInstantTransition(success);
                // Check both X and Y are within release radius (positive and negative)
                success.AddCondition(AnimatorConditionMode.Less, data.releaseRadius, data.xParameterName);
                success.AddCondition(AnimatorConditionMode.Greater, -data.releaseRadius, data.xParameterName);
                success.AddCondition(AnimatorConditionMode.Less, data.releaseRadius, data.yParameterName);
                success.AddCondition(AnimatorConditionMode.Greater, -data.releaseRadius, data.yParameterName);
                
                // Emit flick event
                SetStateDriver(idleState, data, null,
                    ("_FlickState", DriverSet, 0f),
                    ("_FlickDir", DriverSet, 0f),
                    ("_FlickTimer", DriverSet, 0f),
                    (data.flickEventParameterName, DriverSet, (float)directionValues[i]));
            }
            
            return flickStates;
        }



        private float NormalizeAngle(float angle)
        {
            angle = angle % 360f;
            if (angle < 0f) angle += 360f;
            return angle;
        }

        private void SetStateDriver(AnimatorState state, RotationCounterData data, float? debugPhase, params (string name, int type, float value)[] entries)
        {
            var driverEntries = new List<(string, int, float, float)>();

            foreach (var (name, type, value) in entries)
            {
                if (string.IsNullOrEmpty(name)) continue;
                driverEntries.Add((name, type, value, 0f));
            }

            if (debugPhase.HasValue && data.createDebugPhaseParameter && !string.IsNullOrEmpty(data.debugPhaseParameterName))
            {
                driverEntries.Add((data.debugPhaseParameterName, DriverSet, debugPhase.Value, 0f));
            }

            if (driverEntries.Count > 0)
            {
                AddParameterDriver(state, driverEntries.ToArray());
            }
        }

        private Vector3 GetSectorStatePosition(int sector, int type)
        {
            // type: 0=Idle, 1=Inc, -1=Dec
            int column = sector % 6;
            int row = sector / 6;
            
            float baseX = 250f + column * 250f;
            float baseY = 0f + row * 300f;

            if (type == 0) return new Vector3(baseX, baseY, 0);
            if (type == 1) return new Vector3(baseX + 80f, baseY + 60f, 0); // Inc to right/down
            if (type == -1) return new Vector3(baseX - 80f, baseY + 60f, 0); // Dec to left/down
            
            return Vector3.zero;
        }


        private void ConfigureInstantTransition(AnimatorStateTransition transition)
        {
            transition.duration = 0f;
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.exitTime = 0f;
            transition.canTransitionToSelf = false;
        }

        private void ClearStateMachine(AnimatorStateMachine stateMachine)
        {
            foreach (var childState in stateMachine.states.ToArray())
            {
                stateMachine.RemoveState(childState.state);
            }

            foreach (var childMachine in stateMachine.stateMachines.ToArray())
            {
                stateMachine.RemoveStateMachine(childMachine.stateMachine);
            }

            foreach (var transition in stateMachine.anyStateTransitions.ToArray())
            {
                stateMachine.RemoveAnyStateTransition(transition);
            }
        }

        /// <summary>
        /// Add VRCParameterDriver to an animator state
        /// </summary>
        private void AddParameterDriver(AnimatorState state, (string name, int type, float value, float min)[] parameters)
        {
            try
            {
                var vrcDriverType = System.Type.GetType("VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver, VRCSDK3A");
                if (vrcDriverType == null)
                {
                    Debug.LogWarning("[RotationCounterProcessor] VRCParameterDriver type not found. VRCSDK3A may not be available.");
                    return;
                }

                var driver = state.AddStateMachineBehaviour(vrcDriverType) as StateMachineBehaviour;
                if (driver == null)
                {
                    Debug.LogWarning("[RotationCounterProcessor] Could not add VRCParameterDriver to state");
                    return;
                }

                var parametersField = vrcDriverType.GetField("parameters");
                if (parametersField == null)
                {
                    Debug.LogWarning("[RotationCounterProcessor] VRCParameterDriver parameters field not found");
                    return;
                }

                var parameterListType = parametersField.FieldType;
                var parameterList = System.Activator.CreateInstance(parameterListType) as System.Collections.IList;
                var parameterType = parameterListType.GetGenericArguments()[0];

                foreach (var (name, type, value, min) in parameters)
                {
                    var parameter = System.Activator.CreateInstance(parameterType);
                    parameterType.GetField("type").SetValue(parameter, type); // 0=Set, 1=Add
                    parameterType.GetField("name").SetValue(parameter, name);
                    parameterType.GetField("value").SetValue(parameter, value);
                    parameterList.Add(parameter);
                }

                parametersField.SetValue(driver, parameterList);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RotationCounterProcessor] Could not add VRCParameterDriver: {ex.Message}");
            }
        }

        private string EnsureUnityPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.Replace("\\", "/");
        }
    }
}

