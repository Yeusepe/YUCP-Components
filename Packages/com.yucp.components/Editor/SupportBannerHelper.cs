using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.SupportBanner;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Helper utilities for displaying support banners in custom editors.
    /// </summary>
    public static class SupportBannerHelper
    {
        private const string SupportUrl = "http://patreon.com/Yeusepe";
        private const string PrefNeverKey = "com.yucp.components.support.never";
        private const string PrefCounterKey = "com.yucp.components.support.counter";
        private const string PrefCadenceKey = "com.yucp.components.support.cadence";
        private const string SessionDismissKey = "com.yucp.components.support.dismissed.session";


        /// <summary>
        /// Creates a VisualElement support banner container if the component has a SupportBanner attribute.
        /// Returns null if no banner attribute is present or if it should not be shown.
        /// Uses UI Toolkit components (no IMGUI).
        /// </summary>
        public static VisualElement CreateSupportBannerVisualElement(Type componentType)
        {
            var supportAttribute = (SupportBannerAttribute)Attribute.GetCustomAttribute(
                componentType,
                typeof(SupportBannerAttribute)
            );

            if (supportAttribute == null)
                return null;


            MilestoneTracker.IncrementComponentUsage();


            if (EditorPrefs.GetBool(PrefNeverKey, false)) return null;


            if (SessionState.GetBool(SessionDismissKey, false)) return null;


            Milestone milestone = MilestoneTracker.GetCurrentMilestone();
            bool hasMilestone = milestone != null;


            int count = EditorPrefs.GetInt(PrefCounterKey, 0) + 1;
            EditorPrefs.SetInt(PrefCounterKey, count);

            if (!hasMilestone)
            {
                int cadence = Mathf.Max(1, EditorPrefs.GetInt(PrefCadenceKey, 1000));
                if (count % cadence != 0) return null;
            }

            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPSupportBanner.uxml");

            if (template == null)
                return null;

            var banner = template.Instantiate();

            var iconImage = banner.Q<UnityEngine.UIElements.Image>("support-icon");
            if (iconImage != null)
            {
                var heartIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.components/Resources/Icons/NucleoArcade/heart.png");
                if (heartIcon != null)
                {
                    iconImage.image = heartIcon;
                }
            }


            var titleLabel = banner.Q<Label>("support-title");
            if (titleLabel != null)
            {
                titleLabel.text = milestone != null ? milestone.Title : "Your Support Keeps This Free";
            }

            var subtitleLabel = banner.Q<Label>("support-subtitle");
            if (subtitleLabel != null)
            {
                subtitleLabel.text = milestone != null ? milestone.Subtitle : supportAttribute.Message;
            }

            var supportButton = banner.Q<Button>("support-button");
            if (supportButton != null)
            {
                supportButton.clicked += () => Application.OpenURL(SupportUrl);
            }

            var dismissButton = banner.Q<Button>("support-dismiss");
            if (dismissButton != null)
            {
                dismissButton.clicked += () => {
                    SessionState.SetBool(SessionDismissKey, true);
                    banner.style.display = DisplayStyle.None;
                };
            }

            var neverButton = banner.Q<Button>("support-never");
            if (neverButton != null)
            {
                neverButton.clicked += () => {
                    EditorPrefs.SetBool(PrefNeverKey, true);
                    banner.style.display = DisplayStyle.None;
                };
            }

            return banner;
        }

    }
}

