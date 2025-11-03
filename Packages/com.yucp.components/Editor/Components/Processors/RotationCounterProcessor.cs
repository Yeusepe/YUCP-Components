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
            
            // Step progress parameters (counts traversed zones by direction)
            controller.AddParameter("RotationCounter_StepProgressCW", AnimatorControllerParameterType.Int);
            controller.AddParameter("RotationCounter_StepProgressCCW", AnimatorControllerParameterType.Int);

            // One-shot gates to prevent repeated counting while resting
            controller.AddParameter("RotationCounter_GateUp", AnimatorControllerParameterType.Bool);
            controller.AddParameter("RotationCounter_GateDown", AnimatorControllerParameterType.Bool);

            // Step pulses to detect a fresh step this frame
            controller.AddParameter("RotationCounter_StepPulseCW", AnimatorControllerParameterType.Bool);
            controller.AddParameter("RotationCounter_StepPulseCCW", AnimatorControllerParameterType.Bool);
            
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
            // Router state for initial entry into the correct zone based on angle
            var routerState = stateMachine.AddState("Router", new Vector3(50, -50, 0));
            routerState.motion = null;
            routerState.writeDefaultValues = false;
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

                // Add transition from Router to this zone
                var routerToZone = routerState.AddTransition(zoneState);
                routerToZone.duration = 0f;
                routerToZone.hasExitTime = false;
                routerToZone.canTransitionToSelf = true;

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
                    routerToZone.AddCondition(AnimatorConditionMode.Greater, minAngle + epsilon, data.angleParameterName);
                    routerToZone.AddCondition(AnimatorConditionMode.Less, zoneEnd, data.angleParameterName);
                }
                else if (i == numZones - 1)
                {
                    // Last zone: zoneStart to nearMaxThreshold (exclude wraparound range)
                    // If angle is > nearMaxThreshold, we're wrapping, let wraparound handle it
                    float maxAngle = data.nearMaxThreshold;
                    routerToZone.AddCondition(AnimatorConditionMode.Greater, zoneStartThreshold, data.angleParameterName);
                    routerToZone.AddCondition(AnimatorConditionMode.Less, maxAngle - epsilon, data.angleParameterName);
                }
                else
                {
                    // Middle zones: standard range
                    routerToZone.AddCondition(AnimatorConditionMode.Greater, zoneStartThreshold, data.angleParameterName);
                    routerToZone.AddCondition(AnimatorConditionMode.Less, zoneEnd, data.angleParameterName);
                }
            }

            // Set default state to Router (routes into the correct zone based on angle)
            stateMachine.defaultState = routerState;

            // Create transitions for wraparound detection
            int lastZoneIndex = numZones - 1;
            int firstZoneIndex = 0;
            
            // Calculate last zone start once for use in multiple places
            var lastZoneStart = lastZoneIndex * zoneSize;
            
            // Create shared step states
            var stepForward = stateMachine.AddState("StepForward", new Vector3(100, 500, 0));
            stepForward.writeDefaultValues = false;
            AddParameterDriver(stepForward, new (string,int,float,float)[] {
                ("RotationCounter_StepProgressCW", 1, 1f, 0f),
                ("RotationCounter_StepProgressCCW", 0, 0f, 0f),
                ("RotationCounter_StepPulseCW", 0, 1f, 0f),
                ("RotationCounter_StepPulseCCW", 0, 0f, 0f)
            });
            var stepReverse = stateMachine.AddState("StepReverse", new Vector3(100, 600, 0));
            stepReverse.writeDefaultValues = false;
            AddParameterDriver(stepReverse, new (string,int,float,float)[] {
                ("RotationCounter_StepProgressCCW", 1, 1f, 0f),
                ("RotationCounter_StepProgressCW", 0, 0f, 0f),
                ("RotationCounter_StepPulseCCW", 0, 1f, 0f),
                ("RotationCounter_StepPulseCW", 0, 0f, 0f)
            });

            // ClearPulse state resets pulses when we are clearly outside boundary bands
            var clearPulse = stateMachine.AddState("ClearPulse", new Vector3(100, 400, 0));
            clearPulse.writeDefaultValues = false;
            AddParameterDriver(clearPulse, new (string,int,float,float)[] {
                ("RotationCounter_StepPulseCW", 0, 0f, 0f),
                ("RotationCounter_StepPulseCCW", 0, 0f, 0f)
            });

            // Any State band-based detectors for steps and pulse clear
            float band = Mathf.Max(0.001f, data.hysteresisEpsilon);
            for (int k = 0; k < numZones; k++)
            {
                float boundary = k * zoneSize;
                float fwdMin = boundary + 0.0005f;
                float fwdMax = Mathf.Min(1f, boundary + band);
                float revMin = Mathf.Max(0f, boundary - band);
                float revMax = boundary - 0.0005f;

                // Any State → StepForward for post-boundary band
                var anyToStepFwd = stateMachine.AddAnyStateTransition(stepForward);
                anyToStepFwd.duration = 0f;
                anyToStepFwd.hasExitTime = false;
                anyToStepFwd.canTransitionToSelf = true;
                anyToStepFwd.AddCondition(AnimatorConditionMode.Greater, fwdMin, data.angleParameterName);
                anyToStepFwd.AddCondition(AnimatorConditionMode.Less, fwdMax, data.angleParameterName);
                anyToStepFwd.AddCondition(AnimatorConditionMode.IfNot, 0f, "RotationCounter_StepPulseCW");

                // Any State → StepReverse for pre-boundary band
                var anyToStepRev = stateMachine.AddAnyStateTransition(stepReverse);
                anyToStepRev.duration = 0f;
                anyToStepRev.hasExitTime = false;
                anyToStepRev.canTransitionToSelf = true;
                // Special case for boundary 0: pre-boundary band wraps at 1.0
                if (k == 0)
                {
                    anyToStepRev.AddCondition(AnimatorConditionMode.Greater, 1f - band, data.angleParameterName);
                    anyToStepRev.AddCondition(AnimatorConditionMode.Less, 1f - 0.0005f, data.angleParameterName);
                }
                else
                {
                    anyToStepRev.AddCondition(AnimatorConditionMode.Greater, revMin, data.angleParameterName);
                    anyToStepRev.AddCondition(AnimatorConditionMode.Less, revMax, data.angleParameterName);
                }
                anyToStepRev.AddCondition(AnimatorConditionMode.IfNot, 0f, "RotationCounter_StepPulseCCW");

                // Any State → ClearPulse for interior of zone (away from both bands)
                float interiorStart = boundary + band;
                float interiorEnd = (k + 1) * zoneSize - band;
                if (interiorEnd > interiorStart)
                {
                    var anyToClear = stateMachine.AddAnyStateTransition(clearPulse);
                    anyToClear.duration = 0f;
                    anyToClear.hasExitTime = false;
                    anyToClear.canTransitionToSelf = true;
                    anyToClear.AddCondition(AnimatorConditionMode.Greater, interiorStart, data.angleParameterName);
                    anyToClear.AddCondition(AnimatorConditionMode.Less, interiorEnd, data.angleParameterName);
                }
            }

            // Count states in the same layer: perform the increment/decrement immediately on step threshold
            int sections = Mathf.Max(1, Mathf.Min(data.sectionsPerCount, numZones));
            var countUp = stateMachine.AddState("CountUp", new Vector3(300, 500, 0));
            countUp.writeDefaultValues = false;
            float addUp = data.clockwiseIsPositive ? 1f : -1f;
            AddParameterDriver(countUp, new (string,int,float,float)[] {
                (data.rotationIndexParameterName, 1, addUp, 0f),
                ("RotationCounter_StepProgressCW", 1, -sections, 0f)
            });
            var countDown = stateMachine.AddState("CountDown", new Vector3(300, 600, 0));
            countDown.writeDefaultValues = false;
            float addDown = data.clockwiseIsPositive ? -1f : 1f;
            AddParameterDriver(countDown, new (string,int,float,float)[] {
                (data.rotationIndexParameterName, 1, addDown, 0f),
                ("RotationCounter_StepProgressCCW", 1, -sections, 0f)
            });

            // Add transitions between adjacent zones (normal rotation, no wraparound)
            // These handle all normal zone-to-zone transitions
            for (int i = 0; i < numZones; i++)
            {
                int nextZone = (i + 1) % numZones;
                int prevZone = (i - 1 + numZones) % numZones;

                // Forward rotation: i → StepForward (then StepForward → nextZone)
                var forwardTransition = zoneStates[i].AddTransition(stepForward);
                forwardTransition.duration = 0f;
                forwardTransition.hasExitTime = false;
                forwardTransition.canTransitionToSelf = false;
                
                // Skip last zone → first zone transition - wraparound detection handles it
                if (i == lastZoneIndex)
                {
                    // Don't create direct last → first transition
                    // Wraparound detection will handle this case
                    // For step counting, we still route via stepForward using first zone range
                    forwardTransition.AddCondition(AnimatorConditionMode.Greater, data.nearZeroThreshold, data.angleParameterName);
                    forwardTransition.AddCondition(AnimatorConditionMode.Less, zoneSize, data.angleParameterName);
                }
                else
                {
                    // Normal forward transitions
                    float nextZoneStart = nextZone * zoneSize;
                    float epsilon = 0.001f;
                    float nextZoneThreshold = nextZoneStart > epsilon ? nextZoneStart - epsilon : -0.001f;
                    forwardTransition.AddCondition(AnimatorConditionMode.Greater, nextZoneThreshold, data.angleParameterName);
                }

                // Reverse rotation: i → StepReverse (then StepReverse → prevZone)
                var reverseTransition = zoneStates[i].AddTransition(stepReverse);
                reverseTransition.duration = 0f;
                reverseTransition.hasExitTime = false;
                reverseTransition.canTransitionToSelf = false;
                
                // Skip first zone → last zone transition - wraparound detection handles it
                if (i == firstZoneIndex)
                {
                    // Don't create direct first → last transition  
                    // Wraparound detection will handle this case
                    reverseTransition.AddCondition(AnimatorConditionMode.Greater, lastZoneStart, data.angleParameterName);
                    reverseTransition.AddCondition(AnimatorConditionMode.Less, data.nearMaxThreshold, data.angleParameterName);
                }
                else
                {
                    // Normal reverse transitions
                    float prevZoneStart = prevZone * zoneSize;
                    reverseTransition.AddCondition(AnimatorConditionMode.Less, prevZoneStart, data.angleParameterName);
                }
            }
            
            // From stepForward/stepReverse, route to the appropriate zone based on current angle range
            for (int i = 0; i < numZones; i++)
            {
                float zoneStart = i * zoneSize;
                float zoneEnd = (i + 1) * zoneSize;
                float epsilon = 0.001f;
                float zoneStartThreshold = zoneStart > epsilon ? zoneStart - epsilon : -0.001f;

                var stepFwdToZone = stepForward.AddTransition(zoneStates[i]);
                stepFwdToZone.duration = 0f;
                stepFwdToZone.hasExitTime = false;
                stepFwdToZone.canTransitionToSelf = false;
                if (i == 0)
                {
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Greater, data.nearZeroThreshold, data.angleParameterName);
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Less, zoneEnd, data.angleParameterName);
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Less, sections - 0.5f, "RotationCounter_StepProgressCW");
                }
                else if (i == numZones - 1)
                {
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Greater, zoneStartThreshold, data.angleParameterName);
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Less, data.nearMaxThreshold, data.angleParameterName);
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Less, sections - 0.5f, "RotationCounter_StepProgressCW");
                }
                else
                {
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Greater, zoneStartThreshold, data.angleParameterName);
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Less, zoneEnd, data.angleParameterName);
                    stepFwdToZone.AddCondition(AnimatorConditionMode.Less, sections - 0.5f, "RotationCounter_StepProgressCW");
                }

                // If threshold reached on this step, go to CountUp instead of zone
                var stepFwdToCount = stepForward.AddTransition(countUp);
                stepFwdToCount.duration = 0f;
                stepFwdToCount.hasExitTime = false;
                stepFwdToCount.canTransitionToSelf = false;
                stepFwdToCount.AddCondition(AnimatorConditionMode.Greater, sections - 0.5f, "RotationCounter_StepProgressCW");
 
                var stepRevToZone = stepReverse.AddTransition(zoneStates[i]);
                stepRevToZone.duration = 0f;
                stepRevToZone.hasExitTime = false;
                stepRevToZone.canTransitionToSelf = false;
                if (i == 0)
                {
                    stepRevToZone.AddCondition(AnimatorConditionMode.Greater, data.nearZeroThreshold, data.angleParameterName);
                    stepRevToZone.AddCondition(AnimatorConditionMode.Less, zoneEnd, data.angleParameterName);
                    stepRevToZone.AddCondition(AnimatorConditionMode.Less, sections - 0.5f, "RotationCounter_StepProgressCCW");
                }
                else if (i == numZones - 1)
                {
                    stepRevToZone.AddCondition(AnimatorConditionMode.Greater, zoneStartThreshold, data.angleParameterName);
                    stepRevToZone.AddCondition(AnimatorConditionMode.Less, data.nearMaxThreshold, data.angleParameterName);
                    stepRevToZone.AddCondition(AnimatorConditionMode.Less, sections - 0.5f, "RotationCounter_StepProgressCCW");
                }
                else
                {
                    stepRevToZone.AddCondition(AnimatorConditionMode.Greater, zoneStartThreshold, data.angleParameterName);
                    stepRevToZone.AddCondition(AnimatorConditionMode.Less, zoneEnd, data.angleParameterName);
                    stepRevToZone.AddCondition(AnimatorConditionMode.Less, sections - 0.5f, "RotationCounter_StepProgressCCW");
                }

                var stepRevToCount = stepReverse.AddTransition(countDown);
                stepRevToCount.duration = 0f;
                stepRevToCount.hasExitTime = false;
                stepRevToCount.canTransitionToSelf = false;
                stepRevToCount.AddCondition(AnimatorConditionMode.Greater, sections - 0.5f, "RotationCounter_StepProgressCCW");
            }

            // After counting, route back to router to land in the correct zone based on current angle
            var countUpToRouter = countUp.AddTransition(routerState);
            countUpToRouter.duration = 0f;
            countUpToRouter.hasExitTime = true;
            countUpToRouter.exitTime = 0f;
            countUpToRouter.canTransitionToSelf = false;

            var countDownToRouter = countDown.AddTransition(routerState);
            countDownToRouter.duration = 0f;
            countDownToRouter.hasExitTime = true;
            countDownToRouter.exitTime = 0f;
            countDownToRouter.canTransitionToSelf = false;
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

