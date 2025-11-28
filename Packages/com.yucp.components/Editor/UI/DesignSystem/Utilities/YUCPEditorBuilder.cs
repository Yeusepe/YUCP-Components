using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// Fluent API builder for creating editor UIs programmatically.
    /// Provides a chainable interface for building complex UI layouts.
    /// </summary>
    public class YUCPEditorBuilder
    {
        private readonly VisualElement root;
        private VisualElement currentContainer;
        private readonly Stack<VisualElement> containerStack = new Stack<VisualElement>();

        public YUCPEditorBuilder(VisualElement root)
        {
            this.root = root;
            this.currentContainer = root;
        }

        /// <summary>
        /// Adds a card with title and optional subtitle.
        /// </summary>
        public YUCPEditorBuilder AddCard(string title, string subtitle = null, bool isInfo = false)
        {
            var card = YUCPUIToolkitHelper.CreateCard(title, subtitle, isInfo);
            currentContainer.Add(card);
            var content = YUCPUIToolkitHelper.GetCardContent(card);
            PushContainer(content);
            return this;
        }

        /// <summary>
        /// Adds a section header with optional subtitle.
        /// </summary>
        public YUCPEditorBuilder AddSection(string title, string subtitle = null)
        {
            var section = YUCPUIToolkitHelper.CreateSection(title, subtitle);
            currentContainer.Add(section);
            var content = YUCPUIToolkitHelper.GetSectionContent(section);
            PushContainer(content);
            return this;
        }

        /// <summary>
        /// Adds a property field.
        /// </summary>
        public YUCPEditorBuilder AddField(SerializedProperty property, string label = null)
        {
            var field = YUCPUIToolkitHelper.CreateField(property, label);
            currentContainer.Add(field);
            return this;
        }

        /// <summary>
        /// Adds a help box/alert.
        /// </summary>
        public YUCPEditorBuilder AddHelpBox(string message, YUCPUIToolkitHelper.MessageType type = YUCPUIToolkitHelper.MessageType.Info, string title = null)
        {
            var helpBox = YUCPUIToolkitHelper.CreateHelpBox(message, type, title);
            currentContainer.Add(helpBox);
            return this;
        }

        /// <summary>
        /// Adds a button.
        /// </summary>
        public YUCPEditorBuilder AddButton(string text, Action onClick, YUCPUIToolkitHelper.ButtonVariant variant = YUCPUIToolkitHelper.ButtonVariant.Primary)
        {
            var button = YUCPUIToolkitHelper.CreateButton(text, onClick, variant);
            currentContainer.Add(button);
            return this;
        }

        /// <summary>
        /// Adds a foldout.
        /// </summary>
        public YUCPEditorBuilder AddFoldout(string title, bool expanded = false, Action<Foldout> contentBuilder = null)
        {
            var foldout = YUCPUIToolkitHelper.CreateFoldout(title, expanded);
            currentContainer.Add(foldout);
            
            if (contentBuilder != null)
            {
                PushContainer(foldout);
                contentBuilder(foldout);
                PopContainer();
            }
            
            return this;
        }

        /// <summary>
        /// Adds spacing.
        /// </summary>
        public YUCPEditorBuilder AddSpacing(float spacing = 5f)
        {
            YUCPUIToolkitHelper.AddSpacing(currentContainer, spacing);
            return this;
        }

        /// <summary>
        /// Adds a divider.
        /// </summary>
        public YUCPEditorBuilder AddDivider()
        {
            var divider = YUCPUIToolkitHelper.CreateDivider();
            currentContainer.Add(divider);
            return this;
        }

        /// <summary>
        /// Adds a custom element.
        /// </summary>
        public YUCPEditorBuilder AddElement(VisualElement element)
        {
            currentContainer.Add(element);
            return this;
        }

        /// <summary>
        /// Adds multiple elements.
        /// </summary>
        public YUCPEditorBuilder AddElements(IEnumerable<VisualElement> elements)
        {
            foreach (var element in elements)
            {
                currentContainer.Add(element);
            }
            return this;
        }

        /// <summary>
        /// Ends the current container (card or section) and returns to the parent.
        /// </summary>
        public YUCPEditorBuilder EndContainer()
        {
            PopContainer();
            return this;
        }

        private void PushContainer(VisualElement container)
        {
            containerStack.Push(currentContainer);
            currentContainer = container;
        }

        private void PopContainer()
        {
            if (containerStack.Count > 0)
            {
                currentContainer = containerStack.Pop();
            }
        }

        /// <summary>
        /// Builds and returns the root element.
        /// </summary>
        public VisualElement Build()
        {
            return root;
        }
    }
}


