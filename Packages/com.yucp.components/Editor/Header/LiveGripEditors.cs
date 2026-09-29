using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Toolkit = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(LiveGripData))]
    internal class LiveGripDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (LiveGripData)target;
            serializedObject.Update();
            var root = LiveGripUI.Root("Live Grip");
            var support = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(LiveGripData));
            if (support != null) root.Add(support);
            var validation = new VisualElement();
            root.Add(validation);


            var grip = Toolkit.CreateCard("Grip");
            var gripContent = Toolkit.GetCardContent(grip);
            gripContent.Add(LiveGripUI.Segmented(serializedObject, "hand", "Hand", "Left", "Right"));
            gripContent.Add(LiveGripUI.Segmented(serializedObject, "mode", "Mode", "Live", "Baked"));
            var modeNote = LiveGripUI.Note("");
            gripContent.Add(modeNote);
            root.Add(grip);

            var points = Toolkit.CreateCard("Grip Points");
            var pointsContent = Toolkit.GetCardContent(points);
            var items = new List<LiveGripPoint>();
            var list = new ListView(items, 24, () =>
            {
                var row = new VisualElement();
                row.AddToClassList("live-grip-list-item");
                var name = new Label();
                name.AddToClassList("live-grip-list-item-name");
                row.Add(name);
                var badge = new Label();
                badge.AddToClassList("yucp-badge");
                row.Add(badge);
                return row;
            }, (element, i) =>
            {
                var point = items[i];
                element.Q<Label>(className: "live-grip-list-item-name").text = point.name;
                var badge = element.Q<Label>(className: "yucp-badge");
                var pinned = point.Pins.Count(p => p != null);
                badge.text = pinned == 0 ? "Auto" : pinned + " Pinned";
                badge.EnableInClassList("yucp-badge-default", pinned == 0);
                badge.EnableInClassList("yucp-badge-warning", pinned > 0);
            })
            {
                selectionType = SelectionType.Single,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                tooltip = "Drag to reorder. The grip parameter moves through the grip points in this order."
            };
            list.AddToClassList("live-grip-list");
            list.selectionChanged += _ => LiveGripScene.SelectPoint(data, list.selectedItem as LiveGripPoint);
            list.itemIndexChanged += (_, __) => LiveGripScene.ApplyOrder(items);
            var empty = new Label("Held as placed. Add a grip point to choose where the hand holds it.");
            empty.AddToClassList("live-grip-empty");
            pointsContent.Add(empty);
            pointsContent.Add(list);
            var footer = new VisualElement();
            footer.AddToClassList("live-grip-footer");
            footer.Add(LiveGripUI.IconButton("Toolbar Plus", "Add Grip Point", () => LiveGripScene.Add(data)));
            var remove = LiveGripUI.IconButton("Toolbar Minus", "Remove Grip Point", () =>
            {
                var selected = LiveGripScene.SelectedPoint(data);
                if (selected != null) LiveGripScene.Remove(selected);
            });
            footer.Add(remove);
            pointsContent.Add(footer);
            var detail = new VisualElement();
            pointsContent.Add(detail);
            var parameter = new VisualElement();
            LiveGripUI.Bind(serializedObject, parameter, new TextField("Grip Parameter"), "gripParameter");
            parameter.Add(LiveGripUI.Note("0 is the first grip point, 1 the last. Empty uses LiveGrip/<object>."));
            pointsContent.Add(parameter);
            root.Add(points);

            var toggle = Toolkit.CreateCard("Toggle");
            var toggleContent = Toolkit.GetCardContent(toggle);
            toggleContent.Add(LiveGripUI.Segmented(serializedObject, "useExistingToggle", "Control", "Menu Toggle", "Existing Toggle"));
            var menuFields = new VisualElement();
            LiveGripUI.Bind(serializedObject, menuFields, new TextField("Menu Path"), "menuPath");
            LiveGripUI.Bind(serializedObject, menuFields, new TextField("Toggle Name"), "menuName");
            LiveGripUI.Bind(serializedObject, menuFields, new Toggle("On by Default"), "defaultOn");
            toggleContent.Add(menuFields);
            var existingFields = new VisualElement();
            toggleContent.Add(existingFields);
            var toggleProblem = new VisualElement();
            toggleContent.Add(toggleProblem);
            root.Add(toggle);

            var advanced = Toolkit.CreateFoldout("Advanced & Diagnostics");
            advanced.viewDataKey = "live-grip-advanced";
            LiveGripUI.Bind(serializedObject, advanced, new Slider("Finger Thickness", 0f, 0.03f) { showInputField = true }, "fingerThickness");
            LiveGripUI.Bind(serializedObject, advanced, new FloatField("Release Distance"), "releaseDistance");
            var summary = LiveGripUI.Note("");
            summary.style.marginLeft = 0;
            advanced.Add(summary);
            root.Add(advanced);

            string shownItems = null;
            LiveGripPoint shownPoint = null;
            var shownEditing = false;
            List<Component> shownToggles = null;
            void Refresh()
            {
                if (target == null) return;
                serializedObject.Update();
                validation.Clear();
                foreach (var problem in LiveGripScene.Problems(data))
                    validation.Add(Toolkit.CreateHelpBox(problem, Toolkit.MessageType.Error));
                modeNote.text = data.mode == LiveGripData.Mode.Live
                    ? "Fingers follow the object with IK."
                    : "Fingers hold the saved grip. No IK.";

                var current = data.GetComponentsInChildren<LiveGripPoint>(true).ToList();
                var itemsKey = string.Join("|", current.Select(p => p.GetInstanceID() + p.name + p.Pins.Count(x => x != null)));
                if (itemsKey != shownItems)
                {
                    shownItems = itemsKey;
                    items.Clear();
                    items.AddRange(current);
                    list.style.height = Mathf.Max(1, items.Count) * 24 + 2;
                    list.RefreshItems();
                }
                list.style.display = items.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                empty.style.display = items.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
                var selected = LiveGripScene.SelectedPoint(data);
                var index = items.IndexOf(selected);
                if (list.selectedIndex != index) list.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
                remove.SetEnabled(selected != null);
                var editing = selected != null && LiveGripScene.Editing == selected;
                if (selected != shownPoint || editing != shownEditing)
                {
                    shownPoint = selected;
                    shownEditing = editing;
                    detail.Clear();
                    if (selected != null) detail.Add(LiveGripUI.Detail(selected));
                }
                parameter.style.display = items.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;

                var useExisting = data.useExistingToggle;
                menuFields.style.display = useExisting ? DisplayStyle.None : DisplayStyle.Flex;
                existingFields.style.display = useExisting ? DisplayStyle.Flex : DisplayStyle.None;
                var toggles = LiveGripUI.Toggles(data);
                if (useExisting && (shownToggles == null || !toggles.SequenceEqual(shownToggles)))
                {
                    shownToggles = toggles;
                    existingFields.Clear();
                    existingFields.Add(LiveGripUI.TogglePicker(serializedObject, data, toggles));
                    existingFields.Add(LiveGripUI.Note("The grip is on while this toggle is on."));
                }
                toggleProblem.Clear();
                var problemText = LiveGripScene.ToggleProblem(data);
                if (problemText != null) toggleProblem.Add(Toolkit.CreateHelpBox(problemText, Toolkit.MessageType.Error));
                summary.text = string.IsNullOrEmpty(data.GetBuildSummary()) ? "" : "Last build: " + data.GetBuildSummary();
            }

            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            root.schedule.Execute(Refresh).Every(250);
            Refresh();
            return root;
        }
    }

    [CustomEditor(typeof(LiveGripPoint))]
    internal class LiveGripPointEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var point = (LiveGripPoint)target;
            serializedObject.Update();
            var root = LiveGripUI.Root("Live Grip Point");
            var validation = new VisualElement();
            root.Add(validation);
            var card = Toolkit.CreateCard("Grip");
            var content = Toolkit.GetCardContent(card);
            root.Add(card);
            var pins = Toolkit.CreateFoldout("Pins");
            pins.viewDataKey = "live-grip-point-pins";
            foreach (var finger in LiveGripProcessor.FingerNames)
                pins.Add(Toolkit.CreateField(serializedObject.FindProperty(finger.ToLowerInvariant()), finger));
            root.Add(pins);

            bool? shownEditing = null;
            void Refresh()
            {
                if (target == null) return;
                validation.Clear();
                var data = point.GetComponentInParent<LiveGripData>(true);
                if (data == null) validation.Add(Toolkit.CreateHelpBox("Put this under an object with Live Grip.", Toolkit.MessageType.Error));
                else foreach (var problem in LiveGripScene.Problems(data))
                    validation.Add(Toolkit.CreateHelpBox(problem, Toolkit.MessageType.Error));
                var editing = LiveGripScene.Editing == point;
                if (shownEditing == editing) return;
                shownEditing = editing;
                content.Clear();
                content.Add(LiveGripUI.Detail(point, rename: false));
            }
            root.schedule.Execute(Refresh).Every(250);
            Refresh();
            return root;
        }
    }

    /// <summary>Pieces both Live Grip inspectors are built from, on the YUCP design system.</summary>
    internal static class LiveGripUI
    {
        private const string StylePath = "Packages/com.yucp.components/Editor/UI/LiveGripStyle.uss";

        internal static VisualElement Root(string title)
        {
            var root = new VisualElement();
            Toolkit.LoadDesignSystemStyles(root);
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) root.styleSheets.Add(style);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay(title));
            return root;
        }

        /// <summary>A segmented control over an enum or bool property: one segment per value.</summary>
        internal static VisualElement Segmented(SerializedObject so, string property, string label, params string[] segments)
        {
            var field = new SegmentedField(label, segments) { tooltip = so.FindProperty(property).tooltip };
            int Get()
            {
                var p = so.FindProperty(property);
                return p.propertyType == SerializedPropertyType.Boolean ? (p.boolValue ? 1 : 0) : p.enumValueIndex;
            }
            field.RegisterValueChangedCallback(e =>
            {
                so.Update();
                var p = so.FindProperty(property);
                if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = e.newValue == 1;
                else p.enumValueIndex = e.newValue;
                so.ApplyModifiedProperties();
            });
            field.TrackSerializedObjectValue(so, _ => field.SetValueWithoutNotify(Get()));
            field.SetValueWithoutNotify(Get());
            return field;
        }

        /// <summary>
        /// Segments in a Unity field, so its label lines up with every other field in the inspector.
        /// </summary>
        private sealed class SegmentedField : BaseField<int>
        {
            private readonly Button[] segments;

            public SegmentedField(string label, string[] names) : base(label, new VisualElement())
            {
                AddToClassList(alignedFieldUssClassName);
                var input = this.Q(className: inputUssClassName);
                input.AddToClassList("live-grip-segmented");
                segments = names.Select((name, i) =>
                {
                    var button = new Button(() => value = i) { text = name };
                    button.AddToClassList("live-grip-segment");
                    input.Add(button);
                    return button;
                }).ToArray();
            }

            public override void SetValueWithoutNotify(int newValue)
            {
                base.SetValueWithoutNotify(newValue);
                for (var i = 0; i < segments.Length; i++)
                    segments[i].EnableInClassList("live-grip-segment--selected", i == newValue);
            }
        }

        internal static Label Note(string text)
        {
            var note = new Label(text);
            note.AddToClassList("live-grip-note");
            return note;
        }

        internal static Button IconButton(string icon, string tooltip, Action onClick)
        {
            var button = new Button(onClick) { tooltip = tooltip };
            button.AddToClassList("live-grip-icon-button");
            button.Add(new Image { image = EditorGUIUtility.IconContent(icon).image });
            return button;
        }

        internal static void Bind(SerializedObject so, VisualElement parent, BindableElement field, string property)
        {
            var p = so.FindProperty(property);
            field.bindingPath = p.propertyPath;
            field.tooltip = p.tooltip;
            field.AddToClassList("yucp-field-input");
            field.AddToClassList("unity-base-field__aligned");
            field.Bind(so);
            parent.Add(field);
        }

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
            picker.AddToClassList("unity-base-field__aligned");
            picker.RegisterValueChangedCallback(_ =>
            {
                so.Update();
                so.FindProperty("selectedToggle").objectReferenceValue = picker.index <= 0 ? null : toggles[picker.index - 1];
                so.ApplyModifiedProperties();
            });
            return picker;
        }

        /// <summary>The selected grip: its name, the edit button, and one row per finger.</summary>
        internal static VisualElement Detail(LiveGripPoint point, bool rename = true)
        {
            var detail = new VisualElement();
            if (rename)
            {
                detail.AddToClassList("live-grip-detail");
                var name = new TextField("Name") { bindingPath = "m_Name", isDelayed = true };
                name.AddToClassList("yucp-field-input");
                name.AddToClassList("unity-base-field__aligned");
                name.Bind(new SerializedObject(point.gameObject));
                detail.Add(name);
            }
            var editing = LiveGripScene.Editing == point;
            var edit = Toolkit.CreateButton("", () =>
            {
                if (LiveGripScene.Editing == point) LiveGripScene.StopEditing();
                else LiveGripScene.StartEditing(point);
            }, editing ? Toolkit.ButtonVariant.Primary : Toolkit.ButtonVariant.Secondary);
            edit.AddToClassList("live-grip-edit");
            edit.tooltip = editing ? "Stop editing (Esc)." : "Move the wrist and fingertips in the Scene view.";
            edit.Add(new Image { image = EditorGUIUtility.IconContent("EditCollider").image });
            edit.Add(new Label(editing ? "Done" : "Edit Grip"));
            detail.Add(edit);
            detail.Add(LiveGripScene.CreateFingerList(point));
            return detail;
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
        }

        internal static void SelectFinger(LiveGripPoint point, int finger)
        {
            if (Editing != point) StartEditing(point);
            Selected = finger;
            SceneView.RepaintAll();
        }

        internal static void StartEditing(LiveGripPoint point)
        {
            Editing = point;
            Selected = Wrist;
            var data = point.GetComponentInParent<LiveGripData>(true);
            if (data != null) ListSelection[data] = point;
            Tools.hidden = true;
            SceneView.RepaintAll();
        }

        internal static void StopEditing()
        {
            if (Editing == null && surface == null) return;
            Editing = null;
            Tools.hidden = false;
            if (surface != null) UnityEngine.Object.DestroyImmediate(surface.gameObject);
            surface = null;
            SceneView.RepaintAll();
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
                    row.AddToClassList("live-grip-finger");
                    row.EnableInClassList("live-grip-finger--selected", Editing == point && Selected == f);
                    if (reaches)
                    {
                        row.tooltip = "Select this fingertip in the Scene view.";
                        row.RegisterCallback<ClickEvent>(_ => SelectFinger(point, finger));
                    }
                    var label = new Label(LiveGripProcessor.FingerNames[f]);
                    label.AddToClassList("live-grip-finger-name");
                    row.Add(label);
                    var badge = new Label(text);
                    badge.AddToClassList("yucp-badge");
                    badge.AddToClassList("yucp-badge-" + variant);
                    row.Add(badge);
                    var spacer = new VisualElement();
                    spacer.AddToClassList("live-grip-finger-spacer");
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

            var title = new GUIContent("Editing " + Editing.name);
            var body = new GUIContent("Click a dot, then drag its handle. Esc to finish.");
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } };
            var bodyStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            var width = Mathf.Max(titleStyle.CalcSize(title).x, bodyStyle.CalcSize(body).x) + 20f;
            var height = titleStyle.CalcHeight(title, width) + bodyStyle.CalcHeight(body, width) + 14f;
            Handles.BeginGUI();
            var panel = new Rect(10f, 10f, width, height);
            EditorGUI.DrawRect(panel, new Color(0.13f, 0.13f, 0.13f, 0.92f));
            EditorGUI.DrawRect(new Rect(panel.x, panel.y, 3f, panel.height), Touching);
            GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + 6f, width - 14f, height - 8f));
            GUILayout.Label(title, titleStyle);
            GUILayout.Label(body, bodyStyle);
            GUILayout.EndArea();
            Handles.EndGUI();
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
