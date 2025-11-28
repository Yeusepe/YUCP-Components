using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using VRLabs.CustomObjectSyncCreator;
using static VRLabs.CustomObjectSyncCreator.ControllerGenerationMethods;

namespace YUCP.Components.Editor
{
    public class FollowerProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 205;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<FollowerData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Follower] Prefab not found at Resources/YUCP.Follower/Follower.prefab.");
                return false;
            }

            var fxController = LoadFXController();
            if (fxController == null)
            {
                Debug.LogError("[YUCP Follower] FX Controller not found. Please ensure the controller is in the package.");
                return false;
            }

            var members = new List<GroupMember>();
            var hasErrors = false;

            foreach (var component in components)
            {
                var settings = component.ToSettings();
                if (!ValidateTarget(descriptor, component, settings))
                {
                    component.SetBuildSummary("Build failed");
                    hasErrors = true;
                    continue;
                }

                string effectiveGroupId = settings.enableGrouping
                    ? settings.followerGroupId
                    : GetIsolatedGroupId(settings, descriptor);

                members.Add(new GroupMember(component, settings, effectiveGroupId, !settings.enableGrouping));
            }

            if (members.Count == 0)
            {
                return !hasErrors;
            }

            WarnAboutDivergentGroups(members);

            var groups = members.GroupBy(m => new GroupKey(m.Settings, m.GroupId, m.IsIsolated));
            foreach (var group in groups)
            {
                if (!BuildGroup(descriptor, prefab, fxController, group.Key, group.ToList()))
                {
                    hasErrors = true;
                }
            }

            return !hasErrors;
        }

        private static GameObject LoadPrefab()
        {
            return UnityEngine.Resources.Load<GameObject>("YUCP.Follower/Follower");
        }

        private static AnimatorController LoadFXController()
        {
            return UnityEngine.Resources.Load<AnimatorController>("YUCP.Follower/Follower FX");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, FollowerData component, FollowerData.Settings settings)
        {
            if (settings.targetObject == null)
            {
                Debug.LogError("[YUCP Follower] Target object reference is missing.", component);
                return false;
            }

            if (!settings.targetObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Follower] Target object must be inside the avatar descriptor hierarchy.", component);
                return false;
            }

            return true;
        }

        private static void WarnAboutDivergentGroups(IEnumerable<GroupMember> members)
        {
            var byGroupId = members.Where(m => !m.IsIsolated).GroupBy(m => m.GroupId);
            foreach (var group in byGroupId)
            {
                var signatures = new HashSet<GroupSettingsSignature>(group.Select(m => new GroupSettingsSignature(m.Settings)));
                if (signatures.Count > 1)
                {
                    Debug.LogWarning($"[YUCP Follower] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
                }
            }
        }

        private static bool BuildGroup(VRCAvatarDescriptor descriptor, GameObject prefab, AnimatorController sourceController, GroupKey key, List<GroupMember> members)
        {
            var targets = members.Select(m => m.Settings.targetObject).ToArray();
            if (targets.Length == 0)
            {
                return true;
            }

            if (targets.Distinct().Count() != targets.Length)
            {
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.FollowerGroupId}\"";
                Debug.LogError($"[YUCP Follower] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }

            if (sourceController == null)
            {
                Debug.LogError("[YUCP Follower] Source FX controller is null.");
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }

            try
            {
                descriptor.customizeAnimationLayers = true;
                var existingController = descriptor.baseAnimationLayers
                    .Where(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX)
                    .Select(x => x.animatorController)
                    .FirstOrDefault();

                System.IO.Directory.CreateDirectory("Assets/VRLabs/GeneratedAssets/Follower/Animators/");
                string uniqueControllerPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRLabs/GeneratedAssets/Follower/Animators/Follower.controller");
                
                string sourcePath = AssetDatabase.GetAssetPath(sourceController);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    Debug.LogError("[YUCP Follower] Source controller is not a saved asset. Cannot copy.");
                    foreach (var member in members)
                    {
                        member.Component.SetBuildSummary("Build failed");
                    }
                    return false;
                }

                AnimatorController mergedController;
                
                if (existingController == null)
                {
                    AssetDatabase.CopyAsset(sourcePath, uniqueControllerPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    mergedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(uniqueControllerPath);
                    
                    if (mergedController == null)
                    {
                        Debug.LogError("[YUCP Follower] Failed to create controller.");
                        foreach (var member in members)
                        {
                            member.Component.SetBuildSummary("Build failed");
                        }
                        return false;
                    }
                    
                    ControllerGenerationMethods.SerializeController(mergedController);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
                else
                {
                    string existingPath = AssetDatabase.GetAssetPath(existingController);
                    AssetDatabase.CopyAsset(existingPath, uniqueControllerPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    mergedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(uniqueControllerPath);

                    if (mergedController == null)
                    {
                        Debug.LogError("[YUCP Follower] Failed to create controller.");
                        foreach (var member in members)
                        {
                            member.Component.SetBuildSummary("Build failed");
                        }
                        return false;
                    }

                    string tempSourcePath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRLabs/GeneratedAssets/Follower/Animators/Follower_Source_Temp.controller");
                    AssetDatabase.CopyAsset(sourcePath, tempSourcePath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    var tempSourceController = AssetDatabase.LoadAssetAtPath<AnimatorController>(tempSourcePath);

                    foreach (var layer in tempSourceController.layers)
                    {
                        if (mergedController.layers.Any(l => l.name == layer.name))
                        {
                            continue;
                        }

                        var newStateMachine = new AnimatorStateMachine
                        {
                            name = layer.stateMachine.name,
                            anyStatePosition = layer.stateMachine.anyStatePosition,
                            entryPosition = layer.stateMachine.entryPosition,
                            exitPosition = layer.stateMachine.exitPosition,
                            parentStateMachinePosition = layer.stateMachine.parentStateMachinePosition
                        };
                        AssetDatabase.AddObjectToAsset(newStateMachine, mergedController);
                        newStateMachine.hideFlags = HideFlags.HideInHierarchy;

                        var newLayer = new AnimatorControllerLayer
                        {
                            name = layer.name,
                            defaultWeight = layer.defaultWeight,
                            avatarMask = layer.avatarMask,
                            blendingMode = layer.blendingMode,
                            syncedLayerAffectsTiming = layer.syncedLayerAffectsTiming,
                            syncedLayerIndex = layer.syncedLayerIndex,
                            stateMachine = newStateMachine
                        };
                        mergedController.AddLayer(newLayer);

                        CopyStateMachineContents(layer.stateMachine, newStateMachine, mergedController);
                        ControllerGenerationMethods.SerializeStateMachine(mergedController, newStateMachine);
                    }

                    foreach (var param in tempSourceController.parameters)
                    {
                        if (mergedController.parameters.All(p => p.name != param.name))
                        {
                            mergedController.AddParameter(param);
                        }
                    }

                    AssetDatabase.DeleteAsset(tempSourcePath);
                    ControllerGenerationMethods.SerializeController(mergedController);
                }

                var fxLayer = descriptor.baseAnimationLayers.FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
                fxLayer.isDefault = false;
                fxLayer.animatorController = mergedController;
                var layers = descriptor.baseAnimationLayers.ToList();
                var fxIndex = layers.FindIndex(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
                layers[fxIndex] = fxLayer;
                descriptor.baseAnimationLayers = layers.ToArray();

                foreach (var member in members)
                {
                    var settings = member.Settings;
                    InstallSystem(descriptor, prefab, settings);
                }

                var summaryLabel = key.IsIsolated
                    ? "Follower (isolated)"
                    : $"Follower group \"{key.FollowerGroupId}\"";
                var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary(summary);
                }

                if (key.VerboseLogging)
                {
                    Debug.Log($"[YUCP Follower] Generated group \"{key.FollowerGroupId}\" with {targets.Length} object(s).");
                }

                if (key.IncludeCredits)
                {
                    Debug.Log("[YUCP Follower] Built using VRLabs Follower (MIT). Please credit VRLabs when sharing your avatar.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Follower] Failed to generate follower system: {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
        }

        private static void CopyStateMachineContents(AnimatorStateMachine source, AnimatorStateMachine dest, AnimatorController targetController)
        {
            var stateMapping = new Dictionary<AnimatorState, AnimatorState>();
            var stateMachineMapping = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
            stateMachineMapping[source] = dest;

            foreach (var childState in source.states)
            {
                var newState = CloneState(targetController, childState.state, stateMapping);
                dest.AddState(newState, childState.position);
                if (source.defaultState == childState.state)
                {
                    dest.defaultState = newState;
                }
            }

            foreach (var childStateMachine in source.stateMachines)
            {
                var newChild = new AnimatorStateMachine
                {
                    name = childStateMachine.stateMachine.name,
                    anyStatePosition = childStateMachine.stateMachine.anyStatePosition,
                    entryPosition = childStateMachine.stateMachine.entryPosition,
                    exitPosition = childStateMachine.stateMachine.exitPosition,
                    parentStateMachinePosition = childStateMachine.stateMachine.parentStateMachinePosition
                };
                AssetDatabase.AddObjectToAsset(newChild, targetController);
                newChild.hideFlags = HideFlags.HideInHierarchy;
                stateMachineMapping[childStateMachine.stateMachine] = newChild;
                dest.AddStateMachine(newChild, childStateMachine.position);
                CopyStateMachineContents(childStateMachine.stateMachine, newChild, targetController);
            }

            foreach (var transition in source.entryTransitions)
            {
                AnimatorTransition newTransition = null;
                if (transition.destinationState != null && stateMapping.TryGetValue(transition.destinationState, out var destState))
                {
                    newTransition = dest.AddEntryTransition(destState);
                }
                else if (transition.destinationStateMachine != null && stateMachineMapping.TryGetValue(transition.destinationStateMachine, out var destStateMachine))
                {
                    newTransition = dest.AddEntryTransition(destStateMachine);
                }
                
                if (newTransition != null)
                {
                    CopyTransitionProperties(newTransition, transition, targetController);
                }
            }

            foreach (var transition in source.anyStateTransitions)
            {
                AnimatorStateTransition newTransition = null;
                if (transition.destinationState != null && stateMapping.TryGetValue(transition.destinationState, out var destState))
                {
                    newTransition = dest.AddAnyStateTransition(destState);
                }
                else if (transition.destinationStateMachine != null && stateMachineMapping.TryGetValue(transition.destinationStateMachine, out var destStateMachine))
                {
                    newTransition = dest.AddAnyStateTransition(destStateMachine);
                }
                
                if (newTransition != null)
                {
                    CopyTransitionProperties(newTransition, transition, targetController);
                }
            }

            foreach (var state in source.states)
            {
                var newState = stateMapping[state.state];
                foreach (var transition in state.state.transitions)
                {
                    AnimatorStateTransition newTransition = null;
                    if (transition.destinationState != null && stateMapping.TryGetValue(transition.destinationState, out var destState))
                    {
                        newTransition = newState.AddTransition(destState);
                    }
                    else if (transition.destinationStateMachine != null && stateMachineMapping.TryGetValue(transition.destinationStateMachine, out var destStateMachine))
                    {
                        newTransition = newState.AddTransition(destStateMachine);
                    }
                    
                    if (newTransition != null)
                    {
                        CopyTransitionProperties(newTransition, transition, targetController);
                    }
                }
            }

            foreach (var behaviour in source.behaviours)
            {
                var newBehaviour = UnityEngine.Object.Instantiate(behaviour);
                AssetDatabase.AddObjectToAsset(newBehaviour, targetController);
                newBehaviour.hideFlags = HideFlags.HideInHierarchy;
                dest.behaviours = dest.behaviours.Append(newBehaviour).ToArray();
            }
        }

        private static AnimatorStateMachine CloneStateMachine(AnimatorController targetController, AnimatorStateMachine sourceStateMachine)
        {
            var newStateMachine = new AnimatorStateMachine
            {
                name = sourceStateMachine.name,
                anyStatePosition = sourceStateMachine.anyStatePosition,
                entryPosition = sourceStateMachine.entryPosition,
                exitPosition = sourceStateMachine.exitPosition,
                parentStateMachinePosition = sourceStateMachine.parentStateMachinePosition
            };

            AssetDatabase.AddObjectToAsset(newStateMachine, targetController);
            newStateMachine.hideFlags = HideFlags.HideInHierarchy;

            var stateMapping = new Dictionary<AnimatorState, AnimatorState>();
            var stateMachineMapping = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
            stateMachineMapping[sourceStateMachine] = newStateMachine;

            foreach (var childState in sourceStateMachine.states)
            {
                var newState = CloneState(targetController, childState.state, stateMapping);
                newStateMachine.AddState(newState, childState.position);
                if (sourceStateMachine.defaultState == childState.state)
                {
                    newStateMachine.defaultState = newState;
                }
            }

            foreach (var childStateMachine in sourceStateMachine.stateMachines)
            {
                var clonedChild = CloneStateMachine(targetController, childStateMachine.stateMachine);
                stateMachineMapping[childStateMachine.stateMachine] = clonedChild;
                newStateMachine.AddStateMachine(clonedChild, childStateMachine.position);
            }

            foreach (var transition in sourceStateMachine.entryTransitions)
            {
                AnimatorTransition newTransition = null;
                if (transition.destinationState != null && stateMapping.TryGetValue(transition.destinationState, out var destState))
                {
                    newTransition = newStateMachine.AddEntryTransition(destState);
                }
                else if (transition.destinationStateMachine != null && stateMachineMapping.TryGetValue(transition.destinationStateMachine, out var destStateMachine))
                {
                    newTransition = newStateMachine.AddEntryTransition(destStateMachine);
                }
                
                if (newTransition != null)
                {
                    CopyTransitionProperties(newTransition, transition, targetController);
                }
            }

            foreach (var transition in sourceStateMachine.anyStateTransitions)
            {
                AnimatorStateTransition newTransition = null;
                if (transition.destinationState != null && stateMapping.TryGetValue(transition.destinationState, out var destState))
                {
                    newTransition = newStateMachine.AddAnyStateTransition(destState);
                }
                else if (transition.destinationStateMachine != null && stateMachineMapping.TryGetValue(transition.destinationStateMachine, out var destStateMachine))
                {
                    newTransition = newStateMachine.AddAnyStateTransition(destStateMachine);
                }
                
                if (newTransition != null)
                {
                    CopyTransitionProperties(newTransition, transition, targetController);
                }
            }

            foreach (var state in sourceStateMachine.states)
            {
                var newState = stateMapping[state.state];
                foreach (var transition in state.state.transitions)
                {
                    AnimatorStateTransition newTransition = null;
                    if (transition.destinationState != null && stateMapping.TryGetValue(transition.destinationState, out var destState))
                    {
                        newTransition = newState.AddTransition(destState);
                    }
                    else if (transition.destinationStateMachine != null && stateMachineMapping.TryGetValue(transition.destinationStateMachine, out var destStateMachine))
                    {
                        newTransition = newState.AddTransition(destStateMachine);
                    }
                    
                    if (newTransition != null)
                    {
                        CopyTransitionProperties(newTransition, transition, targetController);
                    }
                }
            }

            foreach (var behaviour in sourceStateMachine.behaviours)
            {
                var newBehaviour = UnityEngine.Object.Instantiate(behaviour);
                AssetDatabase.AddObjectToAsset(newBehaviour, targetController);
                newBehaviour.hideFlags = HideFlags.HideInHierarchy;
                newStateMachine.behaviours = newStateMachine.behaviours.Append(newBehaviour).ToArray();
            }

            return newStateMachine;
        }

        private static AnimatorState CloneState(AnimatorController targetController, AnimatorState sourceState, Dictionary<AnimatorState, AnimatorState> stateMapping)
        {
            var newState = new AnimatorState
            {
                name = sourceState.name,
                speed = sourceState.speed,
                cycleOffset = sourceState.cycleOffset,
                iKOnFeet = sourceState.iKOnFeet,
                writeDefaultValues = sourceState.writeDefaultValues,
                mirror = sourceState.mirror,
                speedParameterActive = sourceState.speedParameterActive,
                mirrorParameterActive = sourceState.mirrorParameterActive,
                cycleOffsetParameterActive = sourceState.cycleOffsetParameterActive,
                timeParameterActive = sourceState.timeParameterActive,
                speedParameter = sourceState.speedParameter,
                mirrorParameter = sourceState.mirrorParameter,
                cycleOffsetParameter = sourceState.cycleOffsetParameter,
                timeParameter = sourceState.timeParameter,
                tag = sourceState.tag
            };

            if (sourceState.motion is BlendTree sourceTree)
            {
                newState.motion = CloneBlendTree(targetController, sourceTree);
            }
            else if (sourceState.motion != null)
            {
                newState.motion = sourceState.motion;
            }

            AssetDatabase.AddObjectToAsset(newState, targetController);
            newState.hideFlags = HideFlags.HideInHierarchy;
            stateMapping[sourceState] = newState;

            foreach (var behaviour in sourceState.behaviours)
            {
                var newBehaviour = UnityEngine.Object.Instantiate(behaviour);
                AssetDatabase.AddObjectToAsset(newBehaviour, targetController);
                newBehaviour.hideFlags = HideFlags.HideInHierarchy;
                newState.behaviours = newState.behaviours.Append(newBehaviour).ToArray();
            }

            return newState;
        }

        private static BlendTree CloneBlendTree(AnimatorController targetController, BlendTree sourceTree)
        {
            var newTree = new BlendTree
            {
                name = sourceTree.name,
                blendType = sourceTree.blendType,
                blendParameter = sourceTree.blendParameter,
                blendParameterY = sourceTree.blendParameterY,
                minThreshold = sourceTree.minThreshold,
                maxThreshold = sourceTree.maxThreshold,
                useAutomaticThresholds = sourceTree.useAutomaticThresholds
            };

            var children = new List<ChildMotion>();
            foreach (var child in sourceTree.children)
            {
                var newChild = new ChildMotion
                {
                    motion = child.motion is BlendTree ? CloneBlendTree(targetController, (BlendTree)child.motion) : child.motion,
                    threshold = child.threshold,
                    position = child.position,
                    timeScale = child.timeScale,
                    cycleOffset = child.cycleOffset,
                    directBlendParameter = child.directBlendParameter,
                    mirror = child.mirror
                };
                children.Add(newChild);
            }
            newTree.children = children.ToArray();

            AssetDatabase.AddObjectToAsset(newTree, targetController);
            newTree.hideFlags = HideFlags.HideInHierarchy;
            return newTree;
        }

        private static void CopyTransitionProperties(AnimatorStateTransition dest, AnimatorStateTransition source, AnimatorController targetController)
        {
            dest.name = source.name;
            dest.duration = source.duration;
            dest.offset = source.offset;
            dest.interruptionSource = source.interruptionSource;
            dest.orderedInterruption = source.orderedInterruption;
            dest.exitTime = source.exitTime;
            dest.hasExitTime = source.hasExitTime;
            dest.hasFixedDuration = source.hasFixedDuration;
            dest.canTransitionToSelf = source.canTransitionToSelf;

            foreach (var condition in source.conditions)
            {
                dest.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }

            AssetDatabase.AddObjectToAsset(dest, targetController);
            dest.hideFlags = HideFlags.HideInHierarchy;
        }

        private static void CopyTransitionProperties(AnimatorTransition dest, AnimatorTransition source, AnimatorController targetController)
        {
            dest.name = source.name;

            foreach (var condition in source.conditions)
            {
                dest.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }

            AssetDatabase.AddObjectToAsset(dest, targetController);
            dest.hideFlags = HideFlags.HideInHierarchy;
        }


        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, FollowerData.Settings settings)
        {
            var rootObject = descriptor.gameObject;
            var followerSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            followerSystem.name = followerSystem.name.Replace("(Clone)", "");

            var container = followerSystem.transform.Find("Container");
            if (container != null)
            {
                var cube = container.Find("Cube");
                if (cube != null)
                {
                    UnityEngine.Object.DestroyImmediate(cube.gameObject);
                }
            }

            var followerTarget = followerSystem.transform.Find("Follower Target");
            if (followerTarget != null && settings.followerTarget != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.followerTarget.transform, descriptor.transform);
                followerTarget.parent = settings.followerTarget.parent;
                followerTarget.localPosition = settings.followerTarget.localPosition;
                followerTarget.localRotation = settings.followerTarget.localRotation;
                followerTarget.localScale = settings.followerTarget.localScale;
                var newPath = AnimationUtility.CalculateTransformPath(followerTarget.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            if (settings.targetObject != null && container != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);
                settings.targetObject.transform.parent = container;
                var newPath = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }
        }

        private readonly struct GroupMember
        {
            public GroupMember(FollowerData component, FollowerData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public FollowerData Component { get; }
            public FollowerData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(FollowerData.Settings settings)
            {
                MenuLocation = settings.menuLocation;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private string MenuLocation { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return MenuLocation == other.MenuLocation &&
                       VerboseLogging == other.VerboseLogging &&
                       IncludeCredits == other.IncludeCredits;
            }

            public override bool Equals(object obj)
            {
                return obj is GroupSettingsSignature other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = MenuLocation != null ? MenuLocation.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(FollowerData.Settings settings, string groupId, bool isIsolated)
            {
                FollowerGroupId = groupId;
                IsIsolated = isIsolated;
                MenuLocation = settings.menuLocation;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string FollowerGroupId { get; }
            public bool IsIsolated { get; }
            public string MenuLocation { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return FollowerGroupId == other.FollowerGroupId &&
                       IsIsolated == other.IsIsolated &&
                       MenuLocation == other.MenuLocation &&
                       VerboseLogging == other.VerboseLogging &&
                       IncludeCredits == other.IncludeCredits;
            }

            public override bool Equals(object obj)
            {
                return obj is GroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = FollowerGroupId != null ? FollowerGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(FollowerData.Settings settings, VRCAvatarDescriptor descriptor)
        {
            if (settings.targetObject == null || descriptor == null)
            {
                return $"__Isolated__/{Guid.NewGuid()}";
            }

            string path = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);
            return $"__Isolated__/{path}";
        }
    }
}

