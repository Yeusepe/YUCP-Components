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
            
            AnimatorController controller = GenerateInMemoryController(data);
            
            if (isFullController)
            {
                var contentField = existingVRCFury.GetType().GetField("content", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var content = contentField.GetValue(existingVRCFury);
                
                var controllersField = content.GetType().GetField("controllers", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (controllersField == null)
                {
                    Debug.LogError($"[RotationCounterProcessor] Could not find controllers field on FullController", data);
                    return;
                }
                
                var controllerEntryType = controllersField.FieldType.GetGenericArguments()[0];
                var entry = System.Activator.CreateInstance(controllerEntryType);
                controllerEntryType.GetField("controller").SetValue(entry, controller);
                controllerEntryType.GetField("type").SetValue(entry, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);
                
                var controllersList = controllersField.GetValue(content) as System.Collections.IList;
                controllersList.Add(entry);
                
                var globalParamsField = content.GetType().GetField("globalParams", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (globalParamsField != null)
                {
                    var globalParamsList = globalParamsField.GetValue(content) as System.Collections.IList;
                    if (globalParamsList != null)
                    {
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
                var fullController = FuryComponents.CreateFullController(data.gameObject);
                
                fullController.AddController(controller, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);
                
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

            controller.AddParameter("_PrevSector", AnimatorControllerParameterType.Int);
            controller.AddParameter("_CurrentSector", AnimatorControllerParameterType.Int);
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

            var sectorStates = GenerateRotationDetection(data, stateMachine);
            
            var flickStates = GenerateFlickDetection(data, stateMachine);

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
            
            for (int i = 0; i < numSectors; i++)
            {
                float sectorStartDeg = i * sectorWidthDeg;
                float sectorEndDeg = (i + 1) * sectorWidthDeg;
                
                var pos = GetSectorStatePosition(i, 0);
                var state = stateMachine.AddState($"Sector_{i}", pos);
                state.writeDefaultValues = false;
                
                sectorStates[i] = state;
            }
            
            for (int i = 0; i < numSectors; i++)
            {
                SetStateDriver(sectorStates[i], data, null,
                    (data.rotationStepParameterName, DriverSet, 0f),
                    ("_CurrentSector", DriverSet, (float)i),
                    ("_PrevSector", DriverSet, (float)i));
            }
            
            for (int i = 0; i < numSectors; i++)
            {
                for (int j = 0; j < numSectors; j++)
                {
                    if (i == j) continue;
                    
                    int rawStep = j - i;
                    int correctedStep = rawStep;
                    
                    if (rawStep > halfSectors)
                    {
                        correctedStep = rawStep - numSectors;
                    }
                    else if (rawStep < -halfSectors)
                    {
                        correctedStep = rawStep + numSectors;
                    }
                    
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
                        
                        float sectorStartDeg = j * sectorWidthDeg;
                        float sectorEndDeg = (j + 1) * sectorWidthDeg;
                        
                        if (sectorEndDeg >= 360f)
                        {
                            transition.AddCondition(AnimatorConditionMode.Greater, sectorStartDeg, data.angleParameterName);
                        }
                        else
                        {
                            transition.AddCondition(AnimatorConditionMode.Greater, sectorStartDeg, data.angleParameterName);
                            transition.AddCondition(AnimatorConditionMode.Less, sectorEndDeg, data.angleParameterName);
                        }
                    }
                }
            }
            
            foreach (int i in Enumerable.Range(0, numSectors))
            {
                for (int j = 0; j < numSectors; j++)
                {
                    if (i == j) continue;
                    
                    int rawStep = j - i;
                    int correctedStep = rawStep;
                    
                    if (rawStep > halfSectors)
                    {
                        correctedStep = rawStep - numSectors;
                    }
                    else if (rawStep < -halfSectors)
                    {
                        correctedStep = rawStep + numSectors;
                    }
                    
                    int rotationStep = 0;
                    if (correctedStep == 1 || correctedStep == -numSectors + 1)
                    {
                        rotationStep = 1;
                    }
                    else if (correctedStep == -1 || correctedStep == numSectors - 1)
                    {
                        rotationStep = -1;
                    }
                    
                    if (rotationStep != 0)
                    {
                        SetStateDriver(sectorStates[j], data, null,
                            ("_PrevSector", DriverSet, (float)i),
                            ("_CurrentSector", DriverSet, (float)j),
                            (data.rotationStepParameterName, DriverSet, (float)rotationStep));
                    }
                }
            }
            
            return sectorStates;
        }

        private Dictionary<string, AnimatorState> GenerateFlickDetection(RotationCounterData data, AnimatorStateMachine stateMachine)
        {
            var flickStates = new Dictionary<string, AnimatorState>();
            
            var idleState = stateMachine.AddState("Flick_Idle", new Vector3(0, 400, 0));
            idleState.writeDefaultValues = false;
            SetStateDriver(idleState, data, null,
                ("_FlickState", DriverSet, 0f),
                ("_FlickDir", DriverSet, 0f),
                ("_FlickTimer", DriverSet, 0f),
                (data.flickEventParameterName, DriverSet, 0f));
            flickStates["IDLE"] = idleState;
            
            string[] directions = { "RIGHT", "UP", "LEFT", "DOWN" };
            float[] directionAngles = { 0f, 90f, 180f, 270f };
            int[] directionValues = { 1, 2, 3, 4 };
            
            for (int i = 0; i < directions.Length; i++)
            {
                var activeState = stateMachine.AddState($"Flick_Active_{directions[i]}", new Vector3((i - 1.5f) * 200f, 600, 0));
                activeState.writeDefaultValues = false;
                flickStates[$"ACTIVE_{directions[i]}"] = activeState;
            }
            
            for (int i = 0; i < directions.Length; i++)
            {
                float dirAngle = directionAngles[i];
                float angleMin = NormalizeAngle(dirAngle - data.angleToleranceDeg);
                float angleMax = NormalizeAngle(dirAngle + data.angleToleranceDeg);
                bool isWraparound = angleMin > angleMax;
                
                if (isWraparound)
                {
                    if (i == 0)
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
                    else if (i == 1)
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
                    else if (i == 2)
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
                    if (i == 0)
                    {
                        var toActive = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive);
                        toActive.AddCondition(AnimatorConditionMode.Greater, data.flickMinRadius, data.xParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, data.innerDeadzone, data.xParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                    else if (i == 1)
                    {
                        var toActive = idleState.AddTransition(flickStates[$"ACTIVE_{directions[i]}"]);
                        ConfigureInstantTransition(toActive);
                        toActive.AddCondition(AnimatorConditionMode.Greater, data.flickMinRadius, data.yParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, data.innerDeadzone, data.yParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Greater, angleMin, data.angleParameterName);
                        toActive.AddCondition(AnimatorConditionMode.Less, angleMax, data.angleParameterName);
                    }
                    else if (i == 2)
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
                
                SetStateDriver(flickStates[$"ACTIVE_{directions[i]}"], data, null,
                    ("_FlickState", DriverSet, 1f),
                    ("_FlickDir", DriverSet, (float)directionValues[i]),
                    ("_FlickTimer", DriverSet, 0f));
            }
            
            for (int i = 0; i < directions.Length; i++)
            {
                var activeState = flickStates[$"ACTIVE_{directions[i]}"];
                float dirAngle = directionAngles[i];
                float angleMin = NormalizeAngle(dirAngle - data.angleToleranceDeg);
                float angleMax = NormalizeAngle(dirAngle + data.angleToleranceDeg);
                
                var cancelRotation = activeState.AddTransition(idleState);
                ConfigureInstantTransition(cancelRotation);
                cancelRotation.AddCondition(AnimatorConditionMode.NotEqual, 0f, data.rotationStepParameterName);
                SetStateDriver(idleState, data, null,
                    ("_FlickState", DriverSet, 0f),
                    ("_FlickDir", DriverSet, 0f),
                    ("_FlickTimer", DriverSet, 0f),
                    (data.flickEventParameterName, DriverSet, 0f));
                
                var cancelDriftLow = activeState.AddTransition(idleState);
                ConfigureInstantTransition(cancelDriftLow);
                if (angleMin > angleMax)
                {
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
                
                var success = activeState.AddTransition(idleState);
                ConfigureInstantTransition(success);
                success.AddCondition(AnimatorConditionMode.Less, data.releaseRadius, data.xParameterName);
                success.AddCondition(AnimatorConditionMode.Greater, -data.releaseRadius, data.xParameterName);
                success.AddCondition(AnimatorConditionMode.Less, data.releaseRadius, data.yParameterName);
                success.AddCondition(AnimatorConditionMode.Greater, -data.releaseRadius, data.yParameterName);
                
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
        /// Add VRCParameterDriver to an animator state, or merge parameters into existing driver
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

                StateMachineBehaviour driver = null;
                
                foreach (var behaviour in state.behaviours)
                {
                    if (behaviour != null && behaviour.GetType() == vrcDriverType)
                    {
                        driver = behaviour;
                        break;
                    }
                }

                if (driver == null)
                {
                    driver = state.AddStateMachineBehaviour(vrcDriverType) as StateMachineBehaviour;
                    if (driver == null)
                    {
                        Debug.LogWarning("[RotationCounterProcessor] Could not add VRCParameterDriver to state");
                        return;
                    }
                }

                var parametersField = vrcDriverType.GetField("parameters");
                if (parametersField == null)
                {
                    Debug.LogWarning("[RotationCounterProcessor] VRCParameterDriver parameters field not found");
                    return;
                }

                var parameterListType = parametersField.FieldType;
                System.Collections.IList parameterList;
                var parameterType = parameterListType.GetGenericArguments()[0];
                
                var existingParams = parametersField.GetValue(driver);
                if (existingParams != null && existingParams is System.Collections.IList existingList && existingList.Count > 0)
                {
                    parameterList = existingList;
                    
                    var nameField = parameterType.GetField("name");
                    
                    var toRemove = new List<object>();
                    foreach (var param in parameterList)
                    {
                        if (param != null)
                        {
                            var existingName = nameField?.GetValue(param) as string;
                            foreach (var (name, _, _, _) in parameters)
                            {
                                if (existingName == name)
                                {
                                    toRemove.Add(param);
                                    break;
                                }
                            }
                        }
                    }
                    
                    foreach (var param in toRemove)
                    {
                        parameterList.Remove(param);
                    }
                }
                else
                {
                    parameterList = System.Activator.CreateInstance(parameterListType) as System.Collections.IList;
                }

                foreach (var (name, type, value, min) in parameters)
                {
                    var parameter = System.Activator.CreateInstance(parameterType);
                    parameterType.GetField("type").SetValue(parameter, type);
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


