using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Reusable package card component for grid/list views
    /// </summary>
    public class PackageCard : VisualElement
    {
        private InstalledPackageInfo _packageInfo;
        private Action<InstalledPackageInfo> _onClick;

        public PackageCard(InstalledPackageInfo packageInfo, Action<InstalledPackageInfo> onClick, bool isGridView = true)
        {
            _packageInfo = packageInfo;
            _onClick = onClick;

            AddToClassList("package-card");
            if (isGridView)
            {
                AddToClassList("package-card-grid");
            }
            else
            {
                AddToClassList("package-card-list");
            }

            BuildCard();
        }

        private void BuildCard()
        {
            // Icon container
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("package-card-icon");
            
            if (_packageInfo.icon != null)
            {
                var icon = new Image();
                icon.image = _packageInfo.icon;
                icon.AddToClassList("package-card-icon-image");
                iconContainer.Add(icon);
            }
            else
            {
                // Placeholder icon using Unity built-in icon
                var placeholder = new Image();
                var iconContent = EditorGUIUtility.IconContent("Package Manager");
                if (iconContent != null && iconContent.image != null)
                {
                    placeholder.image = iconContent.image as Texture2D;
                }
                else
                {
                    // Fallback to a simple colored box if icon not found
                    placeholder.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                }
                placeholder.AddToClassList("package-card-icon-placeholder");
                iconContainer.Add(placeholder);
            }
            
            Add(iconContainer);

            // Content container with spacing from icon (only in list view)
            var contentContainer = new VisualElement();
            contentContainer.AddToClassList("package-card-content");
            contentContainer.style.position = Position.Relative;
            
            // Name and version row
            var nameRow = new VisualElement();
            nameRow.AddToClassList("package-card-name-row");
            nameRow.style.position = Position.Relative;
            
            var nameLabel = new Label(_packageInfo.packageName ?? "Unknown Package");
            nameLabel.AddToClassList("package-card-name");
            nameRow.Add(nameLabel);
            
            // Verified badge right beside the name
            if (_packageInfo.isVerified)
            {
                var verifiedBadge = new VisualElement();
                verifiedBadge.AddToClassList("package-card-verified-badge");
                
                string verifiedIconPath = "Packages/com.yucp.components/Editor/PackageManager/Resources/Verified.png";
                Texture2D verifiedTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(verifiedIconPath);
                
                if (verifiedTexture != null)
                {
                    var verifiedIcon = new Image();
                    verifiedIcon.image = verifiedTexture;
                    verifiedIcon.style.width = 16;
                    verifiedIcon.style.height = 16;
                    verifiedBadge.Add(verifiedIcon);
                }
                else
                {
                    // Fallback to checkmark if icon not found
                    var checkLabel = new Label("✓");
                    checkLabel.style.fontSize = 12;
                    checkLabel.style.color = new Color(0.2f, 0.8f, 0.4f);
                    verifiedBadge.Add(checkLabel);
                }
                
                nameRow.Add(verifiedBadge);
            }
            
            if (_packageInfo.hasUpdate)
            {
                var updateBadge = new Label("Update");
                updateBadge.AddToClassList("package-card-update-badge");
                updateBadge.style.position = Position.Relative;
                nameRow.Add(updateBadge);
            }
            
            contentContainer.Add(nameRow);
            
            // Version
            if (!string.IsNullOrEmpty(_packageInfo.installedVersion))
            {
                var versionLabel = new Label($"v{_packageInfo.installedVersion}");
                versionLabel.AddToClassList("package-card-version");
                contentContainer.Add(versionLabel);
            }
            
            // Author
            if (!string.IsNullOrEmpty(_packageInfo.author))
            {
                var authorLabel = new Label(_packageInfo.author);
                authorLabel.AddToClassList("package-card-author");
                contentContainer.Add(authorLabel);
            }
            
            Add(contentContainer);

            // Click handler
            RegisterCallback<ClickEvent>(OnCardClicked);
            this.AddManipulator(new Clickable(OnCardClicked));
            
            // Hover banner overlay (shown on right side when hovering)
            // Added last so it appears on top visually, but only covers right side
            var hoverBanner = new VisualElement();
            hoverBanner.AddToClassList("package-card-hover-banner");
            hoverBanner.style.display = DisplayStyle.None;
            
            // Setup hover banner with gradient
            SetupHoverBanner(hoverBanner);
            
            // Add hover banner last so it's on top (but only covers right side)
            Add(hoverBanner);
            
            // Hover handlers - use this element to track hover
            RegisterCallback<MouseEnterEvent>(evt => {
                if (hoverBanner != null)
                {
                    hoverBanner.style.display = DisplayStyle.Flex;
                    hoverBanner.BringToFront();
                }
            });
            RegisterCallback<MouseLeaveEvent>(evt => {
                if (hoverBanner != null)
                {
                    hoverBanner.style.display = DisplayStyle.None;
                }
            });
        }

        private void OnCardClicked(EventBase evt)
        {
            _onClick?.Invoke(_packageInfo);
        }

        private void SetupHoverBanner(VisualElement hoverBanner)
        {
            if (hoverBanner == null) return;
            
            hoverBanner.style.position = Position.Absolute;
            hoverBanner.style.right = 0;
            hoverBanner.style.top = 0;
            hoverBanner.style.bottom = 0;
            hoverBanner.style.width = 200;
            hoverBanner.style.overflow = Overflow.Hidden;
            
            // Add banner image if available (behind gradient)
            if (_packageInfo.banner != null)
            {
                var bannerImageContainer = new VisualElement();
                bannerImageContainer.style.position = Position.Absolute;
                bannerImageContainer.style.left = 0;
                bannerImageContainer.style.right = 0;
                bannerImageContainer.style.top = 0;
                bannerImageContainer.style.bottom = 0;
                bannerImageContainer.style.backgroundImage = new StyleBackground(_packageInfo.banner);
                bannerImageContainer.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                hoverBanner.Add(bannerImageContainer);
            }
            else
            {
                // Even without banner, show placeholder
                var placeholderContainer = new VisualElement();
                placeholderContainer.style.position = Position.Absolute;
                placeholderContainer.style.left = 0;
                placeholderContainer.style.right = 0;
                placeholderContainer.style.top = 0;
                placeholderContainer.style.bottom = 0;
                placeholderContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.3f);
                hoverBanner.Add(placeholderContainer);
            }
            
            // Create gradient overlay (same grey as other gradients: Color(0.220f, 0.220f, 0.220f))
            // Gradient from right (transparent) to left (opaque grey)
            // Added AFTER banner so it appears on top
            var gradientOverlay = new VisualElement();
            gradientOverlay.style.position = Position.Absolute;
            gradientOverlay.style.left = 0;
            gradientOverlay.style.right = 0;
            gradientOverlay.style.top = 0;
            gradientOverlay.style.bottom = 0;
            
            // Create gradient texture
            int width = 200;
            int height = 300; // Use a reasonable height, will be stretched
            var gradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color gradientEndColor = new Color(0.220f, 0.220f, 0.220f);
            
            // Gradient from transparent at right to semi-transparent at left (so banner shows through)
            // Max alpha of 0.7 so banner is still visible
            for (int x = 0; x < width; x++)
            {
                float t = (float)x / (width - 1); // 0 at right, 1 at left
                float alpha = t * 0.7f; // Linear interpolation from 0 (transparent) to 0.7 (semi-transparent)
                Color color = new Color(gradientEndColor.r, gradientEndColor.g, gradientEndColor.b, alpha);
                
                for (int y = 0; y < height; y++)
                {
                    gradientTexture.SetPixel(x, y, color);
                }
            }
            
            gradientTexture.Apply();
            gradientTexture.wrapMode = TextureWrapMode.Clamp;
            
            gradientOverlay.style.backgroundImage = new StyleBackground(gradientTexture);
            hoverBanner.Add(gradientOverlay);
        }

    }
}


