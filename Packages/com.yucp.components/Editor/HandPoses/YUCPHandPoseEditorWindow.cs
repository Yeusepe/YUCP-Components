#if UNITY_EDITOR
// Portions adapted from UltimateXR (MIT License) by VRMADA
// Based on UxrHandPoseEditorWindow functionality, adapted for VRChat avatars and UI Toolkit.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using VRC.SDK3.Avatars.Components;

using EditorObjectField = UnityEditor.UIElements.ObjectField;
using EditorEnumField = UnityEngine.UIElements.EnumField;

namespace YUCP.Components.HandPoses.Editor
{
    public class YUCPHandPoseEditorWindow : EditorWindow
    {
        private const string UxmlPath = "Packages/com.yucp.components/Editor/HandPoses/UI/YUCPHandPoseEditor.uxml";
        private const string StylePath = "Packages/com.yucp.components/Editor/HandPoses/UI/YUCPHandPoseStyle.uss";

        private EditorObjectField _avatarField;
        private Button _refreshAvatarButton;
        private Button _createPoseButton;
        private Button _duplicatePoseButton;
        private Button _deletePoseButton;
        private ListView _poseListView;
        private ListView _presetListView;
        private Button _applyPresetButton;
        private Button _importPresetsButton;
        private TextField _poseNameField;
        private EditorEnumField _poseTypeField;
        private Slider _blendSlider;
        private Label _blendLabel;
        private Button _captureOpenLeftButton;
        private Button _captureClosedLeftButton;
        private Button _captureOpenRightButton;
        private Button _captureClosedRightButton;
        private Button _mirrorLeftToRightButton;
        private Button _mirrorRightToLeftButton;
        private VisualElement _fingerControlsContainer;
        private VisualElement _fingerSection;
        private Button _handLeftButton;
        private Button _handRightButton;
        private IMGUIContainer _previewIMGUI;
        private Label _avatarInfoLabel;

        private readonly List<YUCPHandPoseAsset> _poseAssets = new List<YUCPHandPoseAsset>();
        private readonly List<YUCPHandPosePreset> _presetAssets = new List<YUCPHandPosePreset>();

        private VRCAvatarDescriptor _avatarDescriptor;
        private Animator _targetAnimator;
        private YUCPHandPoseAsset _currentPose;
        private YUCPHandPoseEditingSession _session;
        private YUCPHandSide _selectedHand = YUCPHandSide.Left;
        private float _previewBlend;

        [MenuItem("Tools/YUCP/Hand Pose Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<YUCPHandPoseEditorWindow>("Hand Pose Editor");
            window.minSize = new Vector2(960, 620);
        }

        private void OnEnable()
        {
            if (Selection.activeObject is YUCPHandPoseAsset asset)
            {
                _currentPose = asset;
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is YUCPHandPoseAsset asset)
            {
                _currentPose = asset;
                RefreshPoseSelection();
            }
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();

            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                Debug.LogError("Hand Pose Editor UXML not found.");
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            CacheUIReferences();
            SetupHandToggleButtons();
            RegisterCallbacks();

            LoadPoseAssets();
            LoadPresetAssets();
            RefreshAvatarInfo();
            RefreshPoseSelection();
            RefreshFingerControls();
            RefreshPreview();
        }

        private void CacheUIReferences()
        {
            _avatarField = rootVisualElement.Q<EditorObjectField>("avatarField");
            _refreshAvatarButton = rootVisualElement.Q<Button>("refreshAvatarButton");
            _avatarInfoLabel = rootVisualElement.Q<Label>("avatarInfoLabel");

            _poseListView = rootVisualElement.Q<ListView>("poseList");
            _presetListView = rootVisualElement.Q<ListView>("presetList");
            _applyPresetButton = rootVisualElement.Q<Button>("applyPresetButton");
            _importPresetsButton = rootVisualElement.Q<Button>("importPresetsButton");

            _createPoseButton = rootVisualElement.Q<Button>("createPoseButton");
            _duplicatePoseButton = rootVisualElement.Q<Button>("duplicatePoseButton");
            _deletePoseButton = rootVisualElement.Q<Button>("deletePoseButton");

            _poseNameField = rootVisualElement.Q<TextField>("poseNameField");
            _poseTypeField = rootVisualElement.Q<EditorEnumField>("poseTypeField");
            _blendSlider = rootVisualElement.Q<Slider>("blendSlider");
            _blendLabel = rootVisualElement.Q<Label>("blendLabel");

            _captureOpenLeftButton = rootVisualElement.Q<Button>("captureOpenLeftButton");
            _captureClosedLeftButton = rootVisualElement.Q<Button>("captureClosedLeftButton");
            _captureOpenRightButton = rootVisualElement.Q<Button>("captureOpenRightButton");
            _captureClosedRightButton = rootVisualElement.Q<Button>("captureClosedRightButton");
            _mirrorLeftToRightButton = rootVisualElement.Q<Button>("mirrorLeftToRightButton");
            _mirrorRightToLeftButton = rootVisualElement.Q<Button>("mirrorRightToLeftButton");

            _fingerControlsContainer = rootVisualElement.Q<VisualElement>("fingerControlsContainer");
            _fingerSection = rootVisualElement.Q<VisualElement>("fingerControlsSection");
            _previewIMGUI = rootVisualElement.Q<IMGUIContainer>("previewIMGUI");

            if (_avatarField == null ||
                _refreshAvatarButton == null ||
                _poseListView == null ||
                _presetListView == null ||
                _poseNameField == null ||
                _poseTypeField == null ||
                _blendSlider == null ||
                _fingerSection == null ||
                _fingerControlsContainer == null ||
                _previewIMGUI == null)
            {
                throw new NullReferenceException("Hand Pose Editor UI template missing required elements.");
            }
        }

        private void SetupHandToggleButtons()
        {
            var handRow = new VisualElement();
            handRow.AddToClassList("button-row");
            _handLeftButton = new Button(() => SwitchHand(YUCPHandSide.Left)) { text = "Edit Left" };
            _handRightButton = new Button(() => SwitchHand(YUCPHandSide.Right)) { text = "Edit Right" };
            _handLeftButton.AddToClassList("secondary-button");
            _handRightButton.AddToClassList("secondary-button");
            handRow.Add(_handLeftButton);
            handRow.Add(_handRightButton);
            _fingerSection.Insert(0, handRow);
            UpdateHandToggleState();
        }

        private void RegisterCallbacks()
        {
            if (_avatarField != null)
            {
                _avatarField.RegisterValueChangedCallback(evt =>
                {
                    ResolveAvatar(evt.newValue as UnityEngine.Object);
                    RefreshAvatarInfo();
                    RefreshFingerControls();
                    RefreshPreview();
                });
            }

            if (_refreshAvatarButton != null)
            {
                _refreshAvatarButton.clicked += () =>
                {
                    ResolveAvatar(_avatarField != null ? _avatarField.value : null);
                    RefreshAvatarInfo();
                    RefreshFingerControls();
                    RefreshPreview();
                };
            }

            _createPoseButton?.RegisterCallback<ClickEvent>(_ => CreatePose());
            _duplicatePoseButton?.RegisterCallback<ClickEvent>(_ => DuplicatePose());
            _deletePoseButton?.RegisterCallback<ClickEvent>(_ => DeletePose());

            _poseNameField?.RegisterValueChangedCallback(evt => RenamePose(evt.newValue));
            if (_poseTypeField != null)
            {
                _poseTypeField.RegisterValueChangedCallback(evt => ChangePoseType((YUCPHandPoseType)evt.newValue));
            }

            if (_blendSlider != null)
            {
                _blendSlider.lowValue = 0f;
                _blendSlider.highValue = 1f;
                _blendSlider.value = 0f;
                _blendSlider.RegisterValueChangedCallback(evt =>
                {
                    _previewBlend = evt.newValue;
                    if (_blendLabel != null)
                    {
                        _blendLabel.text = $"Blend value: {evt.newValue:F2}";
                    }
                    RefreshPreview();
                });
            }

            _captureOpenLeftButton?.RegisterCallback<ClickEvent>(_ => CaptureHand(YUCPHandSide.Left, false));
            _captureClosedLeftButton?.RegisterCallback<ClickEvent>(_ => CaptureHand(YUCPHandSide.Left, true));
            _captureOpenRightButton?.RegisterCallback<ClickEvent>(_ => CaptureHand(YUCPHandSide.Right, false));
            _captureClosedRightButton?.RegisterCallback<ClickEvent>(_ => CaptureHand(YUCPHandSide.Right, true));
            _mirrorLeftToRightButton?.RegisterCallback<ClickEvent>(_ => MirrorHand(YUCPHandSide.Left, YUCPHandSide.Right));
            _mirrorRightToLeftButton?.RegisterCallback<ClickEvent>(_ => MirrorHand(YUCPHandSide.Right, YUCPHandSide.Left));

            _applyPresetButton?.RegisterCallback<ClickEvent>(_ => ApplySelectedPreset());
            _importPresetsButton?.RegisterCallback<ClickEvent>(_ =>
            {
                LoadPresetAssets(true);
                RefreshPresetList();
            });

            if (_poseListView != null)
            {
                _poseListView.makeItem = () => new Label();
                _poseListView.selectionType = SelectionType.Single;
                _poseListView.bindItem = (element, index) =>
                {
                    if (index >= 0 && index < _poseAssets.Count)
                    {
                        ((Label)element).text = _poseAssets[index] != null ? _poseAssets[index].name : "<Missing>";
                    }
                };
                _poseListView.onSelectionChange += OnPoseSelectionChanged;
            }

            if (_presetListView != null)
            {
                _presetListView.makeItem = () => new VisualElement
                {
                    name = "preset-list-item"
                };
                _presetListView.selectionType = SelectionType.Single;
                _presetListView.bindItem = (element, index) =>
                {
                    element.Clear();
                    element.AddToClassList("preset-list-item");
                    if (index < 0 || index >= _presetAssets.Count)
                    {
                        return;
                    }

                    var preset = _presetAssets[index];
                    if (preset.Thumbnail != null)
                    {
                        var image = new Image
                        {
                            image = preset.Thumbnail,
                            scaleMode = ScaleMode.ScaleToFit
                        };
                        image.AddToClassList("preset-thumbnail");
                        element.Add(image);
                    }

                    element.Add(new Label(preset.Name));
                };
            }
        }

        private void LoadPoseAssets()
        {
            _poseAssets.Clear();
            string[] guids = AssetDatabase.FindAssets("t:YUCPHandPoseAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<YUCPHandPoseAsset>(path);
                if (asset != null)
                {
                    _poseAssets.Add(asset);
                }
            }

            _poseAssets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            if (_poseListView != null)
            {
                _poseListView.itemsSource = _poseAssets;
                _poseListView.Rebuild();
            }

            if (_currentPose != null)
            {
                int index = _poseAssets.IndexOf(_currentPose);
                if (index >= 0)
                {
                    if (_poseListView != null)
                    {
                        _poseListView.selectedIndex = index;
                    }
                }
            }
        }

        private void LoadPresetAssets(bool forceReload = false)
        {
            if (!forceReload && _presetAssets.Count > 0)
            {
                return;
            }

            _presetAssets.Clear();
            _presetAssets.AddRange(YUCPHandPosePresetLibrary.LoadPresets());
            RefreshPresetList();
        }

        private void RefreshPresetList()
        {
            if (_presetListView != null)
            {
                _presetListView.itemsSource = _presetAssets;
                _presetListView.Rebuild();
            }
        }

        private void OnPoseSelectionChanged(IEnumerable<object> selection)
        {
            foreach (var item in selection)
            {
                if (item is YUCPHandPoseAsset asset)
                {
                    _currentPose = asset;
                    break;
                }
            }

            RefreshPoseSelection();
        }

        private void RefreshPoseSelection()
        {
            if (_currentPose == null)
            {
                _poseNameField?.SetValueWithoutNotify(string.Empty);
                _poseTypeField?.SetValueWithoutNotify(YUCPHandPoseType.Fixed);
                SetPoseControlsEnabled(false);
                return;
            }

            SetPoseControlsEnabled(true);
            _poseNameField?.SetValueWithoutNotify(_currentPose.name);
            if (_poseTypeField != null)
            {
                _poseTypeField.Init(_currentPose.PoseType);
                _poseTypeField.SetValueWithoutNotify(_currentPose.PoseType);
            }

            ApplyPosePreview();
        }

        private void SetPoseControlsEnabled(bool enabled)
        {
            _duplicatePoseButton?.SetEnabled(enabled);
            _deletePoseButton?.SetEnabled(enabled);
            _poseNameField?.SetEnabled(enabled);
            _poseTypeField?.SetEnabled(enabled);
            _blendSlider?.SetEnabled(enabled && _currentPose != null && _currentPose.PoseType == YUCPHandPoseType.Blend);
            _captureOpenLeftButton?.SetEnabled(enabled && _session != null);
            _captureClosedLeftButton?.SetEnabled(enabled && _session != null);
            _captureOpenRightButton?.SetEnabled(enabled && _session != null);
            _captureClosedRightButton?.SetEnabled(enabled && _session != null);
            _mirrorLeftToRightButton?.SetEnabled(enabled);
            _mirrorRightToLeftButton?.SetEnabled(enabled);
            _fingerControlsContainer?.SetEnabled(enabled && _session != null);
        }

        private void RefreshAvatarInfo()
        {
            if (_targetAnimator == null)
            {
                if (_avatarInfoLabel != null)
                {
                    _avatarInfoLabel.text = "Select a VRChat avatar with a humanoid Animator.";
                }
                return;
            }

            if (_avatarInfoLabel != null)
            {
                _avatarInfoLabel.text = $"Animator: {_targetAnimator.name} (humanoid)";
            }
        }

        private void RefreshFingerControls()
        {
            YUCPHandPoseEditorUI.BuildFingerControls(_fingerControlsContainer, _session, _selectedHand, () =>
            {
                if (_currentPose != null && _session != null)
                {
                    WriteDescriptorFromAvatar(_selectedHand, _currentPose.PoseType == YUCPHandPoseType.Blend && _previewBlend >= 0.5f);
                    ApplyPosePreview();
                }
            });
        }

        private void RefreshPreview()
        {
            _previewIMGUI.onGUIHandler = () =>
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(_targetAnimator != null
                    ? "Preview updates the avatar directly in the scene."
                    : "Preview unavailable. Select an avatar.", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            };
            ApplyPosePreview();
        }

        private void ApplyPosePreview()
        {
            if (_session == null || _currentPose == null)
            {
                return;
            }

            YUCPHandPoseAsset temp = ScriptableObject.Instantiate(_currentPose);
            _session.ApplyPose(temp, YUCPHandSide.Left, _previewBlend);
            _session.ApplyPose(temp, YUCPHandSide.Right, _previewBlend);
            DestroyImmediate(temp);
        }

        private void SwitchHand(YUCPHandSide hand)
        {
            _selectedHand = hand;
            UpdateHandToggleState();
            RefreshFingerControls();
        }

        private void UpdateHandToggleState()
        {
            _handLeftButton.EnableInClassList("hand-tab-selected", _selectedHand == YUCPHandSide.Left);
            _handRightButton.EnableInClassList("hand-tab-selected", _selectedHand == YUCPHandSide.Right);
        }

        private void CreatePose()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Hand Pose", "NewHandPose", "asset", "Create a new hand pose asset.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var asset = ScriptableObject.CreateInstance<YUCPHandPoseAsset>();
            asset.PoseType = YUCPHandPoseType.Fixed;
            asset.HandDescriptorLeft = new YUCPHandDescriptor();
            asset.HandDescriptorRight = new YUCPHandDescriptor();
            asset.Version = YUCPHandPoseAsset.CurrentVersion;

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            _currentPose = asset;
            LoadPoseAssets();
            if (_poseListView != null)
            {
                _poseListView.selectedIndex = _poseAssets.IndexOf(asset);
            }
            Selection.activeObject = asset;
            RefreshPoseSelection();
        }

        private void DuplicatePose()
        {
            if (_currentPose == null)
            {
                return;
            }

            string originalPath = AssetDatabase.GetAssetPath(_currentPose);
            string directory = Path.GetDirectoryName(originalPath);
            string newPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(directory ?? "Assets", _currentPose.name + " Copy.asset"));

            var clone = Instantiate(_currentPose);
            AssetDatabase.CreateAsset(clone, newPath);
            AssetDatabase.SaveAssets();

            _currentPose = clone;
            LoadPoseAssets();
            if (_poseListView != null)
            {
                _poseListView.selectedIndex = _poseAssets.IndexOf(clone);
            }
            Selection.activeObject = clone;
            RefreshPoseSelection();
        }

        private void DeletePose()
        {
            if (_currentPose == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(_currentPose);
            if (EditorUtility.DisplayDialog("Delete Pose", $"Delete pose {_currentPose.name}?", "Delete", "Cancel"))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
                _currentPose = null;
                LoadPoseAssets();
                RefreshPoseSelection();
            }
        }

        private void RenamePose(string newName)
        {
            if (_currentPose == null || string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(_currentPose);
            AssetDatabase.RenameAsset(path, newName);
            AssetDatabase.SaveAssets();
            LoadPoseAssets();
            RefreshPoseSelection();
        }

        private void ChangePoseType(YUCPHandPoseType poseType)
        {
            if (_currentPose == null)
            {
                return;
            }

            _currentPose.PoseType = poseType;
            EditorUtility.SetDirty(_currentPose);
            AssetDatabase.SaveAssets();
            _blendSlider?.SetEnabled(poseType == YUCPHandPoseType.Blend);
            RefreshPreview();
        }

        private void CaptureHand(YUCPHandSide hand, bool closed)
        {
            if (_session == null || _currentPose == null)
            {
                return;
            }

            YUCPHandDescriptor descriptor = _session.CaptureHand(hand);
            if (descriptor == null)
            {
                return;
            }

            if (_currentPose.PoseType == YUCPHandPoseType.Blend)
            {
                if (hand == YUCPHandSide.Left)
                {
                    if (closed)
                    {
                        _currentPose.HandDescriptorClosedLeft = descriptor;
                    }
                    else
                    {
                        _currentPose.HandDescriptorOpenLeft = descriptor;
                    }
                }
                else
                {
                    if (closed)
                    {
                        _currentPose.HandDescriptorClosedRight = descriptor;
                    }
                    else
                    {
                        _currentPose.HandDescriptorOpenRight = descriptor;
                    }
                }
            }
            else
            {
                if (hand == YUCPHandSide.Left)
                {
                    _currentPose.HandDescriptorLeft = descriptor;
                }
                else
                {
                    _currentPose.HandDescriptorRight = descriptor;
                }
            }

            EditorUtility.SetDirty(_currentPose);
            AssetDatabase.SaveAssets();
        }

        private void MirrorHand(YUCPHandSide from, YUCPHandSide to)
        {
            if (_currentPose == null)
            {
                return;
            }

            if (_currentPose.PoseType == YUCPHandPoseType.Blend)
            {
                if (from == YUCPHandSide.Left && to == YUCPHandSide.Right)
                {
                    if (_currentPose.HandDescriptorOpenLeft != null)
                    {
                        _currentPose.HandDescriptorOpenRight = _currentPose.HandDescriptorOpenLeft.Mirrored();
                    }
                    if (_currentPose.HandDescriptorClosedLeft != null)
                    {
                        _currentPose.HandDescriptorClosedRight = _currentPose.HandDescriptorClosedLeft.Mirrored();
                    }
                }
                else if (from == YUCPHandSide.Right && to == YUCPHandSide.Left)
                {
                    if (_currentPose.HandDescriptorOpenRight != null)
                    {
                        _currentPose.HandDescriptorOpenLeft = _currentPose.HandDescriptorOpenRight.Mirrored();
                    }
                    if (_currentPose.HandDescriptorClosedRight != null)
                    {
                        _currentPose.HandDescriptorClosedLeft = _currentPose.HandDescriptorClosedRight.Mirrored();
                    }
                }
            }
            else
            {
                if (from == YUCPHandSide.Left && to == YUCPHandSide.Right && _currentPose.HandDescriptorLeft != null)
                {
                    _currentPose.HandDescriptorRight = _currentPose.HandDescriptorLeft.Mirrored();
                }
                else if (from == YUCPHandSide.Right && to == YUCPHandSide.Left && _currentPose.HandDescriptorRight != null)
                {
                    _currentPose.HandDescriptorLeft = _currentPose.HandDescriptorRight.Mirrored();
                }
            }

            EditorUtility.SetDirty(_currentPose);
            AssetDatabase.SaveAssets();
            ApplyPosePreview();
        }

        private void ApplySelectedPreset()
        {
            if (_currentPose == null || _presetListView == null || _presetListView.selectedIndex < 0 || _presetListView.selectedIndex >= _presetAssets.Count)
            {
                return;
            }

            var preset = _presetAssets[_presetListView.selectedIndex];
            if (preset.Pose == null)
            {
                return;
            }

            _currentPose.HandDescriptorLeft = preset.Pose.HandDescriptorLeft;
            _currentPose.HandDescriptorRight = preset.Pose.HandDescriptorRight;
            _currentPose.HandDescriptorOpenLeft = preset.Pose.HandDescriptorOpenLeft;
            _currentPose.HandDescriptorOpenRight = preset.Pose.HandDescriptorOpenRight;
            _currentPose.HandDescriptorClosedLeft = preset.Pose.HandDescriptorClosedLeft;
            _currentPose.HandDescriptorClosedRight = preset.Pose.HandDescriptorClosedRight;
            _currentPose.PoseType = preset.Pose.PoseType;

            EditorUtility.SetDirty(_currentPose);
            AssetDatabase.SaveAssets();
            RefreshPoseSelection();
        }

        private void ResolveAvatar(UnityEngine.Object value)
        {
            _avatarDescriptor = null;
            _targetAnimator = null;
            _session = null;

            if (value == null)
            {
                return;
            }

            if (value is VRCAvatarDescriptor descriptor)
            {
                _avatarDescriptor = descriptor;
                _targetAnimator = descriptor.GetComponentInChildren<Animator>();
            }
            else if (value is GameObject go)
            {
                _avatarDescriptor = go.GetComponent<VRCAvatarDescriptor>();
                _targetAnimator = go.GetComponentInChildren<Animator>();
            }
            else if (value is Animator animator)
            {
                _targetAnimator = animator;
                _avatarDescriptor = animator.GetComponentInParent<VRCAvatarDescriptor>();
            }
            else if (value is Component component)
            {
                _avatarDescriptor = component.GetComponentInParent<VRCAvatarDescriptor>();
                _targetAnimator = component.GetComponentInChildren<Animator>();
            }

            if (_targetAnimator != null && _targetAnimator.avatar != null && _targetAnimator.avatar.isHuman)
            {
                _session = new YUCPHandPoseEditingSession(_targetAnimator);
            }
            else
            {
                _targetAnimator = null;
            }
        }

        private void WriteDescriptorFromAvatar(YUCPHandSide hand, bool closed)
        {
            if (_session == null || _currentPose == null)
            {
                return;
            }

            var descriptor = _session.CaptureHand(hand);
            if (descriptor == null)
            {
                return;
            }

            if (_currentPose.PoseType == YUCPHandPoseType.Blend)
            {
                if (hand == YUCPHandSide.Left)
                {
                    if (closed)
                    {
                        _currentPose.HandDescriptorClosedLeft = descriptor;
                    }
                    else
                    {
                        _currentPose.HandDescriptorOpenLeft = descriptor;
                    }
                }
                else
                {
                    if (closed)
                    {
                        _currentPose.HandDescriptorClosedRight = descriptor;
                    }
                    else
                    {
                        _currentPose.HandDescriptorOpenRight = descriptor;
                    }
                }
            }
            else
            {
                if (hand == YUCPHandSide.Left)
                {
                    _currentPose.HandDescriptorLeft = descriptor;
                }
                else
                {
                    _currentPose.HandDescriptorRight = descriptor;
                }
            }

            EditorUtility.SetDirty(_currentPose);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif

