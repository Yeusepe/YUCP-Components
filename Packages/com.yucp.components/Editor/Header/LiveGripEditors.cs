using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;
using Toolkit = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(LiveGripData))]
    internal class LiveGripDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (LiveGripData)target;
            var ui = YUCPInspector.Create(this, "Live Grip");
            ui.Status(() => LiveGripScene.Problems(data).Select(YUCPInspector.Message.Error));

            var grip = ui.Section("Grip");
            ui.Segmented(grip, "hand", "Hand", "Left", "Right");
            ui.Segmented(grip, "mode", "Mode", "Live", "Baked");
            ui.Caption(grip, () => data.mode == LiveGripData.Mode.Live
                ? "Fingers follow the object with IK."
                : "Fingers hold the saved grip. No IK.");

            var points = ui.Section("Grip points");
            var items = new List<LiveGripPoint>();
            var list = ui.List(points, items, () =>
            {
                var row = new VisualElement();
                row.AddToClassList("yucp-list-item");
                var name = new Label();
                name.AddToClassList("yucp-list-item-name");
                row.Add(name);
                var badge = new Label();
                badge.AddToClassList("yucp-badge");
                row.Add(badge);
                return row;
            }, (row, i) =>
            {
                row.Q<Label>(className: "yucp-list-item-name").text = items[i].name;
                var badge = row.Q<Label>(className: "yucp-badge");
                var pinned = items[i].Pins.Count(p => p != null);
                badge.text = pinned == 0 ? "Auto" : pinned + " Pinned";
                badge.EnableInClassList("yucp-badge-default", pinned == 0);
                badge.EnableInClassList("yucp-badge-warning", pinned > 0);
            },
                "Held as placed. Add a grip point to choose where the hand holds it.",
                () => LiveGripScene.Add(data), i => LiveGripScene.Remove(items[i]),
                "Add Grip Point", "Remove Grip Point");
            list.View.tooltip = "Drag to reorder. The grip parameter moves through the grip points in this order.";
            list.View.selectionChanged += _ => LiveGripScene.SelectPoint(data, list.View.selectedItem as LiveGripPoint);
            list.View.itemIndexChanged += (_, __) => LiveGripScene.ApplyOrder(items);
            var detail = new VisualElement();
            points.Add(detail);
            var parameter = ui.Group(points);
            ui.Field(parameter, "gripParameter", "Grip Parameter");
            ui.Caption(parameter, "0 is the first grip point, 1 the last. Empty uses LiveGrip/<object>.");

            string shownItems = null;
            LiveGripPoint shownPoint = null;
            var shownEditing = false;
            ui.OnRefresh(() =>
            {
                var current = data.GetComponentsInChildren<LiveGripPoint>(true).ToList();
                var key = string.Join("|", current.Select(p => p.GetInstanceID() + p.name + p.Pins.Count(x => x != null)));
                if (key != shownItems)
                {
                    shownItems = key;
                    items.Clear();
                    items.AddRange(current);
                    list.Refresh();
                }
                var selected = LiveGripScene.SelectedPoint(data);
                list.Select(items.IndexOf(selected));
                var editing = selected != null && LiveGripScene.Editing == selected;
                if (selected == shownPoint && editing == shownEditing) return;
                shownPoint = selected;
                shownEditing = editing;
                detail.Clear();
                if (selected != null) detail.Add(LiveGripUI.Detail(ui, selected));
            });
            ui.ShowWhen(parameter, () => items.Count > 1);

            var toggle = ui.Section("Toggle");
            ui.Segmented(toggle, "useExistingToggle", "Control", "Menu Toggle", "Existing Toggle");
            var menu = new VisualElement();
            toggle.Add(menu);
            ui.Field(menu, "menuPath", "Menu Path");
            ui.Field(menu, "menuName", "Toggle Name");
            ui.Field(menu, "defaultOn", "On by Default");
            ui.ShowWhen(menu, () => !data.useExistingToggle);
            var existing = new VisualElement();
            toggle.Add(existing);
            var picker = new VisualElement();
            existing.Add(picker);
            ui.Caption(existing, "The grip is on while this toggle is on.");
            ui.Messages(existing, () => new[] { YUCPInspector.Message.Error(LiveGripScene.ToggleProblem(data)) });
            ui.ShowWhen(existing, () => data.useExistingToggle);
            List<Component> shownToggles = null;
            ui.OnRefresh(() =>
            {
                if (!data.useExistingToggle) return;
                var toggles = LiveGripUI.Toggles(data);
                if (shownToggles != null && toggles.SequenceEqual(shownToggles)) return;
                shownToggles = toggles;
                picker.Clear();
                picker.Add(LiveGripUI.TogglePicker(ui.Object, data, toggles));
            });

            var advanced = ui.Advanced();
            ui.Field(advanced, "fingerThickness");
            ui.BuildSummary(data.GetBuildSummary);

            LiveGripScene.Changed += ui.Refresh;
            ui.Root.RegisterCallback<DetachFromPanelEvent>(_ => LiveGripScene.Changed -= ui.Refresh);
            ui.Refresh();
            return ui.Root;
        }
    }

    [CustomEditor(typeof(LiveGripPoint))]
    internal class LiveGripPointEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var point = (LiveGripPoint)target;
            var ui = YUCPInspector.Create(this, "Live Grip Point");
            ui.Status(() =>
            {
                var data = point.GetComponentInParent<LiveGripData>(true);
                return data == null
                    ? new[] { YUCPInspector.Message.Error("Put this under an object with Live Grip.") }
                    : LiveGripScene.Problems(data).Select(YUCPInspector.Message.Error);
            });
            var grip = ui.Section("Grip");
            bool? shownEditing = null;
            ui.OnRefresh(() =>
            {
                var editing = LiveGripScene.Editing == point;
                if (shownEditing == editing) return;
                shownEditing = editing;
                grip.Clear();
                grip.Add(LiveGripUI.Detail(ui, point, rename: false));
            });
            var pins = ui.Advanced();
            foreach (var finger in LiveGripProcessor.FingerNames)
                ui.Field(pins, finger.ToLowerInvariant(), finger + " Pin");

            LiveGripScene.Changed += ui.Refresh;
            ui.Root.RegisterCallback<DetachFromPanelEvent>(_ => LiveGripScene.Changed -= ui.Refresh);
            ui.Refresh();
            return ui.Root;
        }
    }

    /// <summary>Live Grip's own pieces: the toggle picker and the selected grip's detail.</summary>
    internal static class LiveGripUI
    {
        /// <summary>The avatar's VRCFury toggles.</summary>
        internal static List<Component> Toggles(LiveGripData data)
        {
            var animator = data.GetComponentInParent<Animator>(true);
            return VRCFuryHelper.FindToggles(animator != null ? animator.gameObject : data.gameObject);
        }

        internal static VisualElement TogglePicker(SerializedObject so, LiveGripData data, List<Component> toggles)
        {
            var automatic = VRCFuryHelper.FindToggles(data.gameObject).FirstOrDefault();
            var labels = new List<string> { automatic != null ? "Automatic (" + LiveGripProcessor.ToggleName(automatic) + ")" : "Automatic" };
            labels.AddRange(toggles.Select(LiveGripProcessor.ToggleName));
            var property = so.FindProperty("selectedToggle");
            var index = toggles.IndexOf(property.objectReferenceValue as Component) + 1;
            var picker = new DropdownField("Toggle", labels, Mathf.Max(0, index)) { tooltip = property.tooltip };
            picker.AddToClassList("yucp-field-input");
            picker.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            picker.RegisterValueChangedCallback(_ =>
            {
                so.Update();
                so.FindProperty("selectedToggle").objectReferenceValue = picker.index <= 0 ? null : toggles[picker.index - 1];
                so.ApplyModifiedProperties();
            });
            return picker;
        }

        /// <summary>The selected grip: its name, the edit button, and one row per finger.</summary>
        internal static VisualElement Detail(YUCPInspector ui, LiveGripPoint point, bool rename = true)
        {
            var detail = ui.Group(new VisualElement());
            if (rename)
            {
                var name = new TextField("Name") { bindingPath = "m_Name", isDelayed = true };
                name.AddToClassList("yucp-field-input");
                name.AddToClassList(BaseField<string>.alignedFieldUssClassName);
                name.Bind(new SerializedObject(point.gameObject));
                detail.Add(name);
            }
            var editing = LiveGripScene.Editing == point;
            ui.Button(detail, editing ? "Done" : "Edit Grip", () =>
            {
                if (LiveGripScene.Editing == point) LiveGripScene.StopEditing();
                else LiveGripScene.StartEditing(point);
            }, editing ? Toolkit.ButtonVariant.Primary : Toolkit.ButtonVariant.Secondary,
                editing ? "Stop editing (Esc)." : "Move the wrist and fingertips in the Scene view.", "EditCollider");
            detail.Add(LiveGripScene.CreateFingerList(point));
            return detail.parent;
        }
    }

    /// <summary>
    /// The scene side of Live Grip. Selecting the object, a grip point or a pin shows each grip's
    /// hand as a ghost. Editing a grip point, like Unity's Edit Collider, shows a dot on the wrist
    /// and on each fingertip: click one to select it, then move it with its handle. The wrist
    /// follows the Move and Rotate tools; a fingertip's handle stays on the object's surface and
    /// pins that finger where it is left.
    /// </summary>
    [InitializeOnLoad]
    internal static class LiveGripScene
    {
        // YUCP accents: touching, pinned, out of reach.
        private static readonly Color Touching = new Color(0.21f, 0.75f, 0.69f);
        private static readonly Color Pinned = new Color(0.89f, 0.65f, 0.29f);
        private static readonly Color Missed = new Color(0.89f, 0.29f, 0.29f);
        private const int Wrist = -1;

        private static readonly Dictionary<LiveGripPoint, (int key, List<LiveGripProcessor.Finger> fingers)> Solves =
            new Dictionary<LiveGripPoint, (int, List<LiveGripProcessor.Finger>)>();
        private static readonly Dictionary<LiveGripData, LiveGripPoint> ListSelection = new Dictionary<LiveGripData, LiveGripPoint>();
        private static MeshCollider surface;
        private static int surfaceKey;

        internal static LiveGripPoint Editing { get; private set; }

        /// <summary>Raised when the edited grip or the selected element changes, so inspectors refresh.</summary>
        internal static event Action Changed;

        /// <summary>The selected element while editing: the wrist, or a finger index.</summary>
        internal static int Selected { get; private set; } = Wrist;

        static LiveGripScene()
        {
            SceneView.duringSceneGui += OnScene;
            Selection.selectionChanged += () =>
            {
                if (Editing != null && !Selection.transforms.Any(t => Related(t, Editing))) StopEditing();
            };
            AssemblyReloadEvents.beforeAssemblyReload += StopEditing;
        }

        internal static LiveGripPoint SelectedPoint(LiveGripData data)
        {
            if (Editing != null && Editing.GetComponentInParent<LiveGripData>(true) == data) return Editing;
            return ListSelection.TryGetValue(data, out var point) && point != null ? point : null;
        }

        internal static void SelectPoint(LiveGripData data, LiveGripPoint point)
        {
            ListSelection[data] = point;
            if (Editing != null && Editing != point)
            {
                if (point != null) StartEditing(point);
                else StopEditing();
            }
            Changed?.Invoke();
        }

        internal static void SelectFinger(LiveGripPoint point, int finger)
        {
            if (Editing != point) StartEditing(point);
            Selected = finger;
            SceneView.RepaintAll();
            Changed?.Invoke();
        }

        internal static void StartEditing(LiveGripPoint point)
        {
            Editing = point;
            Selected = Wrist;
            var data = point.GetComponentInParent<LiveGripData>(true);
            if (data != null) ListSelection[data] = point;
            Tools.hidden = true;
            SceneView.RepaintAll();
            Changed?.Invoke();
        }

        internal static void StopEditing()
        {
            if (Editing == null && surface == null) return;
            Editing = null;
            Tools.hidden = false;
            if (surface != null) UnityEngine.Object.DestroyImmediate(surface.gameObject);
            surface = null;
            SceneView.RepaintAll();
            Changed?.Invoke();
        }

        internal static IEnumerable<string> Problems(LiveGripData data)
        {
            var animator = data.GetComponentInParent<Animator>(true);
            if (animator == null || !animator.isHuman) yield return "Put the object on a humanoid avatar.";
            if (LiveGripProcessor.FindMesh(data) == null) yield return "Add a mesh to the object for the fingers to hold.";
            if (data.mode == LiveGripData.Mode.Live && !MeshUtils.FinalIkBridge.IsAvailable) yield return "Live mode needs Final IK. Import it, or choose Baked.";
        }

        /// <summary>Shown under the toggle picker, next to the choice it is about.</summary>
        internal static string ToggleProblem(LiveGripData data) =>
            data.useExistingToggle && data.selectedToggle == null && !VRCFuryHelper.FindToggles(data.gameObject).Any()
                ? "Choose the toggle that turns the grip on." : null;

        /// <summary>Captures how the hand holds the object right now, then edits it.</summary>
        internal static void Add(LiveGripData data)
        {
            var animator = data.GetComponentInParent<Animator>(true);
            if (animator == null || !animator.isHuman) return;
            var hand = LiveGripProcessor.HandOf(animator, data);
            var go = new GameObject("Grip Point " + (data.GetComponentsInChildren<LiveGripPoint>(true).Length + 1));
            go.transform.SetParent(data.transform, false);
            go.transform.SetPositionAndRotation(hand.position, hand.rotation);
            go.AddComponent<LiveGripPoint>();
            Undo.RegisterCreatedObjectUndo(go, "Add Grip Point");
            StartEditing(go.GetComponent<LiveGripPoint>());
        }

        internal static void Remove(LiveGripPoint point)
        {
            if (Editing == point) StopEditing();
            Undo.DestroyObjectImmediate(point.gameObject);
        }

        /// <summary>Grip points are used in hierarchy order; gives them their sibling slots in the list's order.</summary>
        internal static void ApplyOrder(IList<LiveGripPoint> order)
        {
            if (order.Count == 0) return;
            var siblings = order.Where(p => p.transform.parent == order[0].transform.parent).ToList();
            var slots = siblings.Select(p => p.transform.GetSiblingIndex()).OrderBy(i => i).ToList();
            for (var i = 0; i < siblings.Count; i++)
                Undo.SetSiblingIndex(siblings[i].transform, slots[i], "Reorder Grip Points");
        }

        /// <summary>One row per finger: what it does in this grip. Click a row to select that fingertip.</summary>
        internal static VisualElement CreateFingerList(LiveGripPoint point)
        {
            var rows = new VisualElement();
            var last = "";
            void Update()
            {
                if (point == null || !Context(point, out var data, out var animator, out var hand)) return;
                var solved = Solved(point, data, animator, hand);
                var pins = point.Pins;
                var states = Enumerable.Range(0, LiveGripProcessor.FingerNames.Length).Select(f =>
                {
                    var finger = solved.FirstOrDefault(x => x.index == f);
                    if (finger.chain == null) return (f, pins[f] != null ? "Can't Reach Pin" : "Doesn't Reach", pins[f] != null ? "danger" : "default", false);
                    if (pins[f] == null) return (f, "Touches", "primary", true);
                    return finger.pinMiss > data.fingerThickness
                        ? (f, $"{finger.pinMiss * 1000f:F0} mm Short", "danger", true)
                        : (f, "Pinned", "warning", true);
                }).ToList();
                var key = string.Join("|", states) + Editing?.GetInstanceID() + Selected;
                if (key == last) return;
                last = key;
                rows.Clear();
                foreach (var (f, text, variant, reaches) in states)
                {
                    var finger = f;
                    var row = new VisualElement();
                    row.AddToClassList("yucp-row");
                    row.EnableInClassList("yucp-row--selected", Editing == point && Selected == f);
                    if (reaches)
                    {
                        row.AddToClassList("yucp-row-selectable");
                        row.tooltip = "Select this fingertip in the Scene view.";
                        row.RegisterCallback<ClickEvent>(_ => SelectFinger(point, finger));
                    }
                    var label = new Label(LiveGripProcessor.FingerNames[f]);
                    label.AddToClassList("yucp-row-label");
                    row.Add(label);
                    var badge = new Label(text);
                    badge.AddToClassList("yucp-badge");
                    badge.AddToClassList("yucp-badge-" + variant);
                    row.Add(badge);
                    var spacer = new VisualElement();
                    spacer.AddToClassList("yucp-spacer");
                    row.Add(spacer);
                    if (pins[f] != null)
                    {
                        var unpin = Toolkit.CreateButton("Unpin", () => Unpin(point, finger), Toolkit.ButtonVariant.Ghost);
                        unpin.tooltip = "Let this finger close until it touches.";
                        row.Add(unpin);
                    }
                    rows.Add(row);
                }
            }
            rows.schedule.Execute(Update).Every(250);
            Update();
            return rows;
        }

        private static bool Related(Transform t, LiveGripPoint point) =>
            t.IsChildOf(point.transform) || (point.GetComponentInParent<LiveGripData>(true) is LiveGripData data && t == data.transform);

        internal static bool Context(LiveGripPoint point, out LiveGripData data, out Animator animator, out Transform hand)
        {
            data = point.GetComponentInParent<LiveGripData>(true);
            animator = data != null ? data.GetComponentInParent<Animator>(true) : null;
            hand = animator != null && animator.isHuman ? LiveGripProcessor.HandOf(animator, data) : null;
            return hand != null && LiveGripProcessor.FindMesh(data) != null;
        }

        internal static List<LiveGripProcessor.Finger> Solved(LiveGripPoint point, LiveGripData data, Animator animator, Transform hand)
        {
            unchecked
            {
                var key = point.transform.localToWorldMatrix.GetHashCode();
                key = key * 31 + data.transform.localToWorldMatrix.GetHashCode();
                key = key * 31 + hand.localToWorldMatrix.GetHashCode();
                key = key * 31 + data.fingerThickness.GetHashCode();
                key = key * 31 + (int)data.hand;
                foreach (var pin in point.Pins) key = key * 31 + (pin != null ? pin.position.GetHashCode() : 0);
                if (!Solves.TryGetValue(point, out var solved) || solved.key != key)
                    Solves[point] = solved = (key, LiveGripProcessor.Solve(data, animator, LiveGripProcessor.Placement(data, hand, point), point.Pins));
                return solved.fingers;
            }
        }

        private static void OnScene(SceneView view)
        {
            if (Editing == null && Event.current.type != EventType.Repaint) return;
            var points = Editing != null
                ? new[] { Editing }
                : Selection.transforms
                    .SelectMany(t => t.TryGetComponent<LiveGripData>(out var data)
                        ? data.GetComponentsInChildren<LiveGripPoint>(true)
                        : new[] { t.GetComponentInParent<LiveGripPoint>(true) })
                    .Where(p => p != null).Distinct().ToArray();
            foreach (var point in points) DrawHand(point, point == Editing);
            if (Editing != null) DrawOverlay();
        }

        private static void DrawHand(LiveGripPoint point, bool editable)
        {
            if (point == null || !Context(point, out var data, out var animator, out var hand)) return;
            var solved = Solved(point, data, animator, hand);

            var toPoint = LiveGripProcessor.Rigid(point.transform) * LiveGripProcessor.Rigid(hand).inverse;
            var pins = point.Pins;
            Color ColorOf(LiveGripProcessor.Finger f) =>
                pins[f.index] == null ? Touching : f.pinMiss > data.fingerThickness ? Missed : Pinned;

            if (Event.current.type == EventType.Repaint)
                foreach (var finger in solved)
                {
                    var chain = finger.chain.Select(toPoint.MultiplyPoint3x4).ToArray();
                    Handles.color = new Color(1f, 1f, 1f, 0.35f);
                    Handles.DrawAAPolyLine(2f, point.transform.position, chain[0]);
                    Handles.color = ColorOf(finger);
                    Handles.DrawAAPolyLine(editable ? 6f : 4f, chain);
                    if (!editable)
                    {
                        var contact = toPoint.MultiplyPoint3x4(finger.contact);
                        Handles.SphereHandleCap(0, contact, Quaternion.identity, HandleUtility.GetHandleSize(contact) * 0.04f, EventType.Repaint);
                    }
                }
            if (!editable) return;


            var wrist = point.transform.position;
            if (Selected == Wrist)
            {
                EditorGUI.BeginChangeCheck();
                if (Tools.current == Tool.Rotate)
                {
                    var rotation = Handles.RotationHandle(point.transform.rotation, wrist);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(point.transform, "Rotate Grip");
                        point.transform.rotation = rotation;
                    }
                }
                else
                {
                    var position = Handles.PositionHandle(wrist, Tools.pivotRotation == PivotRotation.Local ? point.transform.rotation : Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(point.transform, "Move Grip");
                        point.transform.position = position;
                    }
                }
                Label(wrist, "Wrist");
            }
            else if (Dot(wrist, Color.white, "Wrist")) Selected = Wrist;

            foreach (var finger in solved)
            {
                var contact = toPoint.MultiplyPoint3x4(finger.contact);
                if (Selected != finger.index)
                {
                    if (Dot(contact, ColorOf(finger), finger.Name)) Selected = finger.index;
                    continue;
                }
                var normal = (toPoint.rotation * finger.normal).normalized;
                if (Event.current.type == EventType.Repaint)
                {
                    Handles.color = ColorOf(finger);
                    Handles.DrawWireDisc(contact, normal, HandleUtility.GetHandleSize(contact) * 0.12f, 3f);
                }

                EditorGUI.BeginChangeCheck();
                var moved = Handles.PositionHandle(contact, Tools.pivotRotation == PivotRotation.Local
                    ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity);
                if (EditorGUI.EndChangeCheck()) MovePin(point, finger.index, OnSurface(data, moved, normal));
                Label(contact, finger.Name + (pins[finger.index] != null ? " (Pinned)" : ""));
            }
        }

        /// <summary>A clickable dot; true when clicked. Its name shows on hover.</summary>
        private static bool Dot(Vector3 position, Color color, string name)
        {
            var id = GUIUtility.GetControlID(FocusType.Passive);
            var size = HandleUtility.GetHandleSize(position) * 0.09f;
            var e = Event.current;
            switch (e.type)
            {
                case EventType.Layout:
                case EventType.MouseMove:
                    Handles.SphereHandleCap(id, position, Quaternion.identity, size, e.type);
                    break;
                case EventType.Repaint:
                    var hover = HandleUtility.nearestControl == id;
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(position, Camera.current.transform.forward, size * (hover ? 0.85f : 0.65f), 2f);
                    Handles.color = color;
                    Handles.SphereHandleCap(id, position, Quaternion.identity, size, EventType.Repaint);
                    if (hover) Label(position, name);
                    break;
                case EventType.MouseDown:
                    if (e.button == 0 && HandleUtility.nearestControl == id && !e.alt)
                    {
                        e.Use();
                        return true;
                    }
                    break;
            }
            return false;
        }

        private static void Label(Vector3 position, string text)
        {
            if (Event.current.type != EventType.Repaint) return;
            var offset = HandleUtility.GetHandleSize(position) * 0.15f;
            Handles.Label(position + Camera.current.transform.up * offset, text, EditorStyles.whiteBoldLabel);
        }

        private static void DrawOverlay()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                StopEditing();
                Event.current.Use();
                return;
            }

            YUCPSceneOverlay.Draw("Editing " + Editing.name, "Click a dot, then drag its handle. Esc to finish.");
        }

        /// <summary>The point of the object's hull nearest <paramref name="position"/>; from inside, the one above it along <paramref name="normal"/>.</summary>
        private static Vector3 OnSurface(LiveGripData data, Vector3 position, Vector3 normal)
        {
            var key = data.transform.localToWorldMatrix.GetHashCode();
            if (surface == null || surfaceKey != key)
            {
                if (surface != null) UnityEngine.Object.DestroyImmediate(surface.gameObject);
                surface = LiveGripProcessor.MakeShape(LiveGripProcessor.FindMesh(data), Matrix4x4.identity);
                surfaceKey = key;
            }
            var closest = surface.ClosestPoint(position);
            return (closest - position).sqrMagnitude > 1e-12f ? closest
                : surface.ClosestPoint(position + normal * surface.bounds.size.magnitude);
        }

        private static void MovePin(LiveGripPoint point, int finger, Vector3 position)
        {
            var pin = point.Pins[finger];
            if (pin == null)
            {
                pin = new GameObject(LiveGripProcessor.FingerNames[finger] + " Pin").transform;
                pin.SetParent(point.transform, false);
                Undo.RegisterCreatedObjectUndo(pin.gameObject, "Pin Finger");
                var so = new SerializedObject(point);
                so.FindProperty(LiveGripProcessor.FingerNames[finger].ToLowerInvariant()).objectReferenceValue = pin;
                so.ApplyModifiedProperties();
            }
            else Undo.RecordObject(pin, "Move Pin");
            pin.position = position;
        }

        private static void Unpin(LiveGripPoint point, int finger)
        {
            var pin = point.Pins[finger];
            var so = new SerializedObject(point);
            so.FindProperty(LiveGripProcessor.FingerNames[finger].ToLowerInvariant()).objectReferenceValue = null;
            so.ApplyModifiedProperties();
            if (pin != null && pin.IsChildOf(point.transform)) Undo.DestroyObjectImmediate(pin.gameObject);
            SceneView.RepaintAll();
        }
    }
}
