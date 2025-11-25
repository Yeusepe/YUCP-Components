#if UNITY_EDITOR
// Portions adapted from UltimateXR (MIT License) by VRMADA
// Inspired by the original UxrHandPoseEditorWindow finger widgets.

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace YUCP.Components.HandPoses.Editor
{
    internal static class YUCPHandPoseEditorUI
    {
        private static readonly (FingerSegment segment, string label)[] s_segments =
        {
            (FingerSegment.Metacarpal, "Metacarpal"),
            (FingerSegment.Proximal, "Proximal"),
            (FingerSegment.Intermediate, "Intermediate"),
            (FingerSegment.Distal, "Distal")
        };

        public static void BuildFingerControls(
            VisualElement container,
            YUCPHandPoseEditingSession session,
            YUCPHandSide handSide,
            Action onValueChanged)
        {
            container.Clear();

            if (session == null)
            {
                var label = new Label("Select an avatar with a humanoid Animator to edit hand poses.");
                label.AddToClassList("info-label");
                container.Add(label);
                return;
            }

            foreach (YUCPFingerType finger in Enum.GetValues(typeof(YUCPFingerType)))
            {
                if (finger == YUCPFingerType.None)
                {
                    continue;
                }

                VisualElement fingerElement = new VisualElement
                {
                    name = $"finger-{finger}"
                };
                fingerElement.AddToClassList("finger-control");

                Label fingerLabel = new Label(finger.ToString());
                fingerLabel.AddToClassList("finger-label");
                fingerElement.Add(fingerLabel);

                Dictionary<FingerSegment, FingerSegmentState> segments = new Dictionary<FingerSegment, FingerSegmentState>();
                foreach (var entry in session.EnumerateSegments(handSide))
                {
                    if (entry.finger == finger)
                    {
                        segments[entry.segment] = entry.state;
                    }
                }

                foreach (var segmentInfo in s_segments)
                {
                    FingerSegment segment = segmentInfo.segment;
                    if (!segments.TryGetValue(segment, out FingerSegmentState segmentState))
                    {
                        continue;
                    }

                    VisualElement row = new VisualElement();
                    row.AddToClassList("segment-row");

                    Label segmentLabel = new Label(segmentInfo.label);
                    row.Add(segmentLabel);

                    Slider slider = new Slider(segmentState.MinAngle, segmentState.MaxAngle)
                    {
                        value = segmentState.Value
                    };
                    slider.RegisterValueChangedCallback(evt =>
                    {
                        session.SetValue(handSide, finger, segment, evt.newValue);
                        onValueChanged?.Invoke();
                    });
                    row.Add(slider);

                    fingerElement.Add(row);
                }

                container.Add(fingerElement);
            }
        }
    }
}
#endif

