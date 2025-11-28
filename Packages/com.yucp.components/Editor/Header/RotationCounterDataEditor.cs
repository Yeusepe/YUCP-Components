using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor.UI
{
    /// <summary>
    /// Custom editor for Rotation Counter component with configuration UI.
    /// </summary>
    [CustomEditor(typeof(RotationCounterData))]
    public class RotationCounterDataEditor : UnityEditor.Editor
    {
        private RotationCounterData data;

        private void OnEnable()
        {
            if (target is RotationCounterData)
            {
                data = (RotationCounterData)target;
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            data = (RotationCounterData)target;

            var root = new VisualElement();
            
            // Load design system stylesheets
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Rotation Counter"));

            var builder = new YUCPEditorBuilder(root);

            builder.AddCard("Input Parameters", "Configure the input animator parameters")
                .AddField(serializedObject.FindProperty("xParameterName"), "X Parameter")
                .AddField(serializedObject.FindProperty("yParameterName"), "Y Parameter")
                .AddField(serializedObject.FindProperty("angleParameterName"), "Angle Parameter (degrees 0-360)")
                .AddHelpBox("Input parameters: X and Y are joystick coordinates, Angle is in degrees (0-360).", YUCPUIToolkitHelper.MessageType.Info)
                .EndContainer();

            builder.AddCard("Output Parameters", "Configure the output animator parameters")
                .AddField(serializedObject.FindProperty("rotationStepParameterName"), "Rotation Step Parameter")
                .AddField(serializedObject.FindProperty("flickEventParameterName"), "Flick Event Parameter")
                .AddHelpBox("Output parameters: RotationStep outputs -1, 0, or +1. FlickEvent outputs 0=NONE, 1=RIGHT, 2=UP, 3=LEFT, 4=DOWN.", YUCPUIToolkitHelper.MessageType.Info)
                .EndContainer();

            builder.AddCard("Debugging", "Enable debug features for visualization")
                .AddField(serializedObject.FindProperty("createDebugPhaseParameter"), "Create DebugPhase");

            var debugProp = serializedObject.FindProperty("createDebugPhaseParameter");
            if (debugProp.boolValue)
            {
                builder.AddField(serializedObject.FindProperty("debugPhaseParameterName"), "DebugPhase Parameter");
            }

            builder.AddHelpBox("Enable DebugPhase to visualize which sector the rotation counter currently occupies.", YUCPUIToolkitHelper.MessageType.None)
                .EndContainer();

            builder.AddCard("Rotation Detection", "Configure sector-based rotation detection")
                .AddField(serializedObject.FindProperty("numberOfSectors"), "Number of Sectors")
                .AddField(serializedObject.FindProperty("layerName"), "Layer Name")
                .AddHelpBox("The generator builds a sector-based tracking graph. It outputs RotationStep (-1, 0, +1) as you cross sector boundaries with wraparound correction.", YUCPUIToolkitHelper.MessageType.Info)
                .EndContainer();

            builder.AddCard("Flick Detection", "Configure cardinal direction flick detection")
                .AddField(serializedObject.FindProperty("innerDeadzone"), "Inner Deadzone")
                .AddField(serializedObject.FindProperty("flickMinRadius"), "Flick Min Radius")
                .AddField(serializedObject.FindProperty("releaseRadius"), "Release Radius")
                .AddField(serializedObject.FindProperty("angleToleranceDeg"), "Angle Tolerance (degrees)")
                .AddField(serializedObject.FindProperty("maxFlickFrames"), "Max Flick Frames")
                .AddHelpBox("Flick detection: Detects cardinal direction flicks (RIGHT/UP/LEFT/DOWN) when stick moves from center to edge and back. Cancels if rotation is detected during flick.", YUCPUIToolkitHelper.MessageType.Info)
                .EndContainer();

            builder.AddHelpBox("The rotation counter controller will be generated and automatically integrated via VRCFury FullController at build time. No manual setup required - VRCFury handles all integration.", YUCPUIToolkitHelper.MessageType.Info);

            if (data.controllerGenerated)
            {
                builder.AddCard("Build Statistics", "Information about the generated controller")
                    .AddElement(CreateReadOnlyIntField("Generated Sectors", data.generatedSectorCount))
                    .AddElement(CreateReadOnlyToggle("Controller Generated", data.controllerGenerated))
                    .EndContainer();
            }

            root.schedule.Execute(() => serializedObject.ApplyModifiedProperties()).Every(100);

            return root;
        }

        private VisualElement CreateReadOnlyIntField(string label, int value)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 5;

            var labelElement = new Label(label);
            labelElement.style.width = 150;
            container.Add(labelElement);

            var valueField = new IntegerField { value = value };
            valueField.SetEnabled(false);
            valueField.style.flexGrow = 1;
            container.Add(valueField);

            return container;
        }

        private VisualElement CreateReadOnlyToggle(string label, bool value)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginBottom = 5;

            var labelElement = new Label(label);
            labelElement.style.width = 150;
            container.Add(labelElement);

            var toggle = new Toggle { value = value };
            toggle.SetEnabled(false);
            container.Add(toggle);

            return container;
        }
    }
}

