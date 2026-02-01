using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRLabs.CustomObjectSyncCreator;

namespace YUCP.Components.Editor
{
    public static class AnimationCloneUtility
    {
        private static readonly HashSet<string> IgnoredGlobalParameters = new HashSet<string>
        {
            "GestureLeft",
            "GestureRight",
            "IsLocal"
        };

        public static string BuildComponentRootName(string prefix, Transform target, Transform avatarRoot)
        {
            var key = GetStableTargetKey(target, avatarRoot);
            return string.IsNullOrEmpty(prefix) ? key : $"{prefix}_{key}";
        }

        public static string GetStableTargetKey(Transform target, Transform avatarRoot)
        {
            if (target == null || avatarRoot == null)
            {
                return "Target";
            }

            var path = AnimationUtility.CalculateTransformPath(target, avatarRoot);
            return SanitizeName(path);
        }

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Target";
            }

            var sanitized = name.Trim();
            sanitized = sanitized.Replace("/", "_").Replace("\\", "_");
            sanitized = sanitized.Replace(":", "_").Replace("*", "_").Replace("?", "_");
            sanitized = sanitized.Replace("\"", "_").Replace("<", "_").Replace(">", "_").Replace("|", "_");
            return sanitized;
        }

        public static bool IsIgnoredGlobalParameter(string name)
        {
            return string.IsNullOrEmpty(name) || IgnoredGlobalParameters.Contains(name);
        }

        public static string AppendSuffixIfMissing(string name, string suffix)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(suffix))
            {
                return name;
            }

            var needle = "/" + suffix;
            return name.EndsWith(needle, StringComparison.Ordinal) ? name : $"{name}/{suffix}";
        }

        public static Dictionary<string, string> RewriteParameters(
            AnimatorController controller,
            Func<string, string> rename)
        {
            var mapping = new Dictionary<string, string>();
            if (controller == null || rename == null)
            {
                return mapping;
            }

            foreach (var parameter in controller.parameters)
            {
                if (parameter == null) continue;
                var newName = rename(parameter.name);
                if (!string.IsNullOrEmpty(newName) && newName != parameter.name)
                {
                    mapping[parameter.name] = newName;
                }
            }

            if (mapping.Count == 0)
            {
                return mapping;
            }

            var vfController = VRCFuryHelper.TryCreateVFController(controller);
            if (vfController == null)
            {
                Debug.LogError("[YUCP AnimationCloneUtility] Failed to access VRCFury RewriteParameters. Parameter renames may be incomplete.");
                return mapping;
            }

            VRCFuryHelper.TryRewriteParameters(vfController, name => mapping.TryGetValue(name, out var renamed) ? renamed : name);
            return mapping;
        }

        private static void RemapAnimationClipPaths(AnimationClip clip, string oldRootName, string newRootName)
        {
            if (clip == null || string.IsNullOrEmpty(oldRootName) || string.IsNullOrEmpty(newRootName))
            {
                return;
            }

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var newPath = RemapPath(binding.path, oldRootName, newRootName);
                if (newPath == binding.path)
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var newPath = RemapPath(binding.path, oldRootName, newRootName);
                if (newPath == binding.path)
                {
                    continue;
                }

                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, curve);
            }
        }

        private static string RemapPath(string path, string oldRootName, string newRootName)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            if (path == oldRootName)
            {
                return newRootName;
            }

            var prefix = $"{oldRootName}/";
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return newRootName + path.Substring(oldRootName.Length);
            }

            return path;
        }

        public static AnimatorController CreateControllerAssetCloneWithRemappedRoot(
            AnimatorController source,
            string category,
            string oldRootName,
            string newRootName,
            string clipNamePrefix = null)
        {
            if (source == null)
            {
                return null;
            }

            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                return null;
            }

            var safeCategory = SanitizeName(category);
            var safeRoot = SanitizeName(newRootName);
            var controllerFolder = Path.Combine("Assets", "YUCP", "GeneratedAssets", safeCategory, "Controllers");
            Directory.CreateDirectory(controllerFolder);

            var assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(controllerFolder, $"{safeRoot}.controller"));
            if (!AssetDatabase.CopyAsset(sourcePath, assetPath))
            {
                Debug.LogError($"[YUCP AnimationCloneUtility] Failed to copy controller asset: {sourcePath}");
                return null;
            }

            AssetDatabase.ImportAsset(assetPath);
            var newController = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (newController == null)
            {
                Debug.LogError("[YUCP AnimationCloneUtility] Failed to load copied controller asset.");
                return null;
            }

            var motionMap = new Dictionary<Motion, Motion>();
            foreach (var layer in newController.layers)
            {
                if (layer.stateMachine == null)
                {
                    Debug.LogWarning($"[YUCP AnimationCloneUtility] Controller '{newController.name}': Statemachine for layer '{layer.name}' is missing.");
                    continue;
                }

                ReplaceMotionsInStateMachineWithClone(layer.stateMachine, newController, oldRootName, newRootName, clipNamePrefix, motionMap);
            }

            EditorUtility.SetDirty(newController);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);
            return newController;
        }

        private static void ReplaceMotionsInStateMachineWithClone(
            AnimatorStateMachine stateMachine,
            AnimatorController controller,
            string oldRootName,
            string newRootName,
            string clipNamePrefix,
            Dictionary<Motion, Motion> motionMap)
        {
            if (stateMachine == null)
            {
                return;
            }

            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null || state.motion == null)
                {
                    continue;
                }

                state.motion = CloneAndRemapMotion(state.motion, controller, oldRootName, newRootName, clipNamePrefix, motionMap);
            }

            foreach (var child in stateMachine.stateMachines)
            {
                ReplaceMotionsInStateMachineWithClone(child.stateMachine, controller, oldRootName, newRootName, clipNamePrefix, motionMap);
            }
        }

        private static Motion CloneAndRemapMotion(
            Motion motion,
            AnimatorController controller,
            string oldRootName,
            string newRootName,
            string clipNamePrefix,
            Dictionary<Motion, Motion> motionMap)
        {
            if (motion == null)
            {
                return null;
            }

            if (motionMap.TryGetValue(motion, out var existing))
            {
                return existing;
            }

            if (motion is AnimationClip clip)
            {
                var newClip = UnityEngine.Object.Instantiate(clip);
                if (!string.IsNullOrEmpty(clipNamePrefix) && !newClip.name.StartsWith(clipNamePrefix, StringComparison.Ordinal))
                {
                    newClip.name = $"{clipNamePrefix}_{newClip.name}";
                }

                RemapAnimationClipPaths(newClip, oldRootName, newRootName);
                AssetDatabase.AddObjectToAsset(newClip, controller);
                EditorUtility.SetDirty(newClip);
                motionMap[motion] = newClip;
                return newClip;
            }

            if (motion is BlendTree tree)
            {
                var newTree = UnityEngine.Object.Instantiate(tree);
                motionMap[motion] = newTree;
                AssetDatabase.AddObjectToAsset(newTree, controller);

                var children = newTree.children;
                for (var i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    if (child.motion != null)
                    {
                        child.motion = CloneAndRemapMotion(child.motion, controller, oldRootName, newRootName, clipNamePrefix, motionMap);
                        children[i] = child;
                    }
                }
                newTree.children = children;
                EditorUtility.SetDirty(newTree);
                return newTree;
            }

            return motion;
        }

    }
}
