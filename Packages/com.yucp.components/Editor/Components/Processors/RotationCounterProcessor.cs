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
    /// Generates Animator Controller with zone states to detect wraparounds and increment RotationIndex.
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

            if (string.IsNullOrEmpty(data.angle01ParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Angle01 parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (string.IsNullOrEmpty(data.magnitudeParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Magnitude parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (string.IsNullOrEmpty(data.rotationIndexParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Rotation index parameter name is not set for '{data.name}'", data);
                return false;
            }

            if (string.IsNullOrEmpty(data.directionParameterName))
            {
                Debug.LogError($"[RotationCounterProcessor] Direction parameter name is not set for '{data.name}'", data);
                return false;
            }



            if (data.numberOfSectors < 4 || data.numberOfSectors > 24)
            {
                Debug.LogError($"[RotationCounterProcessor] Number of sectors must be between 4 and 24 for '{data.name}'", data);
                return false;
            }

            var sectorWidth = 1f / data.numberOfSectors;
            if (data.sectorHysteresis >= sectorWidth * 0.5f)
            {
                Debug.LogError($"[RotationCounterProcessor] Sector hysteresis is too large for '{data.name}'. Reduce it below half of a sector width.", data);
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

                        AddIfMissing(data.angle01ParameterName);
                        AddIfMissing(data.magnitudeParameterName);
                        AddIfMissing(data.rotationIndexParameterName);
                        AddIfMissing(data.directionParameterName);
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
                fullController.AddGlobalParam(data.angle01ParameterName);
                fullController.AddGlobalParam(data.magnitudeParameterName);
                fullController.AddGlobalParam(data.rotationIndexParameterName);
                fullController.AddGlobalParam(data.directionParameterName);
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
            
            controller.AddParameter(data.angle01ParameterName, AnimatorControllerParameterType.Float);
            controller.AddParameter(data.magnitudeParameterName, AnimatorControllerParameterType.Float);
            controller.AddParameter(data.rotationIndexParameterName, AnimatorControllerParameterType.Int);
            controller.AddParameter(data.directionParameterName, AnimatorControllerParameterType.Int);

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

            // Calculate layout
            float sectorWidth = 1f / data.numberOfSectors;
            
            // Create states
            var idleStates = new AnimatorState[data.numberOfSectors];
            
            for (int i = 0; i < data.numberOfSectors; i++)
            {
                var pos = GetSectorStatePosition(i, 0);
                var state = stateMachine.AddState($"Sector_{i}_Idle", pos);
                state.writeDefaultValues = false;
                // In idle, we just report the phase if debug is on
                SetStateDriver(state, data, (float)i); 
                idleStates[i] = state;
            }

            // Create transitions and intermediate states
            for (int i = 0; i < data.numberOfSectors; i++)
            {
                int next = (i + 1) % data.numberOfSectors;
                int prev = (i - 1 + data.numberOfSectors) % data.numberOfSectors;

                // INC: i -> next
                var incState = stateMachine.AddState($"Sector_{i}_Inc", GetSectorStatePosition(i, 1));
                incState.writeDefaultValues = false;
                SetStateDriver(incState, data, null,
                    (data.rotationIndexParameterName, DriverAdd, 1f),
                    (data.directionParameterName, DriverSet, 1f));
                
                var incTrans = incState.AddTransition(idleStates[next]);
                ConfigureInstantTransition(incTrans);

                // DEC: i -> prev
                var decState = stateMachine.AddState($"Sector_{i}_Dec", GetSectorStatePosition(i, -1));
                decState.writeDefaultValues = false;
                SetStateDriver(decState, data, null,
                    (data.rotationIndexParameterName, DriverAdd, -1f),
                    (data.directionParameterName, DriverSet, -1f));

                var decTrans = decState.AddTransition(idleStates[prev]);
                ConfigureInstantTransition(decTrans);

                // Transitions from Idle
                // To INC
                var toInc = idleStates[i].AddTransition(incState);
                ConfigureInstantTransition(toInc);
                AddMagnitudeGate(toInc, data);
                
                float incThreshold;
                if (i == data.numberOfSectors - 1) // Last sector -> 0
                {
                    // Wrap around: Angle < Sector 0 width - hysteresis
                    // Actually, simpler: if we are in last sector (approx 0.9-1.0), and angle is small (approx 0.0-0.1)
                    // Threshold = 0 + width - hysteresis
                    incThreshold = sectorWidth - data.sectorHysteresis;
                    toInc.AddCondition(AnimatorConditionMode.Less, incThreshold, data.angle01ParameterName);
                }
                else
                {
                    // Normal: Angle > next start + hysteresis
                    incThreshold = ((i + 1) * sectorWidth) + data.sectorHysteresis;
                    toInc.AddCondition(AnimatorConditionMode.Greater, incThreshold, data.angle01ParameterName);
                }

                // To DEC
                var toDec = idleStates[i].AddTransition(decState);
                ConfigureInstantTransition(toDec);
                AddMagnitudeGate(toDec, data);

                float decThreshold;
                if (i == 0) // Sector 0 -> Last
                {
                    // Wrap around: Angle > Last sector start + hysteresis
                    decThreshold = 1f - sectorWidth + data.sectorHysteresis;
                    toDec.AddCondition(AnimatorConditionMode.Greater, decThreshold, data.angle01ParameterName);
                }
                else
                {
                    // Normal: Angle < current start - hysteresis
                    decThreshold = (i * sectorWidth) - data.sectorHysteresis;
                    toDec.AddCondition(AnimatorConditionMode.Less, decThreshold, data.angle01ParameterName);
                }
            }

            stateMachine.defaultState = idleStates[0];
            layer.stateMachine = stateMachine;
            layers[0] = layer;
            controller.layers = layers;
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

        private void AddMagnitudeGate(AnimatorStateTransition transition, RotationCounterData data)
        {
            if (!string.IsNullOrEmpty(data.magnitudeParameterName) && data.magnitudeThreshold > 0)
            {
                transition.AddCondition(AnimatorConditionMode.Greater, data.magnitudeThreshold, data.magnitudeParameterName);
            }
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

