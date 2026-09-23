using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;
using Toolkit = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(DigitigradeLegRigData))]
    public class DigitigradeLegRigDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (DigitigradeLegRigData)target;
            data.ResolveBones();
            serializedObject.Update();

            var root = new VisualElement();
            Toolkit.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Digitigrade Leg Rig"));
            var support = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(DigitigradeLegRigData));
            if (support != null) root.Add(support);
            var validation = new VisualElement();
            root.Add(validation);

            var style = Toolkit.CreateCard("Wearer Defaults", "Starting values for the wearer's controls. Enter Play Mode to rebuild, then adjust them live in Gesture Manager.");
            var styleContent = Toolkit.GetCardContent(style);
            var movement = new VisualElement();
            AddSlider(movement, "thighMovement", "Thigh Movement");
            AddSlider(movement, "kneeMovement", "Knee Movement");
            AddSlider(movement, "hockMovement", "Hock Movement");
            movement.Add(Toolkit.CreateHelpBox("0 holds the joint near its safe rest pose; 1 uses its full solved motion. Reducing movement can move the paw away from tracking."));
            styleContent.Add(movement);
            AddSlider(styleContent, "pawFlattenWeight", "Paw Follow");
            AddSlider(styleContent, "anklesWeight", "Contact Response");
            root.Add(style);

            var limits = Toolkit.CreateCard("Joint Limits", "Keep the knee and hock within their allowed angles, even when the tracked pose cannot be reached.");
            var limitsContent = Toolkit.GetCardContent(limits);
            AddBoundField(limitsContent, new Toggle("Enforce Joint Limits"), "enforceJointLimits");
            var bounded = new VisualElement();
            bounded.Add(Toolkit.CreateField(serializedObject.FindProperty("kneeAngleLimits"), "Knee Angles"));
            bounded.Add(Toolkit.CreateField(serializedObject.FindProperty("hockAngleLimits"), "Hock Angles"));
            limitsContent.Add(bounded);
            var legacy = new VisualElement();
            legacy.Add(Toolkit.CreateHelpBox("Without Joint Limits the leg uses the original two-pass fold. Individual joint movement controls require Joint Limits.", Toolkit.MessageType.Warning));
            AddSlider(legacy, "ankleUpAngle", "Ankle Up Angle", 90f);
            AddSlider(legacy, "ankleDownAngle", "Ankle Down Angle", 90f);
            AddSlider(legacy, "kneeStraightness", "Knee Straightness");
            limitsContent.Add(legacy);
            root.Add(limits);

            var menu = Toolkit.CreateCard("Avatar Menu", "The wearer can save and adjust these settings in the avatar's expression menu.");
            var menuContent = Toolkit.GetCardContent(menu);
            AddBoundField(menuContent, new TextField("Menu Path"), "menuPath");
            menuContent.Add(Toolkit.CreateField(serializedObject.FindProperty("menuName"), "Toggle Name"));
            var menuLocation = new Label();
            menuLocation.AddToClassList("yucp-card-subtitle");
            menuLocation.style.whiteSpace = WhiteSpace.Normal;
            menuContent.Add(menuLocation);
            root.Add(menu);

            var bones = Toolkit.CreateFoldout("Leg Bones");
            bones.viewDataKey = "digitigrade-rig-bones";
            foreach (var side in new[] { "left", "right" })
            {
                var label = side == "left" ? "Left" : "Right";
                AddBoundField(bones, new ObjectField(label + " Thigh") { objectType = typeof(Transform) }, side + "Thigh");
                bones.Add(Toolkit.CreateField(serializedObject.FindProperty(side + "Shin"), label + " Shin"));
                bones.Add(Toolkit.CreateField(serializedObject.FindProperty(side + "Metatarsus"), label + " Metatarsus"));
                bones.Add(Toolkit.CreateField(serializedObject.FindProperty(side + "Foot"), label + " Foot"));
            }
            root.Add(bones);

            var advanced = Toolkit.CreateFoldout("Advanced & Diagnostics");
            advanced.viewDataKey = "digitigrade-rig-advanced";
            AddSlider(advanced, "stanceAngle", "Stance Angle", 60f);
            advanced.Add(Toolkit.CreateField(serializedObject.FindProperty("bendNormalOverride"), "Bend Normal Override"));
            AddBoundField(advanced, new Toggle("Verbose Logging"), "verboseLogging");
            var summary = new Label();
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.AddToClassList("yucp-card-subtitle");
            advanced.Add(summary);
            root.Add(advanced);

            void Refresh()
            {
                if (target == null) return;
                var useLimits = serializedObject.FindProperty("enforceJointLimits").boolValue;
                bounded.style.display = movement.style.display = useLimits ? DisplayStyle.Flex : DisplayStyle.None;
                legacy.style.display = useLimits ? DisplayStyle.None : DisplayStyle.Flex;
                var path = serializedObject.FindProperty("menuPath").stringValue.Trim('/');
                menuLocation.text = useLimits
                    ? "Wearer sliders appear in " + (string.IsNullOrEmpty(path) ? "the menu root." : path + ".")
                    : "Without Joint Limits the leg uses the original ankle controls.";
                summary.text = string.IsNullOrEmpty(data.GetBuildSummary()) ? "" : "Last build: " + data.GetBuildSummary();
                validation.Clear();
                var animator = data.GetComponentInParent<Animator>();
                var split = animator != null ? animator.GetComponentInChildren<DigitigradeLegSplitData>(true) : null;
                if (data.leftThigh == null || data.rightThigh == null || data.leftShin == null || data.rightShin == null || data.leftFoot == null || data.rightFoot == null)
                    validation.Add(Toolkit.CreateHelpBox("Some leg bones are missing. Assign them under Leg Bones.", Toolkit.MessageType.Warning));
                else if (data.leftMetatarsus == null || data.rightMetatarsus == null)
                    validation.Add(Toolkit.CreateHelpBox(split != null
                        ? "Digitigrade Leg Split will create the metatarsus bones during the build."
                        : "Add Digitigrade Leg Split or assign the existing metatarsus bones under Leg Bones.",
                        split != null ? Toolkit.MessageType.Info : Toolkit.MessageType.Error));
            }

            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            root.Query<Label>(className: "yucp-card-subtitle").ForEach(label => label.style.whiteSpace = WhiteSpace.Normal);
            Refresh();
            return root;
        }

        private void AddSlider(VisualElement parent, string property, string label, float maximum = 1f)
        {
            AddBoundField(parent, new Slider(label, 0f, maximum) { showInputField = true }, property);
        }

        private void AddBoundField(VisualElement parent, BindableElement field, string propertyName)
        {
            var property = serializedObject.FindProperty(propertyName);
            field.bindingPath = property.propertyPath;
            field.tooltip = property.tooltip;
            field.AddToClassList("yucp-field-input");
            field.AddToClassList("unity-base-field__aligned");
            parent.Add(field);
        }
    }
}
