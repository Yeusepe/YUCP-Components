using System.Collections.Generic;
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
    /// Generates Animator Controller with zone states to detect wraparounds and increment RotationIndex.
    /// </summary>
    public class RotationCounterProcessor : IVRCSDKPreprocessAvatarCallback
    {
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

            if (string.IsNullOrEmpty(data.angleParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Angle parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (string.IsNullOrEmpty(data.rotationIndexParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Rotation index parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (data.numberOfZones < 4 || data.numberOfZones > 32)
            {
                Debug.LogError($"[RotationCounterProcessor] Number of zones must be between 4 and 32 for '{data.name}'", data);
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
                        bool hasAngle = false;
                        bool hasRotationIndex = false;
                        bool hasTestZone = false;
                        bool hasFwdArmed = false;
                        bool hasRevArmed = false;
                        foreach (string param in globalParamsList)
                        {
                            if (param == data.angleParameterName) hasAngle = true;
                            if (param == data.rotationIndexParameterName) hasRotationIndex = true;
                            if (param == "RotationCounter_TestZone") hasTestZone = true;
                            if (param == "RotationCounter_FwdArmed") hasFwdArmed = true;
                            if (param == "RotationCounter_RevArmed") hasRevArmed = true;
                        }
                        
                        if (!hasAngle) globalParamsList.Add(data.angleParameterName);
                        if (!hasRotationIndex) globalParamsList.Add(data.rotationIndexParameterName);
                        if (!hasTestZone) globalParamsList.Add("RotationCounter_TestZone");
                        if (!hasFwdArmed) globalParamsList.Add("RotationCounter_FwdArmed");
                        if (!hasRevArmed) globalParamsList.Add("RotationCounter_RevArmed");
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
                fullController.AddGlobalParam(data.angleParameterName);
                fullController.AddGlobalParam(data.rotationIndexParameterName);
                fullController.AddGlobalParam("RotationCounter_TestZone"); // Test parameter for debugging
                fullController.AddGlobalParam("RotationCounter_FwdArmed");
                fullController.AddGlobalParam("RotationCounter_RevArmed");
                
                Debug.Log($"[RotationCounterProcessor] Created new VRCFury FullController and added rotation counter controller with global parameters", data);
            }

            // Mark as generated
            data.controllerGenerated = true;
            data.generatedZonesCount = data.numberOfZones;

            Debug.Log($"[RotationCounterProcessor] Generated rotation counter controller with {data.numberOfZones} zones and integrated via VRCFury", data);
        }

        private AnimatorController GenerateInMemoryController(RotationCounterData data)
        {
            // Create controller using Unity's API (creates in memory, will be serialized by VRCFury)
            // Use a temporary path that won't be saved - VRCFury will handle the actual integration
            string tempPath = "Assets/Temp_RotationCounter.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(tempPath);
            
            // Add required parameters
            controller.AddParameter(data.angleParameterName, AnimatorControllerParameterType.Float);
            controller.AddParameter(data.rotationIndexParameterName, AnimatorControllerParameterType.Int);
            
            // Detector parameters
            controller.AddParameter("RotationCounter_FwdArmed", AnimatorControllerParameterType.Bool);
            controller.AddParameter("RotationCounter_RevArmed", AnimatorControllerParameterType.Bool);
            
            // Add debug test parameter to verify zone transitions
            string testZoneParameterName = "RotationCounter_TestZone";
            controller.AddParameter(testZoneParameterName, AnimatorControllerParameterType.Int);
            
            // Add test parameters to global params so they're accessible
            // (We'll add them via FullController later)

            // Generate zone states and transitions
            GenerateRotationController(controller.layers[0].stateMachine, controller, data);

            // The controller file exists temporarily but VRCFury will integrate it
            // We could delete it after adding to VRCFury, but it's safer to leave it for debugging
            // VRCFury will handle the actual build integration

            return controller;
        }

        private void GenerateRotationController(AnimatorStateMachine stateMachine, AnimatorController controller, RotationCounterData data)
        {
            
            int numZones = data.numberOfZones;
            float zoneSize = 1f / numZones;

            // Create zone states
            var zoneStates = new List<AnimatorState>();
            for (int i = 0; i < numZones; i++)
            {
                float zoneStart = i * zoneSize;
                float zoneEnd = (i + 1) * zoneSize;

                var zoneState = stateMachine.AddState($"Zone{i}", new Vector3(300 + i % 8 * 150, (i / 8) * 100, 0));
                zoneState.motion = null;
                zoneState.writeDefaultValues = false;
                zoneStates.Add(zoneState);
                
                // Add VRCParameterDriver to set test zone parameter for debugging
                AddParameterDriver(zoneState, new (string, int, float, float)[] {
                    ("RotationCounter_TestZone", 0, (float)i, 0f) // Set test zone to zone index
                });

                // Add transition from Any State to this zone
                var toZone = stateMachine.AddAnyStateTransition(zoneState);
                toZone.duration = 0f;
                toZone.hasExitTime = false;
                toZone.canTransitionToSelf = true;

                // Conditions: angle must be in this zone's range
                // BUT: For first and last zones, exclude wraparound ranges to avoid conflicts
                float epsilon = 0.001f;
                float zoneStartThreshold = zoneStart > epsilon ? zoneStart - epsilon : -0.001f;
                
                if (i == 0)
                {
                    // First zone: 0.0 to zoneSize, BUT exclude near-zero wraparound detection range
                    // If angle is < nearZeroThreshold, we want wraparound detection, not direct zone entry
                    // So only enter Zone0 if angle is between nearZeroThreshold and zoneEnd
                    float minAngle = data.nearZeroThreshold;
                    toZone.AddCondition(AnimatorConditionMode.Greater, minAngle + epsilon, data.angleParameterName);
                    toZone.AddCondition(AnimatorConditionMode.Less, zoneEnd, data.angleParameterName);
                }
                else if (i == numZones - 1)
                {
                    // Last zone: zoneStart to nearMaxThreshold (exclude wraparound range)
                    // If angle is > nearMaxThreshold, we're wrapping, let wraparound handle it
                    float maxAngle = data.nearMaxThreshold;
                    toZone.AddCondition(AnimatorConditionMode.Greater, zoneStartThreshold, data.angleParameterName);
                    toZone.AddCondition(AnimatorConditionMode.Less, maxAngle - epsilon, data.angleParameterName);
                }
                else
                {
                    // Middle zones: standard range
                    toZone.AddCondition(AnimatorConditionMode.Greater, zoneStartThreshold, data.angleParameterName);
                    toZone.AddCondition(AnimatorConditionMode.Less, zoneEnd, data.angleParameterName);
                }
            }

            // Set default state to zone 0
            stateMachine.defaultState = zoneStates[0];

            // Create wraparound detection states
            var incrementState = stateMachine.AddState("IncrementRotation", new Vector3(100, 200, 0));
            incrementState.motion = null;
            incrementState.writeDefaultValues = false;
            AddParameterDriver(incrementState, new (string, int, float, float)[] {
                (data.rotationIndexParameterName, 1, -1f, 0f), // Add -1 to RotationIndex (flip direction)
                ("RotationCounter_TestZone", 0, 999f, 0f) // Set test zone to 999 to verify increment triggered
            });

            var decrementState = stateMachine.AddState("DecrementRotation", new Vector3(100, 300, 0));
            decrementState.motion = null;
            decrementState.writeDefaultValues = false;
            AddParameterDriver(decrementState, new (string, int, float, float)[] {
                (data.rotationIndexParameterName, 1, 1f, 0f), // Add +1 to RotationIndex (flip direction)
                ("RotationCounter_TestZone", 0, 888f, 0f) // Set test zone to 888 to verify decrement triggered
            });

            // Create transitions for wraparound detection
            int lastZoneIndex = numZones - 1;
            int firstZoneIndex = 0;
            
            // Calculate last zone start once for use in multiple places
            var lastZoneStart = lastZoneIndex * zoneSize;
            
            // Add transitions between adjacent zones (normal rotation, no wraparound)
            // These handle all normal zone-to-zone transitions
            for (int i = 0; i < numZones; i++)
            {
                int nextZone = (i + 1) % numZones;
                int prevZone = (i - 1 + numZones) % numZones;

                // Forward rotation: i → nextZone
                var forwardTransition = zoneStates[i].AddTransition(zoneStates[nextZone]);
                forwardTransition.duration = 0f;
                forwardTransition.hasExitTime = false;
                forwardTransition.canTransitionToSelf = false;
                
                // Skip last zone → first zone transition - wraparound detection handles it
                if (i == lastZoneIndex)
                {
                    // Don't create direct last → first transition
                    // Wraparound detection will handle this case
                    continue;
                }
                else
                {
                    // Normal forward transitions
                    float nextZoneStart = nextZone * zoneSize;
                    float epsilon = 0.001f;
                    float nextZoneThreshold = nextZoneStart > epsilon ? nextZoneStart - epsilon : -0.001f;
                    forwardTransition.AddCondition(AnimatorConditionMode.Greater, nextZoneThreshold, data.angleParameterName);
                }

                // Reverse rotation: i → prevZone
                var reverseTransition = zoneStates[i].AddTransition(zoneStates[prevZone]);
                reverseTransition.duration = 0f;
                reverseTransition.hasExitTime = false;
                reverseTransition.canTransitionToSelf = false;
                
                // Skip first zone → last zone transition - wraparound detection handles it
                if (i == firstZoneIndex)
                {
                    // Don't create direct first → last transition  
                    // Wraparound detection will handle this case
                    continue;
                }
                else
                {
                    // Normal reverse transitions
                    float prevZoneStart = prevZone * zoneSize;
                    reverseTransition.AddCondition(AnimatorConditionMode.Less, prevZoneStart, data.angleParameterName);
                }
            }
            
            // Forward wraparound: Last zone → Increment → First zone
            // When angle crosses from high (last zone) to low (< nearZeroThreshold), increment counter
            var lastToIncrement = zoneStates[lastZoneIndex].AddTransition(incrementState);
            lastToIncrement.duration = 0f;
            lastToIncrement.hasExitTime = false;
            lastToIncrement.canTransitionToSelf = false;
            // Transition fires when angle wraps to low range (< nearZeroThreshold)
            // This means we've completed a full rotation forward
            // We're already in last zone state, so we just need to check if angle is in wraparound range
            lastToIncrement.AddCondition(AnimatorConditionMode.Less, data.nearZeroThreshold, data.angleParameterName);
            
            // Return from increment to first zone
            var incrementToFirst = incrementState.AddTransition(zoneStates[firstZoneIndex]);
            incrementToFirst.duration = 0f;
            incrementToFirst.hasExitTime = false;
            incrementToFirst.canTransitionToSelf = false;
            // Transition to first zone when angle is in first zone range (after wraparound)
            incrementToFirst.AddCondition(AnimatorConditionMode.Greater, data.nearZeroThreshold - 0.001f, data.angleParameterName);
            incrementToFirst.AddCondition(AnimatorConditionMode.Less, zoneSize, data.angleParameterName);
            
            // Direct last → first transition for normal (non-wraparound) forward rotation
            // Only fires when angle is in normal first zone range (> nearZeroThreshold)
            var lastToFirstNormal = zoneStates[lastZoneIndex].AddTransition(zoneStates[firstZoneIndex]);
            lastToFirstNormal.duration = 0f;
            lastToFirstNormal.hasExitTime = false;
            lastToFirstNormal.canTransitionToSelf = false;
            // Normal last → first: angle is in normal first zone range (not wraparound)
            // This handles the case where angle goes from high in last zone to high in first zone
            lastToFirstNormal.AddCondition(AnimatorConditionMode.Greater, data.nearZeroThreshold, data.angleParameterName);
            lastToFirstNormal.AddCondition(AnimatorConditionMode.Less, zoneSize, data.angleParameterName);
            
            // Reverse wraparound: First zone → Decrement → Last zone
            // When angle crosses from low (first zone) to high (> nearMaxThreshold), decrement counter
            var firstToDecrement = zoneStates[firstZoneIndex].AddTransition(decrementState);
            firstToDecrement.duration = 0f;
            firstToDecrement.hasExitTime = false;
            firstToDecrement.canTransitionToSelf = false;
            // Transition fires when angle wraps to high range (> nearMaxThreshold)
            // This means we've completed a full rotation backward
            // We're already in first zone state, so we just need to check if angle is in wraparound range
            firstToDecrement.AddCondition(AnimatorConditionMode.Greater, data.nearMaxThreshold, data.angleParameterName);
            
            // Return from decrement to last zone
            var decrementToLast = decrementState.AddTransition(zoneStates[lastZoneIndex]);
            decrementToLast.duration = 0f;
            decrementToLast.hasExitTime = false;
            decrementToLast.canTransitionToSelf = false;
            // Transition to last zone when angle is in last zone range (after wraparound)
            decrementToLast.AddCondition(AnimatorConditionMode.Greater, lastZoneStart - 0.001f, data.angleParameterName);
            decrementToLast.AddCondition(AnimatorConditionMode.Less, data.nearMaxThreshold + 0.001f, data.angleParameterName);
            
            // Direct first → last transition for normal (non-wraparound) reverse rotation
            // Only fires when angle is in normal last zone range (< nearMaxThreshold)
            var firstToLastNormal = zoneStates[firstZoneIndex].AddTransition(zoneStates[lastZoneIndex]);
            firstToLastNormal.duration = 0f;
            firstToLastNormal.hasExitTime = false;
            firstToLastNormal.canTransitionToSelf = false;
            // Normal first → last: angle is in normal last zone range (not wraparound)
            // This handles the case where angle goes from low in first zone to low in last zone
            firstToLastNormal.AddCondition(AnimatorConditionMode.Greater, lastZoneStart, data.angleParameterName);
            firstToLastNormal.AddCondition(AnimatorConditionMode.Less, data.nearMaxThreshold, data.angleParameterName);

            // ---------------- Detector layer (arming/trigger with hysteresis) ----------------
            var detectorLayer = new AnimatorControllerLayer
            {
                name = "RotationDetector",
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine()
            };
            var layers = new List<AnimatorControllerLayer>(controller.layers) { detectorLayer };
            controller.layers = layers.ToArray();

            var detSM = detectorLayer.stateMachine;
            float eps = Mathf.Clamp01(data.hysteresisEpsilon);
            float nearZeroArm = Mathf.Max(0f, data.nearZeroThreshold - eps);
            float nearZeroDisarm = Mathf.Min(1f, data.nearZeroThreshold + eps);
            float nearMaxArm = Mathf.Min(1f, data.nearMaxThreshold + eps);
            float nearMaxDisarm = Mathf.Max(0f, data.nearMaxThreshold - eps);

            var idle = detSM.AddState("Detector_Idle", new Vector3(50, 50, 0));
            idle.writeDefaultValues = false;
            detSM.defaultState = idle;

            var fwdArmed = detSM.AddState("Detector_FwdArmed", new Vector3(250, 0, 0));
            fwdArmed.writeDefaultValues = false;
            AddParameterDriver(fwdArmed, new (string,int,float,float)[] {
                ("RotationCounter_FwdArmed", 0, 1f, 0f)
            });

            var fwdTrigger = detSM.AddState("Detector_FwdTrigger", new Vector3(450, 0, 0));
            fwdTrigger.writeDefaultValues = false;
            // Clockwise increases per user; add mapping based on clockwiseIsPositive in processor later? Not accessible here; use two states with both adds? We'll use +1 for CW and -1 for CCW by branching via two drivers
            float fwdAdd = data.clockwiseIsPositive ? 1f : -1f;
            AddParameterDriver(fwdTrigger, new (string,int,float,float)[] {
                (data.rotationIndexParameterName, 1, fwdAdd, 0f),
                ("RotationCounter_FwdArmed", 0, 0f, 0f),
                ("RotationCounter_TestZone", 0, 999f, 0f)
            });

            var revArmed = detSM.AddState("Detector_RevArmed", new Vector3(250, 150, 0));
            revArmed.writeDefaultValues = false;
            AddParameterDriver(revArmed, new (string,int,float,float)[] {
                ("RotationCounter_RevArmed", 0, 1f, 0f)
            });

            var revTrigger = detSM.AddState("Detector_RevTrigger", new Vector3(450, 150, 0));
            revTrigger.writeDefaultValues = false;
            float revAdd = data.clockwiseIsPositive ? -1f : 1f;
            AddParameterDriver(revTrigger, new (string,int,float,float)[] {
                (data.rotationIndexParameterName, 1, revAdd, 0f),
                ("RotationCounter_RevArmed", 0, 0f, 0f),
                ("RotationCounter_TestZone", 0, 888f, 0f)
            });

            // Idle → FwdArmed: angle > nearMax + eps
            var idleToFwdArmed = idle.AddTransition(fwdArmed);
            idleToFwdArmed.duration = 0f;
            idleToFwdArmed.hasExitTime = false;
            idleToFwdArmed.canTransitionToSelf = false;
            idleToFwdArmed.AddCondition(AnimatorConditionMode.Greater, nearMaxArm, data.angleParameterName);

            // FwdArmed → FwdTrigger: angle < nearZero - eps
            var fwdArmedToTrigger = fwdArmed.AddTransition(fwdTrigger);
            fwdArmedToTrigger.duration = 0f;
            fwdArmedToTrigger.hasExitTime = false;
            fwdArmedToTrigger.canTransitionToSelf = false;
            fwdArmedToTrigger.AddCondition(AnimatorConditionMode.Less, nearZeroArm, data.angleParameterName);

            // FwdArmed → Idle (disarm): angle < nearMax - eps (fell out)
            var fwdArmedToIdle = fwdArmed.AddTransition(idle);
            fwdArmedToIdle.duration = 0f;
            fwdArmedToIdle.hasExitTime = false;
            fwdArmedToIdle.canTransitionToSelf = false;
            fwdArmedToIdle.AddCondition(AnimatorConditionMode.Less, nearMaxDisarm, data.angleParameterName);

            // FwdTrigger → Idle: unconditional quick return
            var fwdTriggerToIdle = fwdTrigger.AddTransition(idle);
            fwdTriggerToIdle.duration = 0f;
            fwdTriggerToIdle.hasExitTime = true;
            fwdTriggerToIdle.exitTime = 0.0f;
            fwdTriggerToIdle.canTransitionToSelf = false;

            // Idle → RevArmed: angle < nearZero - eps
            var idleToRevArmed = idle.AddTransition(revArmed);
            idleToRevArmed.duration = 0f;
            idleToRevArmed.hasExitTime = false;
            idleToRevArmed.canTransitionToSelf = false;
            idleToRevArmed.AddCondition(AnimatorConditionMode.Less, nearZeroArm, data.angleParameterName);

            // RevArmed → RevTrigger: angle > nearMax + eps
            var revArmedToTrigger = revArmed.AddTransition(revTrigger);
            revArmedToTrigger.duration = 0f;
            revArmedToTrigger.hasExitTime = false;
            revArmedToTrigger.canTransitionToSelf = false;
            revArmedToTrigger.AddCondition(AnimatorConditionMode.Greater, nearMaxArm, data.angleParameterName);

            // RevArmed → Idle (disarm): angle > nearZero + eps (fell out)
            var revArmedToIdle = revArmed.AddTransition(idle);
            revArmedToIdle.duration = 0f;
            revArmedToIdle.hasExitTime = false;
            revArmedToIdle.canTransitionToSelf = false;
            revArmedToIdle.AddCondition(AnimatorConditionMode.Greater, nearZeroDisarm, data.angleParameterName);

            // RevTrigger → Idle: unconditional quick return
            var revTriggerToIdle = revTrigger.AddTransition(idle);
            revTriggerToIdle.duration = 0f;
            revTriggerToIdle.hasExitTime = true;
            revTriggerToIdle.exitTime = 0.0f;
            revTriggerToIdle.canTransitionToSelf = false;
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

