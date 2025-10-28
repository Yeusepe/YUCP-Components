using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Linq;
using System.Collections.Generic;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    public class ModelRevisionWizard : EditorWindow
    {
        private ModelRevisionVariant _targetVariant;
        private ModelRevisionBase _revisionBase;
        private SerializedObject _serializedBase;

        private VisualElement _rootElement;
        private List<Button> _tabButtons;
        private List<VisualElement> _tabPages;

        [MenuItem("Tools/YUCP/Model Revision Manager")]
        public static void OpenWizard()
        {
            var window = GetWindow<ModelRevisionWizard>();
            window.titleContent = new GUIContent("Model Revision Manager");
            window.minSize = new Vector2(800, 600);
            window.Show();
        }

        public static void OpenWizard(ModelRevisionVariant variant)
        {
            var window = GetWindow<ModelRevisionWizard>("Model Revision Wizard");
            window.SetTargetVariant(variant);
        }

        public void SetTargetVariant(ModelRevisionVariant variant)
        {
            _targetVariant = variant;
            if (_targetVariant != null)
            {
                _revisionBase = _targetVariant.revisionBase;
                if (_revisionBase != null)
                {
                    _serializedBase = new SerializedObject(_revisionBase);
                }
                else
                {
                    Debug.LogError("[ModelRevisionWizard] Target variant has no ModelRevisionBase assigned.");
                    _serializedBase = null;
                }
            }
            else
            {
                _revisionBase = null;
                _serializedBase = null;
            }
            RefreshUI();
        }

        public void CreateGUI()
        {
            _rootElement = rootVisualElement;

            // Load UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.yucp.components/Editor/UI/ModelRevisionWizard.uxml");
            visualTree.CloneTree(_rootElement);

            // Load USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.yucp.components/Editor/UI/ModelRevisionWizard.uss");
            _rootElement.styleSheets.Add(styleSheet);

            _tabButtons = _rootElement.Q("tab-buttons").Children().OfType<Button>().ToList();
            _tabPages = _rootElement.Q("tab-content").Children().ToList();

            RegisterTabCallbacks();
            RegisterFieldCallbacks();
            CreateSetupFields();
            InitializeComponentsTab();
            RefreshUI();
        }

        private void RegisterTabCallbacks()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                int tabIndex = i;
                _tabButtons[i].clicked += () => SelectTab(tabIndex);
            }
        }

        private void RegisterFieldCallbacks()
        {
            var detectDifferencesButton = _rootElement.Q<Button>("detect-differences-button");
            if (detectDifferencesButton != null)
            {
                detectDifferencesButton.clicked += OnDetectDifferencesClicked;
            }
        }

        private void CreateSetupFields()
        {
            var setupContent = _rootElement.Q("setup-content");
            if (setupContent == null) return;

            // Create Original Prefab field
            var originalPrefabContainer = new VisualElement();
            originalPrefabContainer.style.flexDirection = FlexDirection.Row;
            originalPrefabContainer.style.marginBottom = 10;

            var originalPrefabLabel = new Label("Original Prefab:");
            originalPrefabLabel.style.width = 120;
            originalPrefabLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            var originalPrefabField = new ObjectField();
            originalPrefabField.name = "originalPrefabField";
            originalPrefabField.objectType = typeof(GameObject);
            originalPrefabField.allowSceneObjects = true;
            originalPrefabField.style.flexGrow = 1;
            originalPrefabField.RegisterValueChangedCallback(OnOriginalPrefabChanged);

            originalPrefabContainer.Add(originalPrefabLabel);
            originalPrefabContainer.Add(originalPrefabField);

            // Create New Prefab field
            var newPrefabContainer = new VisualElement();
            newPrefabContainer.style.flexDirection = FlexDirection.Row;
            newPrefabContainer.style.marginBottom = 10;

            var newPrefabLabel = new Label("New Prefab (Variant):");
            newPrefabLabel.style.width = 120;
            newPrefabLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            var newPrefabField = new ObjectField();
            newPrefabField.name = "newPrefabField";
            newPrefabField.objectType = typeof(GameObject);
            newPrefabField.allowSceneObjects = true;
            newPrefabField.style.flexGrow = 1;
            newPrefabField.RegisterValueChangedCallback(OnNewPrefabChanged);

            newPrefabContainer.Add(newPrefabLabel);
            newPrefabContainer.Add(newPrefabField);

            setupContent.Add(originalPrefabContainer);
            setupContent.Add(newPrefabContainer);
        }

        private void InitializeComponentsTab()
        {
            var boneMatchingDropdown = _rootElement.Q<DropdownField>("bone-matching-mode");
            if (boneMatchingDropdown != null)
            {
                boneMatchingDropdown.choices = new List<string> { "Name", "Path", "Hybrid" };
                boneMatchingDropdown.value = "Hybrid";
            }
        }

        private void OnOriginalPrefabChanged(ChangeEvent<Object> evt)
        {
            if (_revisionBase != null)
            {
                _revisionBase.basePrefab = evt.newValue as GameObject;
                EditorUtility.SetDirty(_revisionBase);
            }
        }

        private void OnNewPrefabChanged(ChangeEvent<Object> evt)
        {
            var newGameObject = evt.newValue as GameObject;
            if (newGameObject != null)
            {
                // Check if the GameObject has a ModelRevisionVariant component
                var variant = newGameObject.GetComponent<ModelRevisionVariant>();
                if (variant != null)
                {
                    SetTargetVariant(variant);
                }
                else
                {
                    // Ask if user wants to add the component
                    if (EditorUtility.DisplayDialog("Add Model Revision Variant",
                        $"The selected GameObject '{newGameObject.name}' does not have a ModelRevisionVariant component.\n\nWould you like to add one?",
                        "Add Component", "Cancel"))
                    {
                        variant = newGameObject.AddComponent<ModelRevisionVariant>();
                        if (_revisionBase != null)
                        {
                            variant.revisionBase = _revisionBase;
                        }
                        SetTargetVariant(variant);
                        EditorUtility.SetDirty(newGameObject);
                    }
                }
            }
        }

        private void OnDetectDifferencesClicked()
        {
            if (_revisionBase == null || _targetVariant == null)
            {
                EditorUtility.DisplayDialog("Model Revision Manager", 
                    "Please select a ModelRevisionVariant component to detect differences.", "OK");
                return;
            }

            // TODO: Implement difference detection logic
            EditorUtility.DisplayDialog("Model Revision Manager", 
                "Difference detection will be implemented in the next phase.", "OK");
        }

        private void SelectTab(int index)
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                _tabButtons[i].EnableInClassList("active", i == index);
                _tabPages[i].EnableInClassList("active", i == index);
            }
        }

        private void RefreshUI()
        {
            if (_rootElement == null) return;

            // Update ObjectField values
            var originalPrefabField = _rootElement.Q<ObjectField>("originalPrefabField");
            var newPrefabField = _rootElement.Q<ObjectField>("newPrefabField");

            if (originalPrefabField != null)
            {
                // Always enable so users can drag and drop
                originalPrefabField.SetEnabled(true);
                
                if (_revisionBase != null)
                {
                    originalPrefabField.value = _revisionBase.basePrefab;
                }
                else
                {
                    originalPrefabField.value = null;
                }
            }

            if (newPrefabField != null)
            {
                // Always enable so users can drag and drop
                newPrefabField.SetEnabled(true);
                
                if (_targetVariant != null)
                {
                    newPrefabField.value = _targetVariant.gameObject;
                }
                else
                {
                    newPrefabField.value = null;
                }
            }

            SelectTab(0); // Default to Setup tab
        }

        private void OnSelectionChange()
        {
            // Update the wizard if a ModelRevisionVariant is selected
            var selectedVariant = Selection.activeGameObject?.GetComponent<ModelRevisionVariant>();
            if (selectedVariant != null && selectedVariant != _targetVariant)
            {
                SetTargetVariant(selectedVariant);
            }
            else if (selectedVariant == null && _targetVariant != null)
            {
                // If no variant is selected, clear the target
                SetTargetVariant(null);
            }
        }
    }
}
