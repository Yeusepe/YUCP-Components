using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace YUCP.Components.PackageGuardian.Editor.Windows.UnifiedDashboard
{
    /// <summary>
    /// Tab container for the unified dashboard.
    /// </summary>
    public class TabView : VisualElement
    {
        private VisualElement _tabBar;
        private VisualElement _contentArea;
        private List<TabButton> _tabs;
        private int _activeTabIndex;
        
        public Action<int> OnTabChanged;
        
        public TabView()
        {
            _tabs = new List<TabButton>();
            _activeTabIndex = 0;
            
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;
            
            // Tab bar
            _tabBar = new VisualElement();
            _tabBar.AddToClassList("pg-tab-bar");
            _tabBar.style.flexDirection = FlexDirection.Row;
            _tabBar.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.06f, 0.06f, 0.06f));
            _tabBar.style.borderBottomWidth = 2;
            _tabBar.style.borderBottomColor = new StyleColor(new UnityEngine.Color(0.21f, 0.75f, 0.69f)); // #36BFB1
            _tabBar.style.paddingTop = 10;
            _tabBar.style.paddingBottom = 0;
            _tabBar.style.paddingLeft = 15;
            _tabBar.style.paddingRight = 15;
            Add(_tabBar);
            
            // Content area
            _contentArea = new VisualElement();
            _contentArea.AddToClassList("pg-tab-content");
            _contentArea.style.flexGrow = 1;
            _contentArea.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.035f, 0.035f, 0.035f)); // #090909
            Add(_contentArea);
        }
        
        public void AddTab(string label, VisualElement content)
        {
            int index = _tabs.Count;
            var button = new TabButton(label, index, () => SelectTab(index));
            _tabs.Add(button);
            _tabBar.Add(button);
            
            content.style.flexGrow = 1;
            content.style.display = index == _activeTabIndex ? DisplayStyle.Flex : DisplayStyle.None;
            _contentArea.Add(content);
            
            if (index == _activeTabIndex)
                button.SetActive(true);
        }
        
        public void SelectTab(int index)
        {
            if (index < 0 || index >= _tabs.Count || index == _activeTabIndex)
                return;
            
            // Deactivate old tab
            _tabs[_activeTabIndex].SetActive(false);
            _contentArea[_activeTabIndex].style.display = DisplayStyle.None;
            
            // Activate new tab
            _activeTabIndex = index;
            _tabs[_activeTabIndex].SetActive(true);
            _contentArea[_activeTabIndex].style.display = DisplayStyle.Flex;
            
            OnTabChanged?.Invoke(index);
        }
    }
    
    public class TabButton : Button
    {
        private bool _isActive;
        private int _index;
        
        public TabButton(string label, int index, Action onClick) : base(onClick)
        {
            _index = index;
            text = label;
            
            style.height = 40;
            style.paddingTop = 8;
            style.paddingBottom = 10;
            style.paddingLeft = 20;
            style.paddingRight = 20;
            style.marginRight = 2;
            style.backgroundColor = new StyleColor(new UnityEngine.Color(0.16f, 0.16f, 0.16f)); // #292929
            style.color = new StyleColor(UnityEngine.Color.white);
            style.borderTopLeftRadius = 0;
            style.borderTopRightRadius = 0;
            style.borderBottomLeftRadius = 0;
            style.borderBottomRightRadius = 0;
            style.borderTopWidth = 0;
            style.borderBottomWidth = 3;
            style.borderLeftWidth = 0;
            style.borderRightWidth = 0;
            style.borderBottomColor = new StyleColor(new UnityEngine.Color(0.16f, 0.16f, 0.16f));
            style.fontSize = 14;
        }
        
        public void SetActive(bool active)
        {
            _isActive = active;
            
            if (active)
            {
                style.backgroundColor = new StyleColor(new UnityEngine.Color(0.035f, 0.035f, 0.035f)); // #090909
                style.borderBottomColor = new StyleColor(new UnityEngine.Color(0.21f, 0.75f, 0.69f)); // #36BFB1
            }
            else
            {
                style.backgroundColor = new StyleColor(new UnityEngine.Color(0.16f, 0.16f, 0.16f)); // #292929
                style.borderBottomColor = new StyleColor(new UnityEngine.Color(0.16f, 0.16f, 0.16f));
            }
        }
    }
}

