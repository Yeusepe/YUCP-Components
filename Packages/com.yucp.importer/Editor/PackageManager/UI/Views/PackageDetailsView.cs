using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Importer.Editor.PackageManager
{
    /// <summary>
    /// Details view for an installed package — modern, clean layout.
    /// </summary>
    public class PackageDetailsView : VisualElement
    {
        private readonly InstalledPackageInfo _packageInfo;
        private readonly Action _onBack;
        private readonly Action<InstalledPackageInfo> _onUpdate;
        private readonly Action<InstalledPackageInfo> _onUninstall;

        private VisualElement _bannerContainer;
        private VisualElement _bannerImageContainer;
        private VisualElement _bannerGradientOverlay;
        private Texture2D _bannerGradientTexture;

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
            var scrollView = new ScrollView();
            scrollView.style.flexGrow = 1;
            Add(scrollView);

            var sc = scrollView.contentContainer;
            sc.style.flexDirection = FlexDirection.Column;
            sc.style.position = Position.Relative;
            sc.AddToClassList("yucp-scroll-content");

            // Banner (absolutely positioned behind content)
            _bannerContainer = BuildBanner();
            _bannerContainer.style.position = Position.Absolute;
            _bannerContainer.style.top = 0;
            _bannerContainer.style.left = 0;
            _bannerContainer.style.right = 0;
            sc.Add(_bannerContainer);
            _bannerContainer.SendToBack();

            // Spacer that pushes cards down under the banner
            var spacer = new VisualElement();
            spacer.AddToClassList("yucp-spacer");
            sc.Add(spacer);

            // Content wrapper — cards stack here
            var wrapper = new VisualElement();
            wrapper.AddToClassList("yucp-content-wrapper");
            sc.Add(wrapper);

            wrapper.Add(BuildMetadataCard());
            wrapper.Add(BuildInfoCard());

            if (_packageInfo.dependencies != null && _packageInfo.dependencies.Count > 0)
                wrapper.Add(BuildDependenciesCard());

            wrapper.Add(BuildFilesCard());

            schedule.Execute(() => CreateBannerGradient());
        }

        // ─── Banner ──────────────────────────────────────────────────────────
        private VisualElement BuildBanner()
        {
            var c = new VisualElement();
            c.AddToClassList("yucp-banner-container");
            c.style.position = Position.Relative;
            c.style.height = Length.Percent(75);
            c.style.width = Length.Percent(100);
            c.style.flexShrink = 0;
            c.style.overflow = Overflow.Hidden;

            _bannerImageContainer = new VisualElement();
            _bannerImageContainer.AddToClassList("yucp-banner-image-container");
            _bannerImageContainer.style.position = Position.Absolute;
            _bannerImageContainer.style.top = 0;
            _bannerImageContainer.style.left = 0;
            _bannerImageContainer.style.right = 0;
            _bannerImageContainer.style.bottom = 0;

            var banner = _packageInfo?.banner ?? GetPlaceholder();
            if (banner != null)
                _bannerImageContainer.style.backgroundImage = new StyleBackground(banner);

            c.Add(_bannerImageContainer);

            _bannerGradientOverlay = new VisualElement();
            _bannerGradientOverlay.AddToClassList("yucp-banner-gradient-overlay");
            _bannerGradientOverlay.style.position = Position.Absolute;
            _bannerGradientOverlay.style.top = 0;
            _bannerGradientOverlay.style.left = 0;
            _bannerGradientOverlay.style.right = 0;
            _bannerGradientOverlay.style.bottom = 0;
            _bannerGradientOverlay.pickingMode = PickingMode.Ignore;
            c.Add(_bannerGradientOverlay);

            return c;
        }

        private void CreateBannerGradient()
        {
            if (_bannerContainer == null) return;

            float h = _bannerContainer.resolvedStyle.height;
            if (h <= 0) h = _bannerContainer.layout.height;
            if (h <= 0) h = 340;

            int height = Mathf.RoundToInt(h);
            if (_bannerGradientTexture != null)
                UnityEngine.Object.DestroyImmediate(_bannerGradientTexture);

            _bannerGradientTexture = new Texture2D(4, height, TextureFormat.RGBA32, false);
            var end = new Color(0.067f, 0.067f, 0.078f);

            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1);
                var c = new Color(end.r, end.g, end.b, t);
                for (int x = 0; x < 4; x++)
                    _bannerGradientTexture.SetPixel(x, height - 1 - y, c);
            }
            _bannerGradientTexture.Apply();
            _bannerGradientTexture.wrapMode = TextureWrapMode.Clamp;

            if (_bannerGradientOverlay != null)
            {
                _bannerGradientOverlay.style.backgroundImage = new StyleBackground(_bannerGradientTexture);
                _bannerGradientOverlay.MarkDirtyRepaint();
            }
        }

        // ─── Metadata card ───────────────────────────────────────────────────
        private VisualElement BuildMetadataCard()
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-section");
            card.AddToClassList("yucp-installer-summary");
            card.style.marginBottom = 10;

            // Header row: icon | info | buttons
            var header = new VisualElement();
            header.AddToClassList("yucp-metadata-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.FlexStart;

            // Icon
            var iconWrap = new VisualElement();
            iconWrap.AddToClassList("yucp-metadata-icon-container");
            var iconImgWrap = new VisualElement();
            iconImgWrap.AddToClassList("yucp-metadata-icon-image-container");
            var icon = new Image { image = _packageInfo?.icon ?? GetPlaceholder() };
            icon.AddToClassList("yucp-metadata-icon-image");
            iconImgWrap.Add(icon);
            iconWrap.Add(iconImgWrap);
            header.Add(iconWrap);

            // Name / author / version column
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.style.marginLeft = 16;
            col.style.minWidth = 0;
            col.style.overflow = Overflow.Hidden;

            // "Package" eyebrow
            var eyebrow = new Label("Installed Package");
            eyebrow.AddToClassList("yucp-package-context-label");
            col.Add(eyebrow);

            // Name + badges row
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems = Align.Center;
            nameRow.style.flexShrink = 1;
            nameRow.style.minWidth = 0;

            string name = string.IsNullOrEmpty(_packageInfo?.packageName) ? "Unknown Package" : _packageInfo.packageName;
            var nameLabel = new Label(name);
            nameLabel.AddToClassList("yucp-metadata-name-field");
            nameLabel.AddToClassList("yucp-ellipsis-text");
            nameLabel.style.flexShrink = 1;
            nameLabel.style.minWidth = 0;
            nameLabel.tooltip = name;
            nameRow.Add(nameLabel);

            if (_packageInfo.isVerified)
            {
                var vBadge = new Label("✓ Verified");
                vBadge.style.marginLeft = 8;
                vBadge.style.fontSize = 11;
                vBadge.style.color = new Color(0.30f, 0.85f, 0.50f);
                vBadge.style.flexShrink = 0;
                nameRow.Add(vBadge);
            }

            if (_packageInfo.hasUpdate)
            {
                var uBadge = new Label("Update");
                uBadge.AddToClassList("package-card-update-badge");
                uBadge.style.marginLeft = 8;
                nameRow.Add(uBadge);
            }

            col.Add(nameRow);

            if (!string.IsNullOrEmpty(_packageInfo?.author))
            {
                var author = new Label(_packageInfo.author);
                author.AddToClassList("yucp-package-author-label");
                author.AddToClassList("yucp-ellipsis-text");
                author.tooltip = _packageInfo.author;
                col.Add(author);
            }

            var versionRow = new VisualElement();
            versionRow.AddToClassList("yucp-version-row");
            versionRow.style.flexDirection = FlexDirection.Row;
            versionRow.style.alignItems = Align.Center;

            var versionLabel = new Label("Version:");
            versionLabel.style.marginRight = 6;
            versionRow.Add(versionLabel);

            var versionValue = new Label(_packageInfo?.installedVersion ?? "—");
            versionRow.Add(versionValue);

            col.Add(versionRow);
            header.Add(col);

            // Action buttons
            var buttons = new VisualElement();
            buttons.AddToClassList("yucp-action-buttons-container");
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.alignItems = Align.FlexStart;
            buttons.style.marginLeft = 14;
            buttons.style.flexShrink = 0;

            var backBtn = new Button(() => _onBack?.Invoke()) { text = "Back" };
            backBtn.AddToClassList("yucp-action-button");
            backBtn.AddToClassList("yucp-cancel-button");
            buttons.Add(backBtn);

            if (_packageInfo.hasUpdate)
            {
                var updateBtn = new Button(() => _onUpdate?.Invoke(_packageInfo)) { text = "Update" };
                updateBtn.AddToClassList("yucp-action-button");
                updateBtn.AddToClassList("yucp-import-button");
                buttons.Add(updateBtn);
            }

            var uninstallBtn = new Button(() => _onUninstall?.Invoke(_packageInfo)) { text = "Uninstall" };
            uninstallBtn.AddToClassList("yucp-action-button");
            uninstallBtn.AddToClassList("yucp-cancel-button");
            buttons.Add(uninstallBtn);

            header.Add(buttons);
            card.Add(header);

            // Description
            if (!string.IsNullOrEmpty(_packageInfo?.description))
            {
                var desc = new Label(_packageInfo.description);
                desc.AddToClassList("yucp-package-description");
                desc.style.marginTop = 12;
                card.Add(desc);
            }

            // Product links
            if (_packageInfo?.productLinks != null && _packageInfo.productLinks.Count > 0)
            {
                var links = new VisualElement();
                links.style.flexDirection = FlexDirection.Row;
                links.style.flexWrap = Wrap.Wrap;
                links.style.marginTop = 14;

                foreach (var link in _packageInfo.productLinks)
                {
                    if (string.IsNullOrEmpty(link.url)) continue;

                    var btn = new Button(() => Application.OpenURL(link.url));
                    btn.style.flexDirection = FlexDirection.Row;
                    btn.style.alignItems = Align.Center;
                    btn.style.marginRight = 8;
                    btn.style.marginBottom = 6;
                    btn.style.paddingLeft = 10;
                    btn.style.paddingRight = 12;
                    btn.style.paddingTop = 5;
                    btn.style.paddingBottom = 5;
                    btn.style.backgroundColor = new Color(1, 1, 1, 0.06f);
                    btn.style.borderTopWidth = 1;
                    btn.style.borderRightWidth = 1;
                    btn.style.borderBottomWidth = 1;
                    btn.style.borderLeftWidth = 1;
                    btn.style.borderTopColor = new Color(1, 1, 1, 0.10f);
                    btn.style.borderRightColor = new Color(1, 1, 1, 0.10f);
                    btn.style.borderBottomColor = new Color(1, 1, 1, 0.10f);
                    btn.style.borderLeftColor = new Color(1, 1, 1, 0.10f);
                    btn.style.borderTopLeftRadius = 8;
                    btn.style.borderTopRightRadius = 8;
                    btn.style.borderBottomLeftRadius = 8;
                    btn.style.borderBottomRightRadius = 8;

                    var linkIcon = link.GetDisplayIcon();
                    if (linkIcon != null)
                    {
                        var img = new Image { image = linkIcon };
                        img.style.width = 14;
                        img.style.height = 14;
                        img.style.marginRight = 7;
                        btn.Add(img);
                    }

                    var lbl = new Label(string.IsNullOrEmpty(link.label) ? link.url : link.label);
                    lbl.style.fontSize = 12;
                    lbl.style.color = new Color(0.85f, 0.85f, 0.90f);
                    btn.Add(lbl);

                    links.Add(btn);
                }

                card.Add(links);
            }

            return card;
        }

        // ─── Package info card ────────────────────────────────────────────────
        private VisualElement BuildInfoCard()
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-section");
            card.style.marginBottom = 10;

            var title = new Label("Package Information");
            title.AddToClassList("yucp-section-title");
            card.Add(title);

            var rows = new VisualElement();

            AddInfoRow(rows, "Version", _packageInfo.installedVersion ?? "Unknown");

            var dt = _packageInfo.GetInstalledDateTime();
            AddInfoRow(rows, "Installed", dt != DateTime.MinValue ? dt.ToString("yyyy-MM-dd  HH:mm") : "Unknown");

            long totalSize = CalculateSize(_packageInfo.installedFiles);
            AddInfoRow(rows, "Size", totalSize > 0 ? FormatBytes(totalSize) : "Unknown");

            if (!string.IsNullOrEmpty(_packageInfo.publisherId))
                AddInfoRow(rows, "Publisher", _packageInfo.publisherId);

            if (!string.IsNullOrEmpty(_packageInfo.packageId))
                AddInfoRow(rows, "Package ID", _packageInfo.packageId);

            if (!string.IsNullOrEmpty(_packageInfo.archiveSha256))
            {
                string hash = _packageInfo.archiveSha256;
                AddInfoRow(rows, "Archive Hash",
                    hash.Substring(0, Math.Min(16, hash.Length)) + (hash.Length > 16 ? "…" : ""));
            }

            card.Add(rows);
            return card;
        }

        private void AddInfoRow(VisualElement container, string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-info-row");

            var lbl = new Label(label);
            lbl.AddToClassList("yucp-info-label");
            row.Add(lbl);

            var val = new Label(value);
            val.AddToClassList("yucp-info-value");
            row.Add(val);

            container.Add(row);
        }

        // ─── Dependencies card ────────────────────────────────────────────────
        private VisualElement BuildDependenciesCard()
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-section");
            card.style.marginBottom = 10;

            var title = new Label("Dependencies");
            title.AddToClassList("yucp-section-title");
            card.Add(title);

            foreach (var dep in _packageInfo.dependencies)
            {
                var row = new VisualElement();
                row.AddToClassList("yucp-dep-row");

                var name = new Label(dep.Key);
                name.AddToClassList("yucp-dep-name");
                row.Add(name);

                var ver = new Label($"v{dep.Value}");
                ver.AddToClassList("yucp-dep-version");
                row.Add(ver);

                card.Add(row);
            }

            return card;
        }

        // ─── Installed files card ─────────────────────────────────────────────
        private VisualElement BuildFilesCard()
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-section");
            card.style.marginBottom = 10;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 12;

            var title = new Label("Installed Files");
            title.AddToClassList("yucp-section-title");
            title.style.marginBottom = 0;
            title.style.flexGrow = 1;
            header.Add(title);

            int count = _packageInfo.installedFiles?.Count ?? 0;
            var countBadge = new Label(count.ToString());
            countBadge.style.fontSize = 11;
            countBadge.style.color = new Color(0.60f, 0.60f, 0.65f);
            countBadge.style.backgroundColor = new Color(1, 1, 1, 0.06f);
            countBadge.style.paddingLeft = 8;
            countBadge.style.paddingRight = 8;
            countBadge.style.paddingTop = 2;
            countBadge.style.paddingBottom = 2;
            countBadge.style.borderTopLeftRadius = 8;
            countBadge.style.borderTopRightRadius = 8;
            countBadge.style.borderBottomLeftRadius = 8;
            countBadge.style.borderBottomRightRadius = 8;
            header.Add(countBadge);

            card.Add(header);

            if (count == 0)
            {
                var empty = new Label("No files recorded.");
                empty.style.fontSize = 12;
                empty.style.color = new Color(0.55f, 0.55f, 0.60f);
                card.Add(empty);
                return card;
            }

            // Group files by top-level folder for cleaner display
            var scroll = new ScrollView();
            scroll.style.maxHeight = 380;
            scroll.style.minHeight = 80;

            var fileList = new VisualElement();
            fileList.style.flexDirection = FlexDirection.Column;

            // Sort and group
            var sorted = new List<string>(_packageInfo.installedFiles);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);

            string lastFolder = null;
            foreach (var filePath in sorted)
            {
                string folder = GetTopFolder(filePath);

                // Folder separator label
                if (folder != lastFolder && !string.IsNullOrEmpty(folder))
                {
                    if (lastFolder != null)
                    {
                        var sep = new VisualElement();
                        sep.style.height = 1;
                        sep.style.backgroundColor = new Color(1, 1, 1, 0.05f);
                        sep.style.marginTop = 4;
                        sep.style.marginBottom = 4;
                        fileList.Add(sep);
                    }

                    var folderLabel = new Label("▸  " + folder);
                    folderLabel.style.fontSize = 11;
                    folderLabel.style.color = new Color(0.42f, 0.62f, 1.0f);
                    folderLabel.style.marginBottom = 3;
                    folderLabel.style.marginTop = 2;
                    fileList.Add(folderLabel);
                    lastFolder = folder;
                }

                var row = new VisualElement();
                row.AddToClassList("yucp-file-item");
                row.style.paddingLeft = 16;

                var dot = new Label("·");
                dot.AddToClassList("yucp-file-item-icon");
                row.Add(dot);

                var pathLabel = new Label(GetFileName(filePath));
                pathLabel.AddToClassList("yucp-file-item-path");
                pathLabel.tooltip = filePath;
                row.Add(pathLabel);

                fileList.Add(row);
            }

            scroll.Add(fileList);
            card.Add(scroll);

            return card;
        }

        // ─── Utilities ────────────────────────────────────────────────────────
        private static Texture2D GetPlaceholder() =>
            AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DefaultGrid.png");

        private static long CalculateSize(System.Collections.Generic.List<string> files)
        {
            if (files == null) return 0;
            long total = 0;
            foreach (var f in files)
            {
                try { if (File.Exists(f)) total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
        }

        private static string GetTopFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string normalized = path.Replace('\\', '/');
            int slash = normalized.IndexOf('/');
            if (slash < 0) return string.Empty;
            int second = normalized.IndexOf('/', slash + 1);
            return second < 0 ? normalized.Substring(0, slash) : normalized.Substring(0, second);
        }

        private static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.Replace('\\', '/').Split('/')[^1];
        }
    }
}
