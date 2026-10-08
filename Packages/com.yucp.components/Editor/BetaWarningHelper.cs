using System;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Helper utilities for displaying beta warnings in custom editors.
    /// </summary>
    public static class BetaWarningHelper
    {
        
        /// <summary>
        /// Creates a VisualElement beta warning container if the component has a BetaWarning attribute.
        /// Returns null if no warning attribute is present.
        /// Uses UI Toolkit components (no IMGUI).
        /// </summary>
        public static VisualElement CreateBetaWarningVisualElement(Type componentType)
        {
            var betaAttribute = (BetaWarningAttribute)Attribute.GetCustomAttribute(
                componentType, 
                typeof(BetaWarningAttribute)
            );
            
            if (betaAttribute == null)
                return null;
            
            var warningBox = YUCPUIToolkitHelper.CreateHelpBox(betaAttribute.Message, YUCPUIToolkitHelper.MessageType.Warning);
            warningBox.style.marginTop = 5;
            warningBox.style.marginBottom = 5;
            
            return warningBox;
        }
    }
}

