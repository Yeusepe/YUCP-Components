﻿using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Builds custom header overlays with YUCP branding for component inspectors.
    /// </summary>
    public static class YUCPComponentHeader
    {
        private static VisualElement FindEditor(VisualElement el)
        {
            if (el == null) return null;
            if (el is InspectorElement) return el.parent;
            return FindEditor(el.parent);
        }

        private static Texture2D _icon;
        private static Texture2D LoadIcon()
        {
            if (_icon == null)
            {
                _icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/com.yucp.components/Resources/Icons/Icon.png"
                );
            }
            return _icon;
        }

        private static VisualElement RenderHeader(string title, bool overlay)
        {
            if (!overlay)
            {
                var label = new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold } };
                label.style.marginTop = 10;
                return label;
            }

            var headerArea = new VisualElement
            {
                style = {
                    height = 17,
                    width = Length.Percent(100),
                    top = -20,
                    position = Position.Absolute,
                },
                pickingMode = PickingMode.Ignore
            };

            Color bg = EditorGUIUtility.isProSkin
                ? new Color32(61, 61, 61, 255)
                : new Color32(194, 194, 194, 255);
            var row = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    height = 24,
                    backgroundColor = bg,
                    marginLeft = 18,
                    marginRight = 60,
                },
                pickingMode = PickingMode.Ignore
            };
            headerArea.Add(row);

            var tex = LoadIcon();
            if (tex != null)
            {
                var iconVE = new VisualElement
                {
                    style = {
                        width = 100,
                        height = 14,
                        backgroundImage = new StyleBackground(tex),
                        marginLeft = 2,
                        marginTop = 2,
                        marginRight = 4,
                    },
                    pickingMode = PickingMode.Ignore
                };
                row.Add(iconVE);
            }

            var titleLabel = new Label(title)
            {
                style = {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    paddingTop = 2,
                },
                pickingMode = PickingMode.Ignore
            };
            row.Add(titleLabel);

            var wrapper = new VisualElement();
            wrapper.Add(headerArea);
            return wrapper;
        }

        private static bool HasMultipleHeaders(VisualElement root)
        {
            if (root == null) return false;
            if (root.ClassListContains("yucpMultipleHeaders")) return true;
            return HasMultipleHeaders(root.parent);
        }

        private static void AttachHeaderOverlay(VisualElement body, string title)
        {
            var inspectorRoot = FindEditor(body);
            if (HasMultipleHeaders(body) || inspectorRoot == null)
            {
                body.Add(RenderHeader(title, false));
                return;
            }

            var headerIndex = inspectorRoot.Children()
                .Select((e, i) => (element: e, index: i))
                .Where(x => x.element.name.EndsWith("Header"))
                .Select(x => x.index)
                .DefaultIfEmpty(-1)
                .First();

            if (headerIndex < 0)
            {
                body.Add(RenderHeader(title, false));
                return;
            }

            var headerOverlay = RenderHeader(title, true);
            headerOverlay.AddToClassList("yucpHeaderOverlay");
            inspectorRoot.Insert(headerIndex + 1, headerOverlay);
            body.RegisterCallback<DetachFromPanelEvent>(e => {
                headerOverlay.parent?.Remove(headerOverlay);
            });
        }

        public static VisualElement CreateHeaderOverlay(string title)
        {
            var ve = new VisualElement();
            ve.AddToClassList("yucpHeader");
            ve.RegisterCallback<AttachToPanelEvent>(e => {
                AttachHeaderOverlay(ve, title);
            });
            return ve;
        }
    }
}