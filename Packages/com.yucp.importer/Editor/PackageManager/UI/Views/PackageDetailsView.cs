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
            card.AddToClassList("yucp-product-panel");
            card.style.marginBottom = 10;

            // ── Header: Icon + Title Block + Action Buttons ──
            var header = new VisualElement();
            header.AddToClassList("yucp-product-header");

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

            // Title block
            var titleBlock = new VisualElement();
            titleBlock.AddToClassList("yucp-product-title-block");

            string name = string.IsNullOrEmpty(_packageInfo?.packageName) ? "Unknown Package" : _packageInfo.packageName;
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems = Align.Center;
            nameRow.style.minWidth = 0;

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("yucp-product-name");
            nameLabel.tooltip = name;
            nameLabel.style.flexShrink = 1;
            nameLabel.style.minWidth = 0;
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

            titleBlock.Add(nameRow);

            // Author + version inline
            string authorLine = "";
            if (!string.IsNullOrEmpty(_packageInfo?.author))
                authorLine += $"By {_packageInfo.author}";
            string ver = _packageInfo?.installedVersion ?? _packageInfo?.version;
            if (!string.IsNullOrEmpty(ver))
            {
                if (authorLine.Length > 0) authorLine += "  ·  ";
                authorLine += $"v{ver}";
            }
            if (authorLine.Length > 0)
            {
                var authorLabel = new Label(authorLine);
                authorLabel.AddToClassList("yucp-product-author");
                authorLabel.tooltip = authorLine;
                titleBlock.Add(authorLabel);
            }

            // Tagline
            if (!string.IsNullOrEmpty(_packageInfo?.tagline))
            {
                var taglineLabel = new Label(_packageInfo.tagline);
                taglineLabel.AddToClassList("yucp-product-tagline");
                titleBlock.Add(taglineLabel);
            }

            header.Add(titleBlock);

            // Action buttons column
            var ctaCol = new VisualElement();
            ctaCol.AddToClassList("yucp-cta-column");

            var backBtn = new Button(() => _onBack?.Invoke()) { text = "← Back" };
            backBtn.AddToClassList("yucp-cta-cancel");
            ctaCol.Add(backBtn);

            if (_packageInfo.hasUpdate)
            {
                var updateBtn = new Button(() => _onUpdate?.Invoke(_packageInfo)) { text = "↓  Update" };
                updateBtn.AddToClassList("yucp-cta-button");
                updateBtn.style.marginTop = 6;
                ctaCol.Add(updateBtn);
            }

            var uninstallBtn = new Button(() => _onUninstall?.Invoke(_packageInfo)) { text = "Uninstall" };
            uninstallBtn.AddToClassList("yucp-cta-cancel");
            uninstallBtn.style.marginTop = 6;
            ctaCol.Add(uninstallBtn);

            header.Add(ctaCol);
            card.Add(header);

            // ── Chip row: category + platforms + tags + derived safety badges ──
            {
                var chips = new List<(string text, string variant)>();

                if (!string.IsNullOrEmpty(_packageInfo?.category) && _packageInfo.category != "None")
                    chips.Add((_packageInfo.category, "yucp-chip-category"));
                if (_packageInfo?.supportedPlatforms != null)
                    foreach (var p in _packageInfo.supportedPlatforms)
                        if (!string.IsNullOrEmpty(p)) chips.Add((p, "yucp-chip-platform"));
                if (_packageInfo?.tags != null)
                    foreach (var tag in _packageInfo.tags.Where(t => !string.IsNullOrEmpty(t)).Take(6))
                        chips.Add((tag, "yucp-chip-content"));
                foreach (var safetyChip in GetDerivedSafetyChips())
                    chips.Add((safetyChip, ""));

                if (chips.Count > 0)
                {
                    var chipRow = new VisualElement();
                    chipRow.AddToClassList("yucp-chip-row");
                    foreach (var (text, variant) in chips)
                    {
                        var chip = new VisualElement();
                        chip.AddToClassList("yucp-chip");
                        if (!string.IsNullOrEmpty(variant)) chip.AddToClassList(variant);
                        chip.Add(new Label(text));
                        chipRow.Add(chip);
                    }
                    card.Add(chipRow);
                }
            }

            // ── Description ──
            if (!string.IsNullOrEmpty(_packageInfo?.description))
            {
                var desc = new Label(_packageInfo.description);
                desc.AddToClassList("yucp-package-description");
                desc.style.marginTop = 12;
                card.Add(desc);
            }

            // ── What's Inside (asset breakdown) ──
            bool hasBreakdown = _packageInfo?.assetBreakdown != null && _packageInfo.assetBreakdown.Count > 0;
            if (hasBreakdown)
            {
                var insideSection = new VisualElement();
                insideSection.AddToClassList("yucp-info-section-block");
                insideSection.Add(CreateSectionTitle("WHAT'S INSIDE"));

                var statsRow = new VisualElement();
                statsRow.AddToClassList("yucp-info-section-body");
                for (int i = 0; i < _packageInfo.assetBreakdown.Count; i++)
                {
                    var ab = _packageInfo.assetBreakdown[i];
                    if (i > 0)
                    {
                        var sep = new Label("·");
                        sep.AddToClassList("yucp-asset-stat-separator");
                        statsRow.Add(sep);
                    }
                    var stat = new Label($"{ab.count} {ab.type}{(ab.count != 1 ? "s" : "")}");
                    stat.AddToClassList("yucp-asset-stat");
                    statsRow.Add(stat);
                }
                insideSection.Add(statsRow);

                if (!string.IsNullOrEmpty(_packageInfo?.minimumUnityVersion))
                {
                    var unityLabel = new Label($"Unity {_packageInfo.minimumUnityVersion}+");
                    unityLabel.AddToClassList("yucp-requirement-text");
                    insideSection.Add(unityLabel);
                }

                card.Add(insideSection);
            }

            // ── From the Creator ──
            bool hasCreatorNote = !string.IsNullOrEmpty(_packageInfo?.creatorNote);
            bool hasProductLinks = _packageInfo?.productLinks != null && _packageInfo.productLinks.Count > 0;
            if (hasCreatorNote || hasProductLinks)
            {
                var creatorSection = new VisualElement();
                creatorSection.AddToClassList("yucp-info-section-block");
                creatorSection.Add(CreateSectionTitle("FROM THE CREATOR"));

                if (hasCreatorNote)
                {
                    var noteLabel = new Label($"\"{_packageInfo.creatorNote}\"");
                    noteLabel.AddToClassList("yucp-creator-note");
                    creatorSection.Add(noteLabel);
                }

                if (hasProductLinks)
                {
                    var linksRow = new VisualElement();
                    linksRow.AddToClassList("yucp-creator-links");
                    foreach (var link in _packageInfo.productLinks)
                    {
                        if (string.IsNullOrEmpty(link.url)) continue;
                        var btn = new Button(() => Application.OpenURL(link.url));
                        btn.AddToClassList("yucp-creator-link-button");
                        btn.tooltip = string.IsNullOrEmpty(link.label) ? link.url : $"{link.label}\n{link.url}";
                        var linkIcon = link.GetDisplayIcon() ?? GetPlaceholder();
                        var img = new Image { image = linkIcon };
                        img.style.width = 28;
                        img.style.height = 28;
                        btn.Add(img);
                        linksRow.Add(btn);
                    }
                    creatorSection.Add(linksRow);
                }

                card.Add(creatorSection);
            }

            // ── Release Notes ──
            if (!string.IsNullOrEmpty(_packageInfo?.releaseNotes))
            {
                var relSection = new VisualElement();
                relSection.AddToClassList("yucp-info-section-block");
                string verSuffix = !string.IsNullOrEmpty(ver) ? $" (v{ver})" : "";
                relSection.Add(CreateSectionTitle($"WHAT'S NEW{verSuffix}"));
                var notes = new Label(_packageInfo.releaseNotes);
                notes.AddToClassList("yucp-release-notes-text");
                relSection.Add(notes);
                card.Add(relSection);
            }

            // ── Gallery ──
            if (_packageInfo?.galleryImages != null && _packageInfo.galleryImages.Count > 0)
            {
                var gallery = new VisualElement();
                gallery.AddToClassList("yucp-gallery-strip");
                foreach (var tex in _packageInfo.galleryImages)
                {
                    if (tex == null) continue;
                    var thumb = new VisualElement();
                    thumb.AddToClassList("yucp-gallery-thumb");
                    thumb.style.backgroundImage = new StyleBackground(tex);
                    var capturedTex = tex;
                    thumb.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (_bannerImageContainer != null)
                            _bannerImageContainer.style.backgroundImage = new StyleBackground(capturedTex);
                        foreach (var child in gallery.Children())
                            child.RemoveFromClassList("yucp-gallery-thumb-selected");
                        thumb.AddToClassList("yucp-gallery-thumb-selected");
                    });
                    gallery.Add(thumb);
                }
                card.Add(gallery);
            }

            return card;
        }

        private static Label CreateSectionTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("yucp-info-section-title");
            return label;
        }

        private IEnumerable<string> GetDerivedSafetyChips()
        {
            var chips = new List<string>();

            bool hasAssetBreakdown = _packageInfo?.assetBreakdown != null && _packageInfo.assetBreakdown.Count > 0;
            bool hasAssemblies = hasAssetBreakdown && _packageInfo.assetBreakdown.Any(ab =>
                string.Equals(ab.type, "Assembly", StringComparison.OrdinalIgnoreCase));

            if (hasAssetBreakdown)
                chips.Add(hasAssemblies ? "Contains DLLs" : "No DLLs");

            if (_packageInfo?.dependencies != null && _packageInfo.dependencies.Count > 0)
                chips.Add("Dependencies Required");

            if (_packageInfo?.licensePackages != null && _packageInfo.licensePackages.Count > 0)
                chips.Add("Protected Assets");

            if (_packageInfo?.isVerified == true)
                chips.Add("Verified Package");

            return chips;
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
            if (container.childCount > 0 && container[container.childCount - 1] is VisualElement previousRow)
            {
                previousRow.style.borderBottomWidth = 1;
                previousRow.style.paddingBottom = 8;
            }

            var row = new VisualElement();
            row.AddToClassList("yucp-info-row");

            var lbl = new Label(label);
            lbl.AddToClassList("yucp-info-label");
            row.Add(lbl);

            var val = new Label(value);
            val.AddToClassList("yucp-info-value");
            row.Add(val);

            container.Add(row);
            row.style.borderBottomWidth = 0;
            row.style.paddingBottom = 0;
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
