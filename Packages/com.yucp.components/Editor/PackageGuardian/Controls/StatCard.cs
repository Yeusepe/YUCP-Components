using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.PackageGuardian.Editor.Controls
{
    /// <summary>
    /// Modern stat card widget
    /// </summary>
    public class StatCard : VisualElement
    {
        private Label _valueLabel;
        private Label _labelLabel;
        private Label _icon;
        
        public StatCard(string label, string value, string icon = "")
        {
            // Card styling
            style.backgroundColor = new StyleColor(new Color(0.11f, 0.11f, 0.11f)); // #1c1c1c
            style.paddingTop = 20;
            style.paddingBottom = 20;
            style.paddingLeft = 20;
            style.paddingRight = 20;
            style.borderTopLeftRadius = 12;
            style.borderTopRightRadius = 12;
            style.borderBottomLeftRadius = 12;
            style.borderBottomRightRadius = 12;
            style.borderTopWidth = 1;
            style.borderBottomWidth = 1;
            style.borderLeftWidth = 1;
            style.borderRightWidth = 1;
            style.borderTopColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f)); // #2a2a2a
            style.borderBottomColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f));
            style.borderLeftColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f));
            style.borderRightColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f));
            style.minWidth = 150;
            style.flexGrow = 1;
            
            // Icon
            if (!string.IsNullOrEmpty(icon))
            {
                _icon = new Label(icon);
                _icon.style.fontSize = 24;
                _icon.style.color = new StyleColor(new Color(0.21f, 0.75f, 0.69f)); // #36BFB1
                _icon.style.marginBottom = 10;
                Add(_icon);
            }
            
            // Value
            _valueLabel = new Label(value);
            _valueLabel.style.fontSize = 28;
            _valueLabel.style.color = Color.white;
            _valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _valueLabel.style.marginBottom = 5;
            Add(_valueLabel);
            
            // Label
            _labelLabel = new Label(label);
            _labelLabel.style.fontSize = 12;
            _labelLabel.style.color = new StyleColor(new Color(0.53f, 0.53f, 0.53f)); // #888888
            Add(_labelLabel);
        }
        
        public void SetValue(string value)
        {
            _valueLabel.text = value;
        }
    }
}

