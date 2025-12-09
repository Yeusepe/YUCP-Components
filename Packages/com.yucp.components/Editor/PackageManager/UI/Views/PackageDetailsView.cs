using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Details view for an installed package, matching installer design
    /// </summary>
    public class PackageDetailsView : VisualElement
    {
        private InstalledPackageInfo _packageInfo;
        private Action _onBack;
        private Action<InstalledPackageInfo> _onUpdate;
        private Action<InstalledPackageInfo> _onUninstall;
        
        private VisualElement _bannerContainer;
        private VisualElement _bannerImageContainer;
        private VisualElement _bannerGradientOverlay;
        private Texture2D _bannerGradientTexture;
        private ScrollView _mainScrollView;
        private VisualElement _contentWrapper;

        public PackageDetailsView(
            InstalledPackageInfo packageInfo,
            Action onBack,
            Action<InstalledPackageInfo> onUpdate,
            Action<InstalledPackageInfo> onUninstall)
        {
            _packageInfo = packageInfo;
            _onBack = onBack;
            _onUpdate = onUpdate;
            _onUninstall = onUninstall;

            AddToClassList("package-details-view");
            
            BuildView();
        }

        private void BuildView()
        {
            // Main scroll view
            _mainScrollView = new ScrollView();
            _mainScrollView.style.flexGrow = 1;
            Add(_mainScrollView);

            var scrollContent = _mainScrollView.contentContainer;
            scrollContent.style.flexDirection = FlexDirection.Column;
            scrollContent.style.position = Position.Relative;
            scrollContent.AddToClassList("yucp-scroll-content");

            // Banner section
            _bannerContainer = CreateBannerSection();
            _bannerContainer.style.position = Position.Absolute;
            _bannerContainer.style.top = 0;
            _bannerContainer.style.left = 0;
            _bannerContainer.style.right = 0;
            scrollContent.Add(_bannerContainer);
            _bannerContainer.SendToBack();

            // Spacer to push content to bottom
            var spacer = new VisualElement();
            spacer.AddToClassList("yucp-spacer");
            scrollContent.Add(spacer);

            // Content wrapper
            _contentWrapper = new VisualElement();
            _contentWrapper.style.flexDirection = FlexDirection.Column;
            _contentWrapper.style.flexShrink = 0;
            _contentWrapper.style.position = Position.Relative;
            _contentWrapper.AddToClassList("yucp-content-wrapper");
            scrollContent.Add(_contentWrapper);

            // Metadata section
            var metadataSection = CreateMetadataSection();
            _contentWrapper.Add(metadataSection);

            // Package Information card
            var infoCard = CreatePackageInfoCard();
            _contentWrapper.Add(infoCard);

            // Dependencies card (if any)
            if (_packageInfo.dependencies != null && _packageInfo.dependencies.Count > 0)
            {
                var dependenciesCard = CreateDependenciesCard();
                _contentWrapper.Add(dependenciesCard);
            }

            // Installed Files section
            var filesSection = CreateInstalledFilesSection();
            _contentWrapper.Add(filesSection);

            // Update gradient after layout
            schedule.Execute(() => CreateBannerGradientTexture());
        }

        private VisualElement CreateBannerSection()
        {
            var bannerContainer = new VisualElement();
            bannerContainer.AddToClassList("yucp-banner-container");
            _bannerContainer = bannerContainer;

            bannerContainer.style.position = Position.Relative;
            bannerContainer.style.height = Length.Percent(75);
            bannerContainer.style.marginBottom = 0;
            bannerContainer.style.width = Length.Percent(100);
            bannerContainer.style.flexShrink = 0;
            bannerContainer.style.overflow = Overflow.Hidden;

            // Banner image container
            _bannerImageContainer = new VisualElement();
            _bannerImageContainer.AddToClassList("yucp-banner-image-container");
            _bannerImageContainer.style.position = Position.Absolute;
            _bannerImageContainer.style.top = 0;
            _bannerImageContainer.style.left = 0;
            _bannerImageContainer.style.right = 0;
            _bannerImageContainer.style.bottom = 0;

            Texture2D displayBanner = _packageInfo?.banner;
            if (displayBanner == null)
            {
                displayBanner = GetPlaceholderTexture();
            }
            if (displayBanner != null)
            {
                _bannerImageContainer.style.backgroundImage = new StyleBackground(displayBanner);
            }
            bannerContainer.Add(_bannerImageContainer);

            // Gradient overlay
            _bannerGradientOverlay = new VisualElement();
            _bannerGradientOverlay.AddToClassList("yucp-banner-gradient-overlay");
            _bannerGradientOverlay.style.position = Position.Absolute;
            _bannerGradientOverlay.style.top = 0;
            _bannerGradientOverlay.style.left = 0;
            _bannerGradientOverlay.style.right = 0;
            _bannerGradientOverlay.style.bottom = 0;
            _bannerGradientOverlay.pickingMode = PickingMode.Ignore;
            bannerContainer.Add(_bannerGradientOverlay);

            return bannerContainer;
        }

        private void CreateBannerGradientTexture()
        {
            if (_bannerContainer == null) return;

            var parent = _bannerContainer.parent;
            float bannerHeight = _bannerContainer.resolvedStyle.height;
            if (bannerHeight <= 0)
            {
                bannerHeight = _bannerContainer.layout.height;
            }
            if (bannerHeight <= 0)
            {
                // Use parent height if available
                if (parent != null)
                {
                    var parentHeight = parent.resolvedStyle.height;
                    if (parentHeight <= 0) parentHeight = parent.layout.height;
                    bannerHeight = parentHeight > 0 ? parentHeight * 0.75f : 400;
                }
                else
                {
                    bannerHeight = 400;
                }
            }

            int width = 4;
            int height = Mathf.RoundToInt(bannerHeight);
            if (height <= 0) height = 400;

            if (_bannerGradientTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(_bannerGradientTexture);
            }

            _bannerGradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color gradientEndColor = new Color(0.220f, 0.220f, 0.220f);

            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1);
                float alpha = t;
                Color color = new Color(gradientEndColor.r, gradientEndColor.g, gradientEndColor.b, alpha);

                for (int x = 0; x < width; x++)
                {
                    _bannerGradientTexture.SetPixel(x, height - 1 - y, color);
                }
            }

            _bannerGradientTexture.Apply();
            _bannerGradientTexture.wrapMode = TextureWrapMode.Clamp;

            if (_bannerGradientOverlay != null && _bannerGradientTexture != null)
            {
                _bannerGradientOverlay.style.backgroundImage = new StyleBackground(_bannerGradientTexture);
                _bannerGradientOverlay.MarkDirtyRepaint();
            }
        }

        private static Texture2D GetPlaceholderTexture()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DefaultGrid.png");
        }

        private VisualElement CreateMetadataSection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-metadata-section");

            // Header row with icon, name, version, author
            var headerRow = new VisualElement();
            headerRow.AddToClassList("yucp-metadata-header");
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 0;

            // Icon
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-metadata-icon-container");

            var iconImageContainer = new VisualElement();
            iconImageContainer.AddToClassList("yucp-metadata-icon-image-container");

            var iconImage = new Image();
            Texture2D displayIcon = _packageInfo?.icon;
            if (displayIcon == null)
            {
                displayIcon = GetPlaceholderTexture();
            }
            iconImage.image = displayIcon;
            iconImage.AddToClassList("yucp-metadata-icon-image");
            iconImageContainer.Add(iconImage);
            iconContainer.Add(iconImageContainer);
            headerRow.Add(iconContainer);

            // Name and version column
            var nameVersionColumn = new VisualElement();
            nameVersionColumn.style.flexGrow = 1;
            nameVersionColumn.style.flexShrink = 1;
            nameVersionColumn.style.marginLeft = 16;
            nameVersionColumn.style.minWidth = 0;
            nameVersionColumn.style.overflow = Overflow.Hidden;

            // Package Name row
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems = Align.Center;
            nameRow.style.flexShrink = 1;
            nameRow.style.minWidth = 0;

            string packageName = string.IsNullOrEmpty(_packageInfo?.packageName) ? "Unknown Package" : _packageInfo.packageName;
            var nameLabel = new Label(packageName);
            nameLabel.AddToClassList("yucp-metadata-name-field");
            nameLabel.AddToClassList("yucp-ellipsis-text");
            nameLabel.style.fontSize = 20;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.tooltip = packageName;
            nameLabel.style.flexShrink = 1;
            nameLabel.style.minWidth = 0;
            nameRow.Add(nameLabel);

            // Verified badge
            if (_packageInfo.isVerified)
            {
                var verifiedBadge = new Label("✓ Verified");
                verifiedBadge.style.marginLeft = 8;
                verifiedBadge.style.fontSize = 11;
                verifiedBadge.style.color = new Color(0.2f, 0.8f, 0.2f);
                nameRow.Add(verifiedBadge);
            }

            // Update badge
            if (_packageInfo.hasUpdate)
            {
                var updateBadge = new Label("Update Available");
                updateBadge.style.marginLeft = 8;
                updateBadge.style.fontSize = 11;
                updateBadge.style.color = new Color(0.2f, 0.6f, 1f);
                nameRow.Add(updateBadge);
            }

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            spacer.style.flexShrink = 0;
            nameRow.Add(spacer);

            nameVersionColumn.Add(nameRow);

            // Author
            if (!string.IsNullOrEmpty(_packageInfo?.author))
            {
                var authorLabel = new Label(_packageInfo.author);
                authorLabel.AddToClassList("yucp-ellipsis-text");
                authorLabel.style.marginTop = 4;
                authorLabel.style.fontSize = 12;
                authorLabel.tooltip = _packageInfo.author;
                nameVersionColumn.Add(authorLabel);
            }

            // Version
            var versionRow = new VisualElement();
            versionRow.style.flexDirection = FlexDirection.Row;
            versionRow.style.alignItems = Align.Center;
            versionRow.style.marginTop = 6;

            var versionLabel = new Label("Version:");
            versionLabel.style.marginRight = 6;
            versionRow.Add(versionLabel);

            var versionValueLabel = new Label(_packageInfo?.installedVersion ?? "");
            versionValueLabel.style.marginRight = 6;
            versionRow.Add(versionValueLabel);

            nameVersionColumn.Add(versionRow);
            headerRow.Add(nameVersionColumn);

            // Action buttons
            var buttonContainer = new VisualElement();
            buttonContainer.AddToClassList("yucp-action-buttons-container");
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.alignItems = Align.Center;
            buttonContainer.style.marginLeft = 16;
            buttonContainer.style.flexShrink = 0;

            // Back button
            var backButton = new Button(() => _onBack?.Invoke()) { text = "Back" };
            backButton.AddToClassList("yucp-action-button");
            buttonContainer.Add(backButton);

            // Update button (if available)
            if (_packageInfo.hasUpdate)
            {
                var updateButton = new Button(() => _onUpdate?.Invoke(_packageInfo)) { text = "Update" };
                updateButton.AddToClassList("yucp-action-button");
                updateButton.AddToClassList("yucp-import-button");
                buttonContainer.Add(updateButton);
            }

            // Uninstall button
            var uninstallButton = new Button(() => _onUninstall?.Invoke(_packageInfo)) { text = "Uninstall" };
            uninstallButton.AddToClassList("yucp-action-button");
            uninstallButton.AddToClassList("yucp-cancel-button");
            buttonContainer.Add(uninstallButton);

            headerRow.Add(buttonContainer);
            section.Add(headerRow);

            // Description
            if (!string.IsNullOrEmpty(_packageInfo?.description))
            {
                var descLabel = new Label(_packageInfo.description);
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.marginTop = 16;
                section.Add(descLabel);
            }

            // Product Links
            if (_packageInfo?.productLinks != null && _packageInfo.productLinks.Count > 0)
            {
                var linksContainer = new VisualElement();
                linksContainer.style.flexDirection = FlexDirection.Row;
                linksContainer.style.flexWrap = Wrap.Wrap;
                linksContainer.style.marginTop = 16;

                foreach (var link in _packageInfo.productLinks)
                {
                    if (string.IsNullOrEmpty(link.url)) continue;

                    var linkButton = new Button(() => Application.OpenURL(link.url));
                    linkButton.style.marginRight = 8;
                    linkButton.style.marginBottom = 8;

                    var linkContent = new VisualElement();
                    linkContent.style.flexDirection = FlexDirection.Row;
                    linkContent.style.alignItems = Align.Center;

                    Texture2D linkIcon = link.GetDisplayIcon();
                    if (linkIcon != null)
                    {
                        var icon = new Image { image = linkIcon };
                        icon.style.width = 16;
                        icon.style.height = 16;
                        icon.style.marginRight = 6;
                        linkContent.Add(icon);
                    }

                    var linkLabel = new Label(string.IsNullOrEmpty(link.label) ? link.url : link.label);
                    linkContent.Add(linkLabel);
                    linkButton.Add(linkContent);
                    linksContainer.Add(linkButton);
                }

                section.Add(linksContainer);
            }

            return section;
        }

        private VisualElement CreatePackageInfoCard()
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-section");
            card.style.marginTop = 20;
            card.style.paddingLeft = 16;
            card.style.paddingRight = 16;
            card.style.paddingTop = 16;
            card.style.paddingBottom = 16;

            var title = new Label("Package Information");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 12;
            card.Add(title);

            var infoGrid = new VisualElement();
            infoGrid.style.flexDirection = FlexDirection.Column;

            AddInfoRow(infoGrid, "Version", _packageInfo.installedVersion ?? "Unknown");
            AddInfoRow(infoGrid, "Installed Date", _packageInfo.GetInstalledDateTime() != DateTime.MinValue 
                ? _packageInfo.GetInstalledDateTime().ToString("yyyy-MM-dd HH:mm:ss") 
                : "Unknown");
            
            // Calculate size
            long totalSize = 0;
            foreach (var filePath in _packageInfo.installedFiles)
            {
                if (File.Exists(filePath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        totalSize += fileInfo.Length;
                    }
                    catch { }
                }
            }
            string sizeStr = totalSize > 0 ? FormatBytes(totalSize) : "Unknown";
            AddInfoRow(infoGrid, "Size", sizeStr);

            if (!string.IsNullOrEmpty(_packageInfo.publisherId))
            {
                AddInfoRow(infoGrid, "Publisher", _packageInfo.publisherId);
            }

            if (!string.IsNullOrEmpty(_packageInfo.packageId))
            {
                AddInfoRow(infoGrid, "Package ID", _packageInfo.packageId);
            }

            if (!string.IsNullOrEmpty(_packageInfo.archiveSha256))
            {
                AddInfoRow(infoGrid, "Archive Hash", _packageInfo.archiveSha256.Substring(0, Math.Min(16, _packageInfo.archiveSha256.Length)) + "...");
            }

            card.Add(infoGrid);

            return card;
        }

        private void AddInfoRow(VisualElement container, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 8;

            var labelElement = new Label(label + ":");
            labelElement.style.width = 120;
            labelElement.style.fontSize = 12;
            labelElement.style.color = new Color(0.7f, 0.7f, 0.7f);
            row.Add(labelElement);

            var valueElement = new Label(value);
            valueElement.style.fontSize = 12;
            valueElement.style.flexGrow = 1;
            row.Add(valueElement);

            container.Add(row);
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private VisualElement CreateDependenciesCard()
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-section");
            card.style.marginTop = 20;
            card.style.paddingLeft = 16;
            card.style.paddingRight = 16;
            card.style.paddingTop = 16;
            card.style.paddingBottom = 16;

            var title = new Label("Dependencies");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 12;
            card.Add(title);

            var depsList = new VisualElement();
            depsList.style.flexDirection = FlexDirection.Column;

            foreach (var dep in _packageInfo.dependencies)
            {
                var depRow = new VisualElement();
                depRow.style.flexDirection = FlexDirection.Row;
                depRow.style.marginBottom = 8;
                depRow.style.paddingLeft = 8;
                depRow.style.paddingRight = 8;
                depRow.style.paddingTop = 8;
                depRow.style.paddingBottom = 8;
                depRow.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
                depRow.style.borderTopLeftRadius = 4;
                depRow.style.borderTopRightRadius = 4;
                depRow.style.borderBottomLeftRadius = 4;
                depRow.style.borderBottomRightRadius = 4;

                var nameLabel = new Label(dep.Key);
                nameLabel.style.fontSize = 12;
                nameLabel.style.flexGrow = 1;
                depRow.Add(nameLabel);

                var versionLabel = new Label($"v{dep.Value}");
                versionLabel.style.fontSize = 11;
                versionLabel.style.color = new Color(0.6f, 0.8f, 1f);
                depRow.Add(versionLabel);

                depsList.Add(depRow);
            }

            card.Add(depsList);
            return card;
        }

        private VisualElement CreateInstalledFilesSection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.style.marginTop = 20;
            section.style.paddingLeft = 16;
            section.style.paddingRight = 16;
            section.style.paddingTop = 16;
            section.style.paddingBottom = 16;

            var title = new Label("Installed Files");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 12;
            section.Add(title);

            var scrollView = new ScrollView();
            scrollView.style.minHeight = 200;
            scrollView.style.maxHeight = 400;

            var filesList = new VisualElement();
            filesList.style.flexDirection = FlexDirection.Column;

            foreach (var filePath in _packageInfo.installedFiles)
            {
                var fileRow = new Label(filePath);
                fileRow.style.fontSize = 11;
                fileRow.style.marginBottom = 4;
                fileRow.style.color = new Color(0.8f, 0.8f, 0.8f);
                filesList.Add(fileRow);
            }

            scrollView.Add(filesList);
            section.Add(scrollView);

            return section;
        }
    }
}





