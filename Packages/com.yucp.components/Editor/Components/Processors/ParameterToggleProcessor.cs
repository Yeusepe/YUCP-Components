using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using YUCP.Components;
using VF.Model;
using VF.Model.StateAction;

namespace YUCP.Components.Editor
{
    public class ParameterToggleProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 50;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var components = avatarRoot.GetComponentsInChildren<ParameterToggleData>(true);

            foreach (var data in components)
            {
                if (data == null || !data.enabled) continue;
                ProcessParameterToggle(data);
            }

            return true;
        }

        private void ProcessParameterToggle(ParameterToggleData data)
        {
            if (!ValidateData(data)) return;

            Debug.Log($"[ParameterToggle] Processing '{data.name}'", data);

            try
            {
                var toggle = FuryComponents.CreateToggle(data.gameObject);
                
                ConfigureToggleSettings(data, toggle);
                ConfigureToggleActions(data, toggle);
                
                var toggleModel = GetToggleModel(toggle);
                if (toggleModel != null)
                {
                    var modelType = toggleModel.GetType();
                    var addMenuItemField = modelType.GetField("addMenuItem", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    addMenuItemField?.SetValue(toggleModel, false);
                    
                    if (!string.IsNullOrEmpty(data.paramOverride))
                    {
                        var paramOverrideField = modelType.GetField("paramOverride", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        paramOverrideField?.SetValue(toggleModel, data.paramOverride);
                    }
                }

                CreateDriverLayer(data, toggle, toggleModel);
                
                Debug.Log($"[ParameterToggle] Successfully processed '{data.name}'", data);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParameterToggle] Error processing '{data.name}': {ex.Message}\n{ex.StackTrace}", data);
            }
        }

        private bool ValidateData(ParameterToggleData data)
        {
            if (data.conditionGroups == null || data.conditionGroups.Count == 0)
            {
                Debug.LogError($"[ParameterToggle] No condition groups defined on '{data.name}'. At least one condition group is required.", data);
                return false;
            }

            bool hasValidConditions = false;
            foreach (var group in data.conditionGroups)
            {
                if (group != null && group.conditions != null && group.conditions.Count > 0)
                {
                    foreach (var condition in group.conditions)
                    {
                        if (condition != null && !string.IsNullOrEmpty(condition.parameterName))
                        {
                            hasValidConditions = true;
                            break;
                        }
                    }
                }
                if (hasValidConditions) break;
            }

            if (!hasValidConditions)
            {
                Debug.LogError($"[ParameterToggle] No valid conditions found in any group on '{data.name}'.", data);
                return false;
            }

            return true;
        }

        private object GetToggleModel(FuryToggle toggle)
        {
            var toggleType = typeof(FuryToggle);
            var cField = toggleType.GetField("c", BindingFlags.NonPublic | BindingFlags.Instance);
            return cField?.GetValue(toggle);
        }

        private void ConfigureToggleSettings(ParameterToggleData data, FuryToggle toggle)
        {
            var toggleModel = GetToggleModel(toggle);
            if (toggleModel == null) return;

            var modelType = toggleModel.GetType();

            SetField(modelType, toggleModel, "saved", data.saved);
            SetField(modelType, toggleModel, "slider", data.slider);
            SetField(modelType, toggleModel, "sliderInactiveAtZero", data.sliderInactiveAtZero);
            SetField(modelType, toggleModel, "securityEnabled", data.securityEnabled);
            SetField(modelType, toggleModel, "defaultOn", data.defaultOn);
            SetField(modelType, toggleModel, "hasExitTime", data.hasExitTime);
            SetField(modelType, toggleModel, "holdButton", data.holdButton);
            SetField(modelType, toggleModel, "defaultSliderValue", data.defaultSliderValue);
            SetField(modelType, toggleModel, "enableExclusiveTag", data.enableExclusiveTag);
            SetField(modelType, toggleModel, "exclusiveTag", data.exclusiveTag);
            SetField(modelType, toggleModel, "exclusiveOffState", data.exclusiveOffState);
            SetField(modelType, toggleModel, "enableIcon", data.enableIcon);
            SetField(modelType, toggleModel, "useGlobalParam", data.useGlobalParam);
            SetField(modelType, toggleModel, "globalParam", data.globalParam);
            SetField(modelType, toggleModel, "enableDriveGlobalParam", data.enableDriveGlobalParam);
            SetField(modelType, toggleModel, "driveGlobalParam", data.driveGlobalParam);
            SetField(modelType, toggleModel, "separateLocal", data.separateLocal);
            SetField(modelType, toggleModel, "hasTransition", data.hasTransition);
            SetField(modelType, toggleModel, "transitionTimeIn", data.transitionTimeIn);
            SetField(modelType, toggleModel, "transitionTimeOut", data.transitionTimeOut);
            SetField(modelType, toggleModel, "simpleOutTransition", data.simpleOutTransition);
            SetField(modelType, toggleModel, "expandIntoTransition", data.expandIntoTransition);
            SetField(modelType, toggleModel, "localTransitionTimeIn", data.localTransitionTimeIn);
            SetField(modelType, toggleModel, "localTransitionTimeOut", data.localTransitionTimeOut);
            SetField(modelType, toggleModel, "invertRestLogic", data.invertRestLogic);

            if (data.enableIcon && data.icon != null)
            {
                var iconField = modelType.GetField("icon", BindingFlags.Public | BindingFlags.Instance);
                if (iconField != null)
                {
                    var guidTexture2dType = iconField.FieldType;
                    var implicitOp = guidTexture2dType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Texture2D) }, null);
                    if (implicitOp != null)
                    {
                        var guidTextureInstance = implicitOp.Invoke(null, new object[] { data.icon });
                        iconField.SetValue(toggleModel, guidTextureInstance);
                    }
                }
            }

            if (data.saved) toggle.SetSaved();
            if (data.defaultOn) toggle.SetDefaultOn();
            if (data.slider) toggle.SetSlider();
            if (data.enableExclusiveTag && !string.IsNullOrEmpty(data.exclusiveTag))
            {
                toggle.AddExclusiveTag(data.exclusiveTag);
            }
            if (data.useGlobalParam && !string.IsNullOrEmpty(data.globalParam))
            {
                toggle.SetGlobalParameter(data.globalParam);
            }
        }

        private void SetField(Type type, object instance, string fieldName, object value)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(instance, value);
            }
        }

        private void ConfigureToggleActions(ParameterToggleData data, FuryToggle toggle)
        {
            var toggleModel = GetToggleModel(toggle);
            if (toggleModel == null) return;

            var modelType = toggleModel.GetType();
            var stateField = modelType.GetField("state", BindingFlags.Public | BindingFlags.Instance);
            var state = stateField?.GetValue(toggleModel);
            
            if (state == null)
            {
                var stateType = Type.GetType("VF.Model.State, VRCFury");
                if (stateType != null)
                {
                    state = Activator.CreateInstance(stateType);
                    stateField?.SetValue(toggleModel, state);
                }
            }

            var actionsField = state.GetType().GetField("actions", BindingFlags.Public | BindingFlags.Instance);
            var actions = actionsField?.GetValue(state) as System.Collections.IList;
            if (actions != null)
            {
                ConvertStateDataToActions(data.state, actions);
            }

            if (data.hasTransition)
            {
                var stateType = Type.GetType("VF.Model.State, VRCFury");
                
                var transitionStateInField = modelType.GetField("transitionStateIn", BindingFlags.Public | BindingFlags.Instance);
                var transitionStateIn = transitionStateInField?.GetValue(toggleModel);
                if (transitionStateIn == null && stateType != null)
                {
                    transitionStateIn = Activator.CreateInstance(stateType);
                    transitionStateInField?.SetValue(toggleModel, transitionStateIn);
                }
                var transitionStateInActions = transitionStateIn.GetType().GetField("actions", BindingFlags.Public | BindingFlags.Instance)?.GetValue(transitionStateIn) as System.Collections.IList;
                if (transitionStateInActions != null)
                {
                    ConvertStateDataToActions(data.transitionStateIn, transitionStateInActions);
                }

                var transitionStateOutField = modelType.GetField("transitionStateOut", BindingFlags.Public | BindingFlags.Instance);
                var transitionStateOut = transitionStateOutField?.GetValue(toggleModel);
                if (transitionStateOut == null && stateType != null)
                {
                    transitionStateOut = Activator.CreateInstance(stateType);
                    transitionStateOutField?.SetValue(toggleModel, transitionStateOut);
                }
                var transitionStateOutActions = transitionStateOut.GetType().GetField("actions", BindingFlags.Public | BindingFlags.Instance)?.GetValue(transitionStateOut) as System.Collections.IList;
                if (transitionStateOutActions != null)
                {
                    ConvertStateDataToActions(data.transitionStateOut, transitionStateOutActions);
                }
            }

            if (data.separateLocal)
            {
                var stateType = Type.GetType("VF.Model.State, VRCFury");
                
                var localStateField = modelType.GetField("localState", BindingFlags.Public | BindingFlags.Instance);
                var localState = localStateField?.GetValue(toggleModel);
                if (localState == null && stateType != null)
                {
                    localState = Activator.CreateInstance(stateType);
                    localStateField?.SetValue(toggleModel, localState);
                }
                var localStateActions = localState.GetType().GetField("actions", BindingFlags.Public | BindingFlags.Instance)?.GetValue(localState) as System.Collections.IList;
                if (localStateActions != null)
                {
                    ConvertStateDataToActions(data.localState, localStateActions);
                }

                if (data.hasTransition)
                {
                    var localTransitionStateInField = modelType.GetField("localTransitionStateIn", BindingFlags.Public | BindingFlags.Instance);
                    var localTransitionStateIn = localTransitionStateInField?.GetValue(toggleModel);
                    if (localTransitionStateIn == null && stateType != null)
                    {
                        localTransitionStateIn = Activator.CreateInstance(stateType);
                        localTransitionStateInField?.SetValue(toggleModel, localTransitionStateIn);
                    }
                    var localTransitionStateInActions = localTransitionStateIn.GetType().GetField("actions", BindingFlags.Public | BindingFlags.Instance)?.GetValue(localTransitionStateIn) as System.Collections.IList;
                    if (localTransitionStateInActions != null)
                    {
                        ConvertStateDataToActions(data.localTransitionStateIn, localTransitionStateInActions);
                    }

                    var localTransitionStateOutField = modelType.GetField("localTransitionStateOut", BindingFlags.Public | BindingFlags.Instance);
                    var localTransitionStateOut = localTransitionStateOutField?.GetValue(toggleModel);
                    if (localTransitionStateOut == null && stateType != null)
                    {
                        localTransitionStateOut = Activator.CreateInstance(stateType);
                        localTransitionStateOutField?.SetValue(toggleModel, localTransitionStateOut);
                    }
                    var localTransitionStateOutActions = localTransitionStateOut.GetType().GetField("actions", BindingFlags.Public | BindingFlags.Instance)?.GetValue(localTransitionStateOut) as System.Collections.IList;
                    if (localTransitionStateOutActions != null)
                    {
                        ConvertStateDataToActions(data.localTransitionStateOut, localTransitionStateOutActions);
                    }
                }
            }
        }

        private void ConvertStateDataToActions(StateData stateData, System.Collections.IList actions)
        {
            if (stateData == null) return;

            if (stateData.animationClip != null)
            {
                try
                {
                    var actionType = Type.GetType("VF.Model.StateAction.AnimationClipAction, VRCFury");
                    if (actionType == null) return;
                    var action = Activator.CreateInstance(actionType);
                    var clipField = actionType.GetField("clip", BindingFlags.Public | BindingFlags.Instance);
                    if (clipField != null)
                    {
                        var guidClipType = clipField.FieldType;
                        var implicitOp = guidClipType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(AnimationClip) }, null);
                        if (implicitOp != null)
                        {
                            var guidClip = implicitOp.Invoke(null, new object[] { stateData.animationClip });
                            clipField.SetValue(action, guidClip);
                        }
                    }
                    actions.Add(action);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ParameterToggle] Could not create AnimationClipAction: {ex.Message}");
                }
            }

            foreach (var obj in stateData.turnOnObjects)
            {
                if (obj != null)
                {
                    try
                    {
                        var actionType = Type.GetType("VF.Model.StateAction.ObjectToggleAction, VRCFury");
                        if (actionType == null) continue;
                        var action = Activator.CreateInstance(actionType);
                        var objField = actionType.GetField("obj", BindingFlags.Public | BindingFlags.Instance);
                        var modeField = actionType.GetField("mode", BindingFlags.Public | BindingFlags.Instance);
                        var modeEnumType = actionType.GetNestedType("Mode", BindingFlags.Public | BindingFlags.NonPublic);
                        objField?.SetValue(action, obj);
                        if (modeEnumType != null && modeField != null)
                        {
                            var turnOnValue = Enum.Parse(modeEnumType, "TurnOn");
                            modeField.SetValue(action, turnOnValue);
                        }
                        actions.Add(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ParameterToggle] Could not create ObjectToggleAction: {ex.Message}");
                    }
                }
            }

            foreach (var obj in stateData.turnOffObjects)
            {
                if (obj != null)
                {
                    try
                    {
                        var actionType = Type.GetType("VF.Model.StateAction.ObjectToggleAction, VRCFury");
                        if (actionType == null) continue;
                        var action = Activator.CreateInstance(actionType);
                        var objField = actionType.GetField("obj", BindingFlags.Public | BindingFlags.Instance);
                        var modeField = actionType.GetField("mode", BindingFlags.Public | BindingFlags.Instance);
                        var modeEnumType = actionType.GetNestedType("Mode", BindingFlags.Public | BindingFlags.NonPublic);
                        objField?.SetValue(action, obj);
                        if (modeEnumType != null && modeField != null)
                        {
                            var turnOffValue = Enum.Parse(modeEnumType, "TurnOff");
                            modeField.SetValue(action, turnOffValue);
                        }
                        actions.Add(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ParameterToggle] Could not create ObjectToggleAction: {ex.Message}");
                    }
                }
            }

            foreach (var blendShape in stateData.blendshapeActions)
            {
                if (!string.IsNullOrEmpty(blendShape.blendShapeName))
                {
                    try
                    {
                        var actionType = Type.GetType("VF.Model.StateAction.BlendShapeAction, VRCFury");
                        if (actionType == null) continue;
                        var action = Activator.CreateInstance(actionType);
                        actionType.GetField("blendShape", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, blendShape.blendShapeName);
                        actionType.GetField("blendShapeValue", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, blendShape.blendShapeValue);
                        actionType.GetField("renderer", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, blendShape.renderer);
                        actionType.GetField("allRenderers", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, blendShape.allRenderers);
                        actions.Add(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ParameterToggle] Could not create BlendShapeAction: {ex.Message}");
                    }
                }
            }

            foreach (var materialSwap in stateData.materialSwaps)
            {
                if (materialSwap.renderer != null && materialSwap.material != null)
                {
                    try
                    {
                        var actionType = Type.GetType("VF.Model.StateAction.MaterialAction, VRCFury");
                        if (actionType == null) continue;
                        var action = Activator.CreateInstance(actionType);
                        actionType.GetField("renderer", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, materialSwap.renderer);
                        actionType.GetField("materialIndex", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, materialSwap.materialIndex);
                        var matField = actionType.GetField("mat", BindingFlags.Public | BindingFlags.Instance);
                        if (matField != null)
                        {
                            var guidMatType = matField.FieldType;
                            var implicitOp = guidMatType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Material) }, null);
                            if (implicitOp != null)
                            {
                                var guidMat = implicitOp.Invoke(null, new object[] { materialSwap.material });
                                matField.SetValue(action, guidMat);
                            }
                        }
                        actions.Add(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ParameterToggle] Could not create MaterialAction: {ex.Message}");
                    }
                }
            }

            foreach (var materialProp in stateData.materialProperties)
            {
                if (!string.IsNullOrEmpty(materialProp.propertyName) && materialProp.rendererObject != null)
                {
                    try
                    {
                        var actionType = Type.GetType("VF.Model.StateAction.MaterialPropertyAction, VRCFury");
                        if (actionType == null) continue;
                        var action = Activator.CreateInstance(actionType);
                        if (action != null)
                        {
                            var renderer2Field = actionType.GetField("renderer2", BindingFlags.Public | BindingFlags.Instance);
                            var affectAllMeshesField = actionType.GetField("affectAllMeshes", BindingFlags.Public | BindingFlags.Instance);
                            var propertyNameField = actionType.GetField("propertyName", BindingFlags.Public | BindingFlags.Instance);
                            var propertyTypeField = actionType.GetField("propertyType", BindingFlags.Public | BindingFlags.Instance);
                            var valueField = actionType.GetField("value", BindingFlags.Public | BindingFlags.Instance);
                            var valueColorField = actionType.GetField("valueColor", BindingFlags.Public | BindingFlags.Instance);
                            var valueVectorField = actionType.GetField("valueVector", BindingFlags.Public | BindingFlags.Instance);

                            renderer2Field?.SetValue(action, materialProp.rendererObject);
                            affectAllMeshesField?.SetValue(action, materialProp.affectAllMeshes);
                            propertyNameField?.SetValue(action, materialProp.propertyName);
                            
                            var propertyTypeEnum = ConvertMaterialPropertyType(materialProp.propertyType);
                            propertyTypeField?.SetValue(action, propertyTypeEnum);

                            switch (materialProp.propertyType)
                            {
                                case MaterialPropertyType.Float:
                                    valueField?.SetValue(action, materialProp.value);
                                    break;
                                case MaterialPropertyType.Color:
                                    valueColorField?.SetValue(action, materialProp.valueColor);
                                    break;
                                case MaterialPropertyType.Vector:
                                    valueVectorField?.SetValue(action, materialProp.valueVector);
                                    break;
                            }

                            actions.Add(action);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ParameterToggle] Could not create MaterialPropertyAction: {ex.Message}");
                    }
                }
            }

            foreach (var scale in stateData.scaleActions)
            {
                if (scale.obj != null)
                {
                    try
                    {
                        var actionType = Type.GetType("VF.Model.StateAction.ScaleAction, VRCFury");
                        if (actionType == null) continue;
                        var action = Activator.CreateInstance(actionType);
                        actionType.GetField("obj", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, scale.obj);
                        actionType.GetField("scale", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, scale.scale);
                        actions.Add(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ParameterToggle] Could not create ScaleAction: {ex.Message}");
                    }
                }
            }

            foreach (var fxFloat in stateData.fxFloatActions)
            {
                if (!string.IsNullOrEmpty(fxFloat.name))
                {
                    try
                    {
                        var actionType = Type.GetType("VF.Model.StateAction.FxFloatAction, VRCFury");
                        if (actionType == null) continue;
                        var action = Activator.CreateInstance(actionType);
                        actionType.GetField("name", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, fxFloat.name);
                        actionType.GetField("value", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, fxFloat.value);
                        actions.Add(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ParameterToggle] Could not create FxFloatAction: {ex.Message}");
                    }
                }
            }

            foreach (var resetPhysbone in stateData.resetPhysbones)
            {
                if (resetPhysbone.physBone != null)
                {
                    try
                    {
                        var actionType = Type.GetType("VF.Model.StateAction.ResetPhysboneAction, VRCFury");
                        if (actionType == null) continue;
                        var action = Activator.CreateInstance(actionType);
                        actionType.GetField("physBone", BindingFlags.Public | BindingFlags.Instance)?.SetValue(action, resetPhysbone.physBone);
                        actions.Add(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ParameterToggle] Could not create ResetPhysboneAction: {ex.Message}");
                    }
                }
            }
        }

        private object ConvertMaterialPropertyType(MaterialPropertyType type)
        {
            try
            {
                var materialPropertyActionType = Type.GetType("VF.Model.StateAction.MaterialPropertyAction, VRCFury");
                if (materialPropertyActionType == null) return null;
                var typeEnumType = materialPropertyActionType.GetNestedType("Type", BindingFlags.Public | BindingFlags.NonPublic);
                if (typeEnumType == null) return null;

                switch (type)
                {
                    case MaterialPropertyType.Float: return Enum.Parse(typeEnumType, "Float");
                    case MaterialPropertyType.Color: return Enum.Parse(typeEnumType, "Color");
                    case MaterialPropertyType.Vector: return Enum.Parse(typeEnumType, "Vector");
                    case MaterialPropertyType.St: return Enum.Parse(typeEnumType, "St");
                    default: return Enum.Parse(typeEnumType, "Float");
                }
            }
            catch
            {
                return null;
            }
        }

        private void CreateDriverLayer(ParameterToggleData data, FuryToggle toggle, object toggleModel)
        {
            try
            {
                var driverLayerBuilder = new ParameterDriverLayerBuilder(data, toggle, toggleModel);
                driverLayerBuilder.Build();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ParameterToggle] Could not create driver layer during preprocess. " +
                    $"The toggle has been created but parameter-driven activation may not work. " +
                    $"Error: {ex.Message}\n" +
                    $"Note: Driver layer creation requires VRCFury's build system to be initialized. " +
                    $"This is expected if VRCFury processes components after preprocess callbacks.", data);
            }
        }
    }

    internal class ParameterDriverLayerBuilder
    {
        private readonly ParameterToggleData data;
        private readonly FuryToggle toggle;
        private readonly object toggleModel;

        public ParameterDriverLayerBuilder(ParameterToggleData data, FuryToggle toggle, object toggleModel)
        {
            this.data = data;
            this.toggle = toggle;
            this.toggleModel = toggleModel;
        }

        public void Build()
        {
            var condition = BuildConditionFromGroups();
            if (condition == null)
            {
                Debug.LogWarning($"[ParameterToggle] Could not build condition for '{data.name}'", data);
                return;
            }

            var toggleParam = GetToggleParameter();
            if (toggleParam == null)
            {
                Debug.LogWarning($"[ParameterToggle] Could not get toggle parameter for '{data.name}'", data);
                return;
            }

            CreateDriverLayer(condition, toggleParam);
        }

        private object BuildConditionFromGroups()
        {
            try
            {
                var vfConditionType = Type.GetType("VF.Utils.Controller.VFCondition, VRCFury-Editor");
                if (vfConditionType == null) return null;

                var fx = GetFXController();
                if (fx == null) return null;

                var conditions = new List<object>();

                foreach (var group in data.conditionGroups)
                {
                    if (group == null || group.conditions == null || group.conditions.Count == 0) continue;

                    var groupCondition = BuildGroupCondition(group, fx);
                    if (groupCondition != null)
                    {
                        conditions.Add(groupCondition);
                    }
                }

                if (conditions.Count == 0) return null;

                if (conditions.Count == 1)
                {
                    return conditions[0];
                }

                var anyMethod = vfConditionType.GetMethod("Any", BindingFlags.Public | BindingFlags.Static);
                if (anyMethod != null)
                {
                    return anyMethod.Invoke(null, new object[] { conditions });
                }

                object result = conditions[0];
                var orMethod = vfConditionType.GetMethod("Or", BindingFlags.Public | BindingFlags.Instance);
                for (int i = 1; i < conditions.Count; i++)
                {
                    result = orMethod.Invoke(result, new object[] { conditions[i] });
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParameterToggle] Error building condition: {ex.Message}", data);
                return null;
            }
        }

        private object BuildGroupCondition(ParameterConditionGroup group, object fx)
        {
            try
            {
                var vfConditionType = Type.GetType("VF.Utils.Controller.VFCondition, VRCFury-Editor");
                if (vfConditionType == null) return null;

                var conditions = new List<object>();

                foreach (var condition in group.conditions)
                {
                    if (condition == null || string.IsNullOrEmpty(condition.parameterName)) continue;

                    var vfCondition = BuildSingleCondition(condition, fx);
                    if (vfCondition != null)
                    {
                        conditions.Add(vfCondition);
                    }
                }

                if (conditions.Count == 0) return null;
                if (conditions.Count == 1) return conditions[0];

                object result = conditions[0];
                var combineMethod = group.groupOperator == ConditionOperator.AND ? 
                    vfConditionType.GetMethod("And", BindingFlags.Public | BindingFlags.Instance) :
                    vfConditionType.GetMethod("Or", BindingFlags.Public | BindingFlags.Instance);

                for (int i = 1; i < conditions.Count; i++)
                {
                    result = combineMethod.Invoke(result, new object[] { conditions[i] });
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParameterToggle] Error building group condition: {ex.Message}", data);
                return null;
            }
        }

        private object BuildSingleCondition(ParameterCondition condition, object fx)
        {
            try
            {
                object param = null;

                switch (condition.parameterType)
                {
                    case ToggleParameterType.Bool:
                        param = GetParameter(fx, "NewBool", condition.parameterName, typeof(bool), false);
                        break;
                    case ToggleParameterType.Int:
                        param = GetParameter(fx, "NewInt", condition.parameterName, typeof(int), 0);
                        break;
                    case ToggleParameterType.Float:
                        param = GetParameter(fx, "NewFloat", condition.parameterName, typeof(float), 0f);
                        break;
                }

                if (param == null) return null;

                var vfConditionType = Type.GetType("VF.Utils.Controller.VFCondition, VRCFury-Editor");
                if (vfConditionType == null) return null;

                switch (condition.conditionMode)
                {
                    case ConditionMode.If:
                        if (condition.parameterType == ToggleParameterType.Bool)
                        {
                            var isTrueMethod = param.GetType().GetMethod("IsTrue", BindingFlags.Public | BindingFlags.Instance);
                            return isTrueMethod?.Invoke(param, null);
                        }
                        break;
                    case ConditionMode.IfNot:
                        if (condition.parameterType == ToggleParameterType.Bool)
                        {
                            var isFalseMethod = param.GetType().GetMethod("IsFalse", BindingFlags.Public | BindingFlags.Instance);
                            return isFalseMethod?.Invoke(param, null);
                        }
                        break;
                    case ConditionMode.Equals:
                        var isEqualToMethod = param.GetType().GetMethod("IsEqualTo", BindingFlags.Public | BindingFlags.Instance);
                        return isEqualToMethod?.Invoke(param, new object[] { condition.threshold });
                    case ConditionMode.NotEqual:
                        var isNotEqualToMethod = param.GetType().GetMethod("IsNotEqualTo", BindingFlags.Public | BindingFlags.Instance);
                        return isNotEqualToMethod?.Invoke(param, new object[] { condition.threshold });
                    case ConditionMode.Greater:
                        var isGreaterThanMethod = param.GetType().GetMethod("IsGreaterThan", BindingFlags.Public | BindingFlags.Instance);
                        return isGreaterThanMethod?.Invoke(param, new object[] { condition.threshold });
                    case ConditionMode.Less:
                        var isLessThanMethod = param.GetType().GetMethod("IsLessThan", BindingFlags.Public | BindingFlags.Instance);
                        return isLessThanMethod?.Invoke(param, new object[] { condition.threshold });
                    case ConditionMode.GreaterOrEqual:
                        var isLessThanMethod2 = param.GetType().GetMethod("IsLessThan", BindingFlags.Public | BindingFlags.Instance);
                        var condition2 = isLessThanMethod2?.Invoke(param, new object[] { condition.threshold });
                        var notMethod = vfConditionType.GetMethod("Not", BindingFlags.Public | BindingFlags.Instance);
                        return notMethod?.Invoke(condition2, null);
                    case ConditionMode.LessOrEqual:
                        var isGreaterThanMethod2 = param.GetType().GetMethod("IsGreaterThan", BindingFlags.Public | BindingFlags.Instance);
                        var condition3 = isGreaterThanMethod2?.Invoke(param, new object[] { condition.threshold });
                        var notMethod2 = vfConditionType.GetMethod("Not", BindingFlags.Public | BindingFlags.Instance);
                        return notMethod2?.Invoke(condition3, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParameterToggle] Error building single condition for parameter '{condition.parameterName}': {ex.Message}", data);
            }
            return null;
        }

        private object GetParameter(object fx, string methodName, string paramName, Type paramType, object defaultValue)
        {
            try
            {
                var method = fx.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return null;

                var parameters = method.GetParameters();
                if (parameters.Length == 0) return null;

                if (parameters[0].ParameterType == typeof(string))
                {
                    return method.Invoke(fx, new object[] { paramName, defaultValue });
                }
                else
                {
                    return method.Invoke(fx, new object[] { paramName });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParameterToggle] Error getting parameter '{paramName}': {ex.Message}", data);
                return null;
            }
        }

        private object GetFXController()
        {
            try
            {
                var vrcfuryType = Type.GetType("VF.Component.VRCFury, VRCFury");
                if (vrcfuryType == null) return null;

                var vrcfuryComponent = data.gameObject.GetComponent(vrcfuryType);
                if (vrcfuryComponent == null) return null;

                var avatarRoot = data.transform.root;
                var globalsServiceType = Type.GetType("VF.Service.GlobalsService, VRCFury-Editor");
                if (globalsServiceType == null) return null;

                var getInstanceMethod = globalsServiceType.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static);
                if (getInstanceMethod == null) return null;

                var globalsService = getInstanceMethod.Invoke(null, new object[] { avatarRoot });
                if (globalsService == null) return null;

                var controllersServiceType = Type.GetType("VF.Service.ControllersService, VRCFury-Editor");
                if (controllersServiceType == null) return null;

                var getControllersServiceMethod = globalsServiceType.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance);
                if (getControllersServiceMethod == null) return null;

                var controllersService = getControllersServiceMethod.MakeGenericMethod(controllersServiceType).Invoke(globalsService, null);
                if (controllersService == null) return null;

                var getFxMethod = controllersServiceType.GetMethod("GetFx", BindingFlags.Public | BindingFlags.Instance);
                return getFxMethod?.Invoke(controllersService, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ParameterToggle] Could not get FX controller via VRCFury services: {ex.Message}. This is expected if VRCFury hasn't initialized yet.", data);
                return null;
            }
        }

        private object GetToggleParameter()
        {
            try
            {
                var modelType = toggleModel.GetType();
                var nameField = modelType.GetField("name", BindingFlags.Public | BindingFlags.Instance);
                var useGlobalParamField = modelType.GetField("useGlobalParam", BindingFlags.Public | BindingFlags.Instance);
                var globalParamField = modelType.GetField("globalParam", BindingFlags.Public | BindingFlags.Instance);
                var paramOverrideField = modelType.GetField("paramOverride", BindingFlags.Public | BindingFlags.Instance);

                string paramName = null;
                if (paramOverrideField != null && paramOverrideField.GetValue(toggleModel) != null)
                {
                    paramName = paramOverrideField.GetValue(toggleModel) as string;
                }
                else if (useGlobalParamField != null && (bool)useGlobalParamField.GetValue(toggleModel))
                {
                    paramName = globalParamField?.GetValue(toggleModel) as string;
                }
                else
                {
                    paramName = nameField?.GetValue(toggleModel) as string;
                }

                if (string.IsNullOrEmpty(paramName))
                {
                    paramName = $"ParameterToggle_{data.GetInstanceID()}";
                }

                var fx = GetFXController();
                if (fx == null) return null;

                var sliderField = modelType.GetField("slider", BindingFlags.Public | BindingFlags.Instance);
                bool isSlider = sliderField != null && (bool)sliderField.GetValue(toggleModel);

                if (isSlider)
                {
                    return GetParameter(fx, "NewFloat", paramName, typeof(float), 0f);
                }
                else
                {
                    return GetParameter(fx, "NewBool", paramName, typeof(bool), false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParameterToggle] Error getting toggle parameter: {ex.Message}", data);
                return null;
            }
        }

        private void CreateDriverLayer(object condition, object toggleParam)
        {
            try
            {
                var fx = GetFXController();
                if (fx == null) return;

                var newLayerMethod = fx.GetType().GetMethod("NewLayer", BindingFlags.Public | BindingFlags.Instance);
                if (newLayerMethod == null) return;

                var layerName = $"ParameterToggle_{data.name}_{data.GetInstanceID()}";
                var layer = newLayerMethod.Invoke(fx, new object[] { layerName });

                var newStateMethod = layer.GetType().GetMethod("NewState", BindingFlags.Public | BindingFlags.Instance);
                if (newStateMethod == null) return;

                var offState = newStateMethod.Invoke(layer, new object[] { "Off" });
                var onState = newStateMethod.Invoke(layer, new object[] { "On" });

                var drivesMethod = typeof(ParameterDriverLayerBuilder).GetMethod("Drives", BindingFlags.NonPublic | BindingFlags.Static);
                if (drivesMethod == null) return;

                var transitionsToMethod = offState.GetType().GetMethod("TransitionsTo", BindingFlags.Public | BindingFlags.Instance);
                if (transitionsToMethod == null) return;

                var whenMethod = typeof(ParameterDriverLayerBuilder).GetMethod("When", BindingFlags.NonPublic | BindingFlags.Static);
                if (whenMethod == null) return;

                var transition = transitionsToMethod.Invoke(offState, new object[] { onState });
                whenMethod.Invoke(null, new object[] { transition, condition });

                var transitionsToExitMethod = onState.GetType().GetMethod("TransitionsToExit", BindingFlags.Public | BindingFlags.Instance);
                if (transitionsToExitMethod != null)
                {
                    var exitTransition = transitionsToExitMethod.Invoke(onState, null);
                    var notMethod = condition.GetType().GetMethod("Not", BindingFlags.Public | BindingFlags.Instance);
                    if (notMethod != null)
                    {
                        var notCondition = notMethod.Invoke(condition, null);
                        whenMethod.Invoke(null, new object[] { exitTransition, notCondition });
                    }
                }

                Drives(offState, toggleParam, false);
                Drives(onState, toggleParam, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParameterToggle] Error creating driver layer: {ex.Message}\n{ex.StackTrace}", data);
            }
        }

        private static void Drives(object state, object param, bool value)
        {
            try
            {
                var drivesMethod = state.GetType().GetMethod("Drives", BindingFlags.Public | BindingFlags.Instance);
                if (drivesMethod == null) return;

                var paramType = param.GetType();
                if (paramType.Name.Contains("Bool"))
                {
                    drivesMethod.MakeGenericMethod(typeof(bool)).Invoke(state, new object[] { param, value });
                }
                else if (paramType.Name.Contains("Float"))
                {
                    drivesMethod.MakeGenericMethod(typeof(float)).Invoke(state, new object[] { param, value ? 1f : 0f });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ParameterToggle] Error driving parameter: {ex.Message}");
            }
        }

        private static void When(object transition, object condition)
        {
            try
            {
                var whenMethod = transition.GetType().GetMethod("When", BindingFlags.Public | BindingFlags.Instance);
                if (whenMethod == null) return;

                whenMethod.Invoke(transition, new object[] { condition });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ParameterToggle] Error setting transition condition: {ex.Message}");
            }
        }
    }
}

