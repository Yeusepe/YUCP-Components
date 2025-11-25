// Portions adapted from UltimateXR (MIT License) by VRMADA

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.HandPoses.Editor
{
    internal sealed class YUCPHandPosePreset
    {
        public string Name { get; }
        public YUCPHandPoseAsset Pose { get; }
        public Texture2D Thumbnail { get; }

        public YUCPHandPosePreset(string name, YUCPHandPoseAsset pose, Texture2D thumbnail)
        {
            Name = name;
            Pose = pose;
            Thumbnail = thumbnail;
        }
    }

    internal static class YUCPHandPosePresetLibrary
    {
        private const string PresetFolder = "Packages/com.yucp.components/Editor/HandPoses/Presets";
        private const string DefaultPresetName = "Neutral";

        public static List<YUCPHandPosePreset> LoadPresets()
        {
            EnsurePresetFolder();
            EnsureDefaultPreset();

            List<YUCPHandPosePreset> presets = new List<YUCPHandPosePreset>();
            string[] poseGuids = AssetDatabase.FindAssets("t:YUCPHandPoseAsset", new[] { PresetFolder });

            foreach (string guid in poseGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pose = AssetDatabase.LoadAssetAtPath<YUCPHandPoseAsset>(path);
                if (pose == null)
                {
                    continue;
                }

                Texture2D thumbnail = null;
                string thumbnailPath = Path.ChangeExtension(path, ".png");
                if (File.Exists(thumbnailPath))
                {
                    thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(thumbnailPath);
                }

                presets.Add(new YUCPHandPosePreset(Path.GetFileNameWithoutExtension(path), pose, thumbnail));
            }

            return presets;
        }

        private static void EnsurePresetFolder()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath = Path.Combine(projectRoot, PresetFolder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                AssetDatabase.Refresh();
            }
        }

        private static void EnsureDefaultPreset()
        {
            string assetPath = Path.Combine(PresetFolder, DefaultPresetName + ".asset");
            assetPath = assetPath.Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<YUCPHandPoseAsset>(assetPath) != null)
            {
                return;
            }

            var pose = ScriptableObject.CreateInstance<YUCPHandPoseAsset>();
            pose.PoseType = YUCPHandPoseType.Fixed;
            pose.HandDescriptorLeft = new YUCPHandDescriptor();
            pose.HandDescriptorRight = new YUCPHandDescriptor();
            pose.Version = YUCPHandPoseAsset.CurrentVersion;

            AssetDatabase.CreateAsset(pose, assetPath);
            AssetDatabase.SaveAssets();
        }
    }
}
