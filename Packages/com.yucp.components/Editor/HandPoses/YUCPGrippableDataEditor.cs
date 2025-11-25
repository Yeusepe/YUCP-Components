#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.HandPoses;
using YUCP.Components.HandPoses.Editor;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(YUCPGrippableData))]
    [CanEditMultipleObjects]
    public class YUCPGrippableDataEditor : UnityEditor.Editor
    {
        private VisualElement root;
        private VisualElement blendPoseSection;
        private VisualElement handPoseInfo;
        private Label handPoseInfoLabel;
        private VisualElement generatedInfo;
        private Label generatedInfoLabel;

        public override VisualElement CreateInspectorGUI()
        {
            root = new VisualElement();
            root.AddToClassList("grippable-inspector");

            var header = YUCPHandPoseComponentHeader.CreateHeaderOverlay("Grippable Object");
            root.Add(header);

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/HandPoses/UI/GrippableInspector.uxml");
            
            if (visualTree == null)
            {
                visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/YUCP/Components/Editor/HandPoses/UI/GrippableInspector.uxml");
            }

            if (visualTree != null)
            {
                visualTree.CloneTree(root);
            }
            else
            {
                root.Add(new Label("Failed to load UI template. Using default inspector."));
                return root;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/HandPoses/UI/GrippableInspectorStyle.uss");
            
            if (styleSheet == null)
            {
                styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Assets/YUCP/Components/Editor/HandPoses/UI/GrippableInspectorStyle.uss");
            }

            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            SetupUI();
            UpdateUI();

            return root;
        }

        private void SetupUI()
        {
            var createButton = root.Q<Button>("CreateHandPoseButton");
            if (createButton != null)
            {
                createButton.clicked += OnCreateHandPose;
            }

            var editButton = root.Q<Button>("EditHandPoseButton");
            if (editButton != null)
            {
                editButton.clicked += OnEditHandPose;
            }

            handPoseInfo = root.Q<VisualElement>("HandPoseInfo");
            handPoseInfoLabel = root.Q<Label>("HandPoseInfoLabel");

            blendPoseSection = root.Q<VisualElement>("BlendPoseSection");
            generatedInfo = root.Q<VisualElement>("GeneratedInfo");
            generatedInfoLabel = root.Q<Label>("GeneratedInfoLabel");

            var handPoseAssetField = root.Q<PropertyField>("handPoseAsset");
            if (handPoseAssetField != null)
            {
                handPoseAssetField.RegisterValueChangeCallback(evt => UpdateUI());
            }
        }

        private void UpdateUI()
        {
            var data = (YUCPGrippableData)target;

            if (data.handPoseAsset != null)
            {
                if (handPoseInfo != null)
                {
                    handPoseInfo.style.display = DisplayStyle.Flex;
                    if (handPoseInfoLabel != null)
                    {
                        handPoseInfoLabel.text = $"Hand Pose: {data.handPoseAsset.name}\n" +
                                                 $"Type: {data.handPoseAsset.PoseType}";
                    }
                }

                if (blendPoseSection != null)
                {
                    blendPoseSection.style.display = data.handPoseAsset.PoseType == YUCPHandPoseType.Blend 
                        ? DisplayStyle.Flex 
                        : DisplayStyle.None;
                }
            }
            else
            {
                if (handPoseInfo != null)
                {
                    handPoseInfo.style.display = DisplayStyle.Flex;
                    if (handPoseInfoLabel != null)
                    {
                        handPoseInfoLabel.text = "No hand pose selected. Create or assign a hand pose asset.";
                    }
                }

                if (blendPoseSection != null)
                {
                    blendPoseSection.style.display = DisplayStyle.None;
                }
            }

            if (!string.IsNullOrEmpty(data.GeneratedGripInfo))
            {
                if (generatedInfo != null)
                {
                    generatedInfo.style.display = DisplayStyle.Flex;
                    if (generatedInfoLabel != null)
                    {
                        generatedInfoLabel.text = $"Generated: {data.GeneratedGripInfo}";
                    }
                }
            }
            else
            {
                if (generatedInfo != null)
                {
                    generatedInfo.style.display = DisplayStyle.None;
                }
            }
        }

        private void OnCreateHandPose()
        {
            var data = (YUCPGrippableData)target;
            
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Hand Pose Asset",
                "NewHandPose",
                "asset",
                "Create a new hand pose asset");

            if (string.IsNullOrEmpty(path)) return;

            var handPoseAsset = ScriptableObject.CreateInstance<YUCPHandPoseAsset>();
            handPoseAsset.PoseType = YUCPHandPoseType.Fixed;
            handPoseAsset.HandDescriptorLeft = new YUCPHandDescriptor();
            handPoseAsset.HandDescriptorRight = new YUCPHandDescriptor();
            handPoseAsset.Version = YUCPHandPoseAsset.CurrentVersion;

            AssetDatabase.CreateAsset(handPoseAsset, path);
            AssetDatabase.SaveAssets();

            data.handPoseAsset = handPoseAsset;
            EditorUtility.SetDirty(data);
            
            UpdateUI();
        }

        private void OnEditHandPose()
        {
            var data = (YUCPGrippableData)target;
            
            if (data.handPoseAsset == null)
            {
                EditorUtility.DisplayDialog(
                    "No Hand Pose Selected",
                    "Please assign a hand pose asset first, or create a new one.",
                    "OK");
                return;
            }

            Selection.activeObject = data.handPoseAsset;
            EditorGUIUtility.PingObject(data.handPoseAsset);
        }
    }
}
#endif

