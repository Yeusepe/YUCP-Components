using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Importer.Editor.PackageManager
{
    /// <summary>
    /// Reusable package card — grid or list layout.
    /// </summary>
    public class PackageCard : VisualElement
    {
        private readonly InstalledPackageInfo _packageInfo;
        private readonly Action<InstalledPackageInfo> _onClick;

        public PackageCard(InstalledPackageInfo packageInfo, Action<InstalledPackageInfo> onClick, bool isGridView = true)
        {
            _packageInfo = packageInfo;
            _onClick = onClick;

            AddToClassList("package-card");
            AddToClassList(isGridView ? "package-card-grid" : "package-card-list");

            if (isGridView) BuildGridCard();
            else BuildListCard();

            this.AddManipulator(new Clickable(OnCardClicked));
        }

        // ─── Grid card ───────────────────────────────────────────────────────
        private void BuildGridCard()
        {
            // Icon area — full width, fixed height
            var iconArea = new VisualElement();
            iconArea.AddToClassList("package-card-icon");
            iconArea.AddToClassList("package-card-grid-icon");
            SetupIcon(iconArea);
            Add(iconArea);

            // Content area below icon
            var content = new VisualElement();
            content.AddToClassList("package-card-content");
            content.AddToClassList("package-card-grid-content");
            Add(content);

            // Name + verified badge row
            var nameRow = new VisualElement();
            nameRow.AddToClassList("package-card-name-row");

            var nameLabel = new Label(_packageInfo.packageName ?? "Unknown Package");
            nameLabel.AddToClassList("package-card-name");
            nameRow.Add(nameLabel);

            if (_packageInfo.isVerified)
                nameRow.Add(BuildVerifiedBadge());

            content.Add(nameRow);

            // Author
            if (!string.IsNullOrEmpty(_packageInfo.author))
            {
                var author = new Label(_packageInfo.author);
                author.AddToClassList("package-card-author");
                content.Add(author);
            }

            // Tagline (1 line, if available)
            if (!string.IsNullOrEmpty(_packageInfo.tagline))
            {
                var tagline = new Label(_packageInfo.tagline);
                tagline.AddToClassList("package-card-author");
                tagline.style.fontSize = 10;
                tagline.style.marginTop = 2;
                tagline.style.opacity = 0.6f;
                tagline.style.overflow = Overflow.Hidden;
                tagline.style.textOverflow = TextOverflow.Ellipsis;
                tagline.style.whiteSpace = WhiteSpace.NoWrap;
                content.Add(tagline);
            }

            // Version + update badge row
            var metaRow = new VisualElement();
            metaRow.style.flexDirection = FlexDirection.Row;
            metaRow.style.alignItems = Align.Center;
            metaRow.style.marginTop = 5;

            // Category badge
            if (!string.IsNullOrEmpty(_packageInfo.category) && _packageInfo.category != "None")
            {
                var catBadge = new VisualElement();
                catBadge.AddToClassList("yucp-chip");
                catBadge.AddToClassList("yucp-chip-category");
                catBadge.style.paddingTop = 1;
                catBadge.style.paddingBottom = 1;
                catBadge.style.paddingLeft = 6;
                catBadge.style.paddingRight = 6;
                catBadge.style.marginRight = 6;
                var catLabel = new Label(_packageInfo.category);
                catLabel.style.fontSize = 9;
                catBadge.Add(catLabel);
                metaRow.Add(catBadge);
            }

            if (!string.IsNullOrEmpty(_packageInfo.installedVersion))
            {
                var version = new Label($"v{_packageInfo.installedVersion}");
                version.AddToClassList("package-card-version");
                version.style.flexGrow = 1;
                metaRow.Add(version);
            }

            if (_packageInfo.hasUpdate)
            {
                var updateBadge = new Label("Update");
                updateBadge.AddToClassList("package-card-update-badge");
                metaRow.Add(updateBadge);
            }

            content.Add(metaRow);
        }

        // ─── List card ───────────────────────────────────────────────────────
        private void BuildListCard()
        {
            // Icon
            var iconArea = new VisualElement();
            iconArea.AddToClassList("package-card-icon");
            SetupIcon(iconArea);
            Add(iconArea);

            // Content
            var content = new VisualElement();
            content.AddToClassList("package-card-content");
            Add(content);

            // Name + verified
            var nameRow = new VisualElement();
            nameRow.AddToClassList("package-card-name-row");

            var nameLabel = new Label(_packageInfo.packageName ?? "Unknown Package");
            nameLabel.AddToClassList("package-card-name");
            nameRow.Add(nameLabel);

            if (_packageInfo.isVerified)
                nameRow.Add(BuildVerifiedBadge());

            content.Add(nameRow);

            // Author
            if (!string.IsNullOrEmpty(_packageInfo.author))
            {
                var author = new Label(_packageInfo.author);
                author.AddToClassList("package-card-author");
                content.Add(author);
            }

            // Tagline (compact, for list view)
            if (!string.IsNullOrEmpty(_packageInfo.tagline))
            {
                var tagline = new Label(_packageInfo.tagline);
                tagline.AddToClassList("package-card-author");
                tagline.style.fontSize = 10;
                tagline.style.marginTop = 1;
                tagline.style.opacity = 0.6f;
                tagline.style.overflow = Overflow.Hidden;
                tagline.style.textOverflow = TextOverflow.Ellipsis;
                tagline.style.whiteSpace = WhiteSpace.NoWrap;
                content.Add(tagline);
            }

            // Right side: category + version + update badge
            var rightSide = new VisualElement();
            rightSide.style.flexDirection = FlexDirection.Row;
            rightSide.style.alignItems = Align.Center;
            rightSide.style.flexShrink = 0;
            rightSide.style.marginLeft = 12;

            // Category chip
            if (!string.IsNullOrEmpty(_packageInfo.category) && _packageInfo.category != "None")
            {
                var catBadge = new VisualElement();
                catBadge.AddToClassList("yucp-chip");
                catBadge.AddToClassList("yucp-chip-category");
                catBadge.style.paddingTop = 1;
                catBadge.style.paddingBottom = 1;
                catBadge.style.paddingLeft = 6;
                catBadge.style.paddingRight = 6;
                catBadge.style.marginRight = 8;
                var catLabel = new Label(_packageInfo.category);
                catLabel.style.fontSize = 9;
                catBadge.Add(catLabel);
                rightSide.Add(catBadge);
            }

            if (!string.IsNullOrEmpty(_packageInfo.installedVersion))
            {
                var version = new Label($"v{_packageInfo.installedVersion}");
                version.AddToClassList("package-card-version");
                version.style.marginRight = 8;
                rightSide.Add(version);
            }

            if (_packageInfo.hasUpdate)
            {
                var updateBadge = new Label("Update");
                updateBadge.AddToClassList("package-card-update-badge");
                rightSide.Add(updateBadge);
            }

            Add(rightSide);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────
        private void SetupIcon(VisualElement container)
        {
            if (_packageInfo.icon != null)
            {
                var img = new Image { image = _packageInfo.icon };
                img.AddToClassList("package-card-icon-image");
                container.Add(img);
            }
            else
            {
                var placeholder = new Image();
                var builtIn = EditorGUIUtility.IconContent("Package Manager");
                if (builtIn?.image is Texture2D tex)
                    placeholder.image = tex;
                else
                    placeholder.style.backgroundColor = new Color(0.18f, 0.18f, 0.22f, 0.5f);
                placeholder.AddToClassList("package-card-icon-placeholder");
                container.Add(placeholder);
            }
        }

        private VisualElement BuildVerifiedBadge()
        {
            var badge = new VisualElement();
            badge.AddToClassList("package-card-verified-badge");

            string iconPath = "Packages/com.yucp.importer/Editor/PackageManager/Resources/Verified.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (tex != null)
            {
                var img = new Image { image = tex };
                img.style.width = 14;
                img.style.height = 14;
                badge.Add(img);
            }
            else
            {
                var check = new Label("✓");
                check.style.fontSize = 11;
                check.style.color = new Color(0.30f, 0.85f, 0.50f);
                badge.Add(check);
            }

            return badge;
        }

        private void OnCardClicked(EventBase evt) => _onClick?.Invoke(_packageInfo);
    }
}
