using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.PackageGuardian.Editor.Controls
{
    /// <summary>
    /// Timeline item with visual connector
    /// </summary>
    public class TimelineItem : VisualElement
    {
        public TimelineItem(string message, string details, Color accentColor, bool isLast = false)
        {
            style.flexDirection = FlexDirection.Row;
            style.marginBottom = isLast ? 0 : 15;
            
            // Left side - timeline connector
            var timelineColumn = new VisualElement();
            timelineColumn.style.width = 40;
            timelineColumn.style.alignItems = Align.Center;
            
            // Dot
            var dot = new VisualElement();
            dot.style.width = 12;
            dot.style.height = 12;
            dot.style.backgroundColor = new StyleColor(accentColor);
            dot.style.borderTopLeftRadius = 6;
            dot.style.borderTopRightRadius = 6;
            dot.style.borderBottomLeftRadius = 6;
            dot.style.borderBottomRightRadius = 6;
            dot.style.marginTop = 5;
            timelineColumn.Add(dot);
            
            // Connecting line
            if (!isLast)
            {
                var line = new VisualElement();
                line.style.width = 2;
                line.style.flexGrow = 1;
                line.style.backgroundColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f)); // #2a2a2a
                line.style.marginTop = 3;
                timelineColumn.Add(line);
            }
            
            Add(timelineColumn);
            
            // Right side - content card
            var contentCard = new VisualElement();
            contentCard.style.flexGrow = 1;
            contentCard.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f)); // #1e1e1e
            contentCard.style.paddingTop = 12;
            contentCard.style.paddingBottom = 12;
            contentCard.style.paddingLeft = 15;
            contentCard.style.paddingRight = 15;
            contentCard.style.borderTopLeftRadius = 8;
            contentCard.style.borderTopRightRadius = 8;
            contentCard.style.borderBottomLeftRadius = 8;
            contentCard.style.borderBottomRightRadius = 8;
            contentCard.style.borderTopWidth = 1;
            contentCard.style.borderBottomWidth = 1;
            contentCard.style.borderLeftWidth = 1;
            contentCard.style.borderRightWidth = 1;
            contentCard.style.borderTopColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f)); // #2a2a2a
            contentCard.style.borderBottomColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f));
            contentCard.style.borderLeftColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f));
            contentCard.style.borderRightColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f));
            
            // Message
            var messageLabel = new Label(message);
            messageLabel.style.color = Color.white;
            messageLabel.style.fontSize = 13;
            messageLabel.style.marginBottom = 5;
            contentCard.Add(messageLabel);
            
            // Details
            var detailsLabel = new Label(details);
            detailsLabel.style.color = new StyleColor(new Color(0.53f, 0.53f, 0.53f)); // #888888
            detailsLabel.style.fontSize = 11;
            contentCard.Add(detailsLabel);
            
            Add(contentCard);
        }
    }
}

