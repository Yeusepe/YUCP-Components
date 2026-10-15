using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// Builds a YUCP inspector the same way every time (Packages/com.yucp.ui/DESIGN.md).
    ///
    /// Top to bottom: the header a package adds through <see cref="Created"/>, status, sections
    /// in order of importance, then one "Advanced Options" disclosure with diagnostics and the
    /// last build. Everything that changes with the data refreshes when the serialized object
    /// changes, plus a cheap tick for scene state; nothing is rebuilt on a timer, so focus and
    /// drags survive.
    /// </summary>
    public sealed class YUCPInspector
    {
        public enum Severity { Info, Warning, Error, Success }

        public readonly struct Message
        {
            public readonly string Text;
            public readonly Severity Severity;

            public Message(string text, Severity severity)
            {
                Text = text;
                Severity = severity;
            }

            public static Message Info(string text) => new Message(text, Severity.Info);
            public static Message Warning(string text) => new Message(text, Severity.Warning);
            public static Message Error(string text) => new Message(text, Severity.Error);
            public static Message Success(string text) => new Message(text, Severity.Success);
        }

        private readonly List<Action> refreshers = new List<Action>();
        private readonly string stateKey;
        private readonly VisualElement status;
        private Foldout advanced;

        public VisualElement Root { get; }
        public SerializedObject Object { get; }
        public string Title { get; }

        /// <summary>
        /// Raised once per inspector, after the status area exists and before the editor adds its
        /// sections. A package uses it for chrome every one of its inspectors shares (a header,
        /// package-wide notices) without each editor asking for it.
        /// </summary>
        public static event Action<YUCPInspector, UnityEditor.Editor> Created;

        private YUCPInspector(VisualElement root, SerializedObject serializedObject, string stateKey, string title)
        {
            Root = root;
            Object = serializedObject;
            Title = title;
            this.stateKey = stateKey;
            status = new VisualElement();
            status.AddToClassList("yucp-status");
            root.Add(status);
        }

        /// <summary>The root of an inspector: styles, status area and refresh.</summary>
        public static YUCPInspector Create(UnityEditor.Editor editor, string title)
        {
            editor.serializedObject.Update();
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.AddToClassList("yucp-inspector");
            var ui = new YUCPInspector(root, editor.serializedObject, editor.target.GetType().FullName, title);
            root.userData = ui; // lets tests and tools reach the builder from the element tree
            root.TrackSerializedObjectValue(editor.serializedObject, _ => ui.Refresh());
            // Scene state an inspector depends on (hierarchy, avatar descriptor) isn't in its serialized object.
            root.schedule.Execute(ui.Refresh).Every(500);
            root.RegisterCallback<AttachToPanelEvent>(_ => ui.Refresh());
            Created?.Invoke(ui, editor);
            return ui;
        }

        // ---- Behaviour -------------------------------------------------------------------------

        /// <summary>Runs on every refresh. Keep it cheap and idempotent: compare, then change only what differs.</summary>
        public void OnRefresh(Action refresh) => refreshers.Add(refresh);

        public void Refresh()
        {
            if (Object.targetObject == null) return;
            Object.UpdateIfRequiredOrScript();
            foreach (var refresh in refreshers) refresh();
        }

        /// <summary>Shows the element only while the condition holds (progressive disclosure).</summary>
        public T ShowWhen<T>(T element, Func<bool> visible) where T : VisualElement
        {
            OnRefresh(() =>
            {
                var display = visible() ? DisplayStyle.Flex : DisplayStyle.None;
                if (element.style.display != display) element.style.display = display;
            });
            element.style.display = visible() ? DisplayStyle.Flex : DisplayStyle.None;
            return element;
        }

        /// <summary>Keeps the element visible but unavailable while the condition fails.</summary>
        public T EnableWhen<T>(T element, Func<bool> enabled) where T : VisualElement
        {
            OnRefresh(() => element.SetEnabled(enabled()));
            element.SetEnabled(enabled());
            return element;
        }

        // ---- Status --------------------------------------------------------------------------

        /// <summary>Component-level messages, directly under the header.</summary>
        public VisualElement Status(Func<IEnumerable<Message>> messages) => Messages(status, messages);

        /// <summary>Messages next to the setting they are about. Rebuilt only when their text changes.</summary>
        public VisualElement Messages(VisualElement parent, Func<IEnumerable<Message>> messages)
        {
            var host = new VisualElement();
            parent.Add(host);
            string shown = null;
            void Update()
            {
                var current = (messages() ?? Enumerable.Empty<Message>()).Where(m => !string.IsNullOrEmpty(m.Text)).ToList();
                var key = string.Join("\n", current.Select(m => (int)m.Severity + m.Text));
                if (key == shown) return;
                shown = key;
                host.Clear();
                foreach (var message in current)
                    host.Add(YUCPUIToolkitHelper.CreateHelpBox(message.Text, ToMessageType(message.Severity)));
            }
            OnRefresh(Update);
            Update();
            return host;
        }

        private static YUCPUIToolkitHelper.MessageType ToMessageType(Severity severity) => severity switch
        {
            Severity.Warning => YUCPUIToolkitHelper.MessageType.Warning,
            Severity.Error => YUCPUIToolkitHelper.MessageType.Error,
            Severity.Success => YUCPUIToolkitHelper.MessageType.Success,
            _ => YUCPUIToolkitHelper.MessageType.Info
        };

        // ---- Structure -----------------------------------------------------------------------

        /// <summary>A titled section at the root; returns its content. Title in sentence case, description only if it adds something.</summary>
        public VisualElement Section(string title, string description = null) => Section(null, title, description);

        /// <summary>A titled section inside <paramref name="parent"/> (a tab page), or the root when null.</summary>
        public VisualElement Section(VisualElement parent, string title, string description = null)
        {
            var card = YUCPUIToolkitHelper.CreateCard(title, description);
            if (parent != null) parent.Add(card);
            else Insert(card);
            return YUCPUIToolkitHelper.GetCardContent(card);
        }

        /// <summary>A subgroup inside a section: a title and spacing, never another box.</summary>
        public VisualElement Group(VisualElement parent, string title = null)
        {
            var group = new VisualElement();
            group.AddToClassList("yucp-group");
            if (!string.IsNullOrEmpty(title))
            {
                var label = new Label(title);
                label.AddToClassList("yucp-group-title");
                group.Add(label);
            }
            parent.Add(group);
            return group;
        }

        /// <summary>Settings that depend on the control above them.</summary>
        public VisualElement Indent(VisualElement parent)
        {
            var indent = new VisualElement();
            indent.AddToClassList("yucp-indent");
            parent.Add(indent);
            return indent;
        }

        /// <summary>A disclosure whose open state is remembered for this component type.</summary>
        public Foldout Disclosure(VisualElement parent, string title, string key, bool open = false)
        {
            var prefKey = "YUCP.Disclosure." + stateKey + "." + key;
            var foldout = YUCPUIToolkitHelper.CreateFoldout(title, SessionState.GetBool(prefKey, open));
            foldout.RegisterValueChangedCallback(e =>
            {
                if (e.target == foldout) SessionState.SetBool(prefKey, e.newValue);
            });
            if (parent != null) parent.Add(foldout);
            else Insert(foldout);
            return foldout;
        }

        /// <summary>The one "Advanced Options" disclosure, always last: rarely changed settings and diagnostics.</summary>
        public VisualElement Advanced()
        {
            if (advanced == null)
            {
                advanced = Disclosure(Root, "Advanced Options", "advanced");
                advanced.AddToClassList("yucp-advanced");
            }
            return advanced.contentContainer;
        }

        /// <summary>Tabs (macOS tab view) for views of the same settings, e.g. Simple and Advanced. The choice is remembered.</summary>
        public TabView Tabs(string key, params string[] names)
        {
            var prefKey = "YUCP.Tabs." + stateKey + "." + key;
            var view = new TabView(names, SessionState.GetInt(prefKey, 0), i => SessionState.SetInt(prefKey, i));
            Insert(view.Header);
            foreach (var page in view.Pages) Insert(page);
            return view;
        }

        public sealed class TabView
        {
            public VisualElement Header { get; }
            public Button[] Buttons { get; }
            public VisualElement[] Pages { get; }
            public int Selected { get; private set; }
            public event Action<int> Changed;

            internal TabView(string[] names, int selected, Action<int> remember)
            {
                Header = new VisualElement();
                Header.AddToClassList("yucp-tabs-header");
                Pages = names.Select(_ => new VisualElement()).ToArray();
                Buttons = names.Select((name, i) =>
                {
                    var button = new Button(() => { Select(i); remember(i); }) { text = name };
                    button.AddToClassList("yucp-tab");
                    Header.Add(button);
                    return button;
                }).ToArray();
                Select(Mathf.Clamp(selected, 0, names.Length - 1));
            }

            public void Select(int index)
            {
                Selected = index;
                for (var i = 0; i < Pages.Length; i++)
                {
                    Pages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                    Buttons[i].EnableInClassList("yucp-tab-selected", i == index);
                }
                Changed?.Invoke(index);
            }
        }

        private void Insert(VisualElement element)
        {
            if (advanced != null && advanced.parent == Root) Root.Insert(Root.IndexOf(advanced), element);
            else Root.Add(element);
        }

        // ---- Fields and text -------------------------------------------------------------------

        /// <summary>A bound field on the shared label column. Label and tooltip come from the property unless given.</summary>
        public PropertyField Field(VisualElement parent, string path, string label = null, string tooltip = null) =>
            Field(parent, Object, path, label, tooltip);

        /// <summary>A bound field on another serialized object (e.g. a profile asset).</summary>
        public PropertyField Field(VisualElement parent, SerializedObject serializedObject, string path, string label = null, string tooltip = null)
        {
            var property = serializedObject.FindProperty(path);
            if (property == null)
            {
                parent.Add(YUCPUIToolkitHelper.CreateHelpBox($"Missing property \"{path}\".", YUCPUIToolkitHelper.MessageType.Error));
                return null;
            }
            var title = label ?? property.displayName;
            var field = new PropertyField(property, title);
            field.AddToClassList("yucp-field-input");
            if (!string.IsNullOrEmpty(tooltip)) field.tooltip = tooltip;
            field.BindProperty(property);
            if (property.propertyType == SerializedPropertyType.Boolean)
            {
                // A macOS checkbox: the box on the leading edge, its title trailing (HIG Toggles), so
                // dependent settings indent under it.
                field.AddToClassList("yucp-checkbox");
                field.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    var toggle = field.Q<Toggle>();
                    // Unity's own label is hidden in YUCPInspector.uss; it re-adds it on rebinding.
                    if (toggle != null && toggle.text != title) toggle.text = title;
                });
            }
            else if (label != null)
                // Drawers such as [Range] build their own field with the property's name (Unity 2022.3).
                field.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    if (field.childCount == 0 || !field[0].ClassListContains(BaseField<int>.ussClassName)) return;
                    var text = field[0].Q<Label>(className: BaseField<int>.labelUssClassName);
                    if (text != null && text.text != label) text.text = label;
                });
            parent.Add(field);
            return field;
        }

        /// <summary>
        /// A checkbox that isn't bound to a bool property (an enum pair, an editor preference). The
        /// box sits on the leading edge with its title trailing, like every bound bool (HIG Toggles).
        /// </summary>
        public static Toggle Checkbox(string title, string tooltip = null)
        {
            var toggle = new Toggle { text = title, tooltip = tooltip };
            toggle.AddToClassList("yucp-checkbox");
            return toggle;
        }

        /// <summary>A segmented control over an enum, bool or int property. Two to five noun labels.</summary>
        public SegmentedField Segmented(VisualElement parent, string path, string label, params string[] segments)
        {
            var property = Object.FindProperty(path);
            var field = new SegmentedField(label, segments) { tooltip = property?.tooltip };
            // HIG Segmented controls (macOS): "provide [a tooltip] for each segment".
            field.SetSegmentTooltips(segments.Select(name => string.IsNullOrEmpty(property?.tooltip) ? name : name + ". " + property.tooltip).ToArray());
            if (property == null)
            {
                parent.Add(YUCPUIToolkitHelper.CreateHelpBox($"Missing property \"{path}\".", YUCPUIToolkitHelper.MessageType.Error));
                return field;
            }
            int Get()
            {
                var p = Object.FindProperty(path);
                return p.propertyType switch
                {
                    SerializedPropertyType.Boolean => p.boolValue ? 1 : 0,
                    SerializedPropertyType.Enum => p.enumValueIndex,
                    _ => p.intValue
                };
            }
            field.RegisterValueChangedCallback(e =>
            {
                Object.Update();
                var p = Object.FindProperty(path);
                if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = e.newValue == 1;
                else if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = e.newValue;
                else p.intValue = e.newValue;
                Object.ApplyModifiedProperties();
                Refresh();
            });
            OnRefresh(() => field.SetValueWithoutNotify(Get()));
            field.SetValueWithoutNotify(Get());
            parent.Add(field);
            return field;
        }

        /// <summary>One sentence of secondary text under a field or group.</summary>
        public Label Caption(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("yucp-caption");
            parent.Add(label);
            return label;
        }

        /// <summary>Secondary text that follows the data. Hidden while empty.</summary>
        public Label Caption(VisualElement parent, Func<string> text)
        {
            var label = Caption(parent, "");
            void Update()
            {
                var value = text() ?? "";
                if (label.text != value) label.text = value;
                label.style.display = value.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            OnRefresh(Update);
            Update();
            return label;
        }

        /// <summary>A read-only value on the label column.</summary>
        public VisualElement InfoRow(VisualElement parent, string label, Func<string> value)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-info-row");
            var name = new Label(label);
            name.AddToClassList("yucp-info-label");
            var text = new Label(value());
            text.AddToClassList("yucp-info-value");
            text.selection.isSelectable = true;
            row.Add(name);
            row.Add(text);
            parent.Add(row);
            OnRefresh(() =>
            {
                var current = value() ?? "";
                if (text.text != current) text.text = current;
            });
            return row;
        }

        /// <summary>The last build result, as passive status at the end of Advanced Options.</summary>
        public void BuildSummary(Func<string> summary, Func<DateTime?> builtAt = null)
        {
            var label = new Label();
            label.AddToClassList("yucp-build-summary");
            label.selection.isSelectable = true;
            Advanced().Add(label);
            void Update()
            {
                var text = summary();
                var when = builtAt?.Invoke();
                var value = string.IsNullOrEmpty(text) ? "" : "Last build: " + text + (when.HasValue ? $" ({when.Value.ToLocalTime():g})" : "");
                if (label.text != value) label.text = value;
                label.style.display = value.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            OnRefresh(Update);
            Update();
        }

        // ---- Actions ---------------------------------------------------------------------------

        /// <summary>A button. One Primary per inspector; Danger for destructive actions, never Primary.</summary>
        public Button Button(VisualElement parent, string text, Action onClick,
            YUCPUIToolkitHelper.ButtonVariant variant = YUCPUIToolkitHelper.ButtonVariant.Secondary, string tooltip = null, string icon = null)
        {
            var button = YUCPUIToolkitHelper.CreateButton(string.IsNullOrEmpty(icon) ? text : "", onClick, variant);
            if (!string.IsNullOrEmpty(icon))
            {
                // A familiar Unity icon before the label (HIG Icons: familiar metaphors).
                button.AddToClassList("yucp-button-leading-icon");
                button.Add(new Image { image = EditorGUIUtility.IconContent(icon).image });
                button.Add(new Label(text));
            }
            if (!string.IsNullOrEmpty(tooltip)) button.tooltip = tooltip;
            parent.Add(button);
            return button;
        }

        /// <summary>A row of related buttons sharing the width, most important first.</summary>
        public VisualElement ButtonRow(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-button-row");
            parent.Add(row);
            return row;
        }

        /// <summary>An icon-only button. Always give it a tooltip naming the action.</summary>
        public static Button IconButton(string icon, string tooltip, Action onClick)
        {
            var button = new Button(onClick) { tooltip = tooltip };
            button.AddToClassList("yucp-button");
            button.AddToClassList("yucp-button-icon");
            button.Add(new Image { image = EditorGUIUtility.IconContent(icon).image });
            return button;
        }

        /// <summary>
        /// A selectable list with + and - under it (HIG Lists and tables). The selected row stays
        /// highlighted; rows can be dragged to reorder. Call <see cref="ItemList.Refresh"/> after the items change.
        /// </summary>
        public ItemList List(VisualElement parent, IList items, Func<VisualElement> makeItem, Action<VisualElement, int> bindItem,
            string emptyText, Action add, Action<int> remove, string addTooltip, string removeTooltip)
        {
            var list = new ItemList(items, makeItem, bindItem, emptyText, add, remove, addTooltip, removeTooltip);
            parent.Add(list.Element);
            return list;
        }

        public sealed class ItemList
        {
            private const int RowHeight = 24;
            private readonly IList items;
            private readonly Label empty;
            private readonly Button removeButton;

            public VisualElement Element { get; }
            public ListView View { get; }

            internal ItemList(IList items, Func<VisualElement> makeItem, Action<VisualElement, int> bindItem,
                string emptyText, Action add, Action<int> remove, string addTooltip, string removeTooltip)
            {
                this.items = items;
                Element = new VisualElement();
                empty = new Label(emptyText);
                empty.AddToClassList("yucp-list-empty");
                Element.Add(empty);
                View = new ListView(items, RowHeight, makeItem, bindItem)
                {
                    selectionType = SelectionType.Single,
                    reorderable = true,
                    reorderMode = ListViewReorderMode.Animated
                };
                View.AddToClassList("yucp-list");
                Element.Add(View);
                var footer = new VisualElement();
                footer.AddToClassList("yucp-list-footer");
                footer.Add(IconButton("Toolbar Plus", addTooltip, add));
                removeButton = IconButton("Toolbar Minus", removeTooltip, () =>
                {
                    if (View.selectedIndex >= 0) remove(View.selectedIndex);
                });
                footer.Add(removeButton);
                Element.Add(footer);
                View.selectionChanged += _ => removeButton.SetEnabled(View.selectedIndex >= 0);
                Refresh();
            }

            /// <summary>Selects a row (or none, with -1) without raising selection events.</summary>
            public void Select(int index)
            {
                if (View.selectedIndex != index)
                    View.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
                removeButton.SetEnabled(index >= 0 && index < items.Count);
            }

            public void Refresh()
            {
                var count = items.Count;
                View.style.height = Mathf.Max(1, count) * RowHeight + 2;
                View.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                empty.style.display = count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
                View.RefreshItems();
                removeButton.SetEnabled(View.selectedIndex >= 0 && View.selectedIndex < count);
            }
        }
    }

    /// <summary>
    /// Segments in a Unity field, so the label sits on the same column as every other field.
    /// HIG Segmented controls: closely related, mutually exclusive choices; noun labels.
    /// </summary>
    public sealed class SegmentedField : BaseField<int>
    {
        private readonly Button[] segments;

        public SegmentedField(string label, string[] names) : base(label, new VisualElement())
        {
            AddToClassList(alignedFieldUssClassName);
            AddToClassList("yucp-segmented-field");
            var input = this.Q(className: inputUssClassName);
            input.AddToClassList("yucp-segmented");
            segments = names.Select((name, i) =>
            {
                var button = new Button(() => value = i) { text = name };
                button.AddToClassList("yucp-segment");
                input.Add(button);
                return button;
            }).ToArray();
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(newValue);
            for (var i = 0; i < segments.Length; i++)
                segments[i].EnableInClassList("yucp-segment--selected", i == newValue);
        }

        /// <summary>One tooltip per segment, in order.</summary>
        public void SetSegmentTooltips(params string[] tooltips)
        {
            for (var i = 0; i < segments.Length && i < tooltips.Length; i++)
                segments[i].tooltip = tooltips[i];
        }
    }

    /// <summary>
    /// The instruction panel shown in the Scene view while an inspector's edit mode is active
    /// (HIG Modality: make the way out obvious). Opaque so it stays legible over any scene.
    /// </summary>
    public static class YUCPSceneOverlay
    {
        public static readonly Color Accent = new Color(0.21f, 0.75f, 0.69f);

        public static void Draw(string title, string body)
        {
            var titleContent = new GUIContent(title);
            var bodyContent = new GUIContent(body);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } };
            var bodyStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            var width = Mathf.Max(titleStyle.CalcSize(titleContent).x, bodyStyle.CalcSize(bodyContent).x) + 20f;
            var height = titleStyle.CalcHeight(titleContent, width) + (string.IsNullOrEmpty(body) ? 0f : bodyStyle.CalcHeight(bodyContent, width)) + 14f;
            Handles.BeginGUI();
            var panel = new Rect(10f, 10f, width, height);
            EditorGUI.DrawRect(panel, new Color(0.13f, 0.13f, 0.13f, 0.92f));
            EditorGUI.DrawRect(new Rect(panel.x, panel.y, 3f, panel.height), Accent);
            GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + 6f, width - 14f, height - 8f));
            GUILayout.Label(titleContent, titleStyle);
            if (!string.IsNullOrEmpty(body)) GUILayout.Label(bodyContent, bodyStyle);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
