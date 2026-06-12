#if !YUCP_PACKAGE_MANAGER_DISABLED
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal sealed class YucpEditorDialog : EditorWindow
    {
        private const float DialogWidth = 560f;
        private const float DialogMinHeight = 380f;
        private const float DialogMaxHeight = 640f;
        private const float BannerHeight = 210f;
        private const string StyleSheetPath = "Packages/com.yucp.importer/Editor/PackageManager/Styles/PackageManager.uss";

        private string _title;
        private string _message;
        private string _ok;
        private string _cancel;
        private bool _hasCancel;
        private bool _isErrorDialog;
        private bool _result;
        private bool _responded;
        private Texture2D _gradientTexture;

        public static bool DisplayDialog(string title, string message, string ok)
        {
            return DisplayDialog(title, message, ok, null);
        }

        public static bool DisplayDialog(string title, string message, string ok, string cancel)
        {
            if (Application.isBatchMode)
            {
                string prefix = string.IsNullOrWhiteSpace(cancel) ? "Dialog" : "Confirmation";
                Debug.Log($"[YUCP PackageManager] {prefix}: {title}\n{message}");
                return string.IsNullOrWhiteSpace(cancel);
            }

            var dialog = CreateDialogWindow(title, message, ok, cancel, isErrorDialog: false);
            dialog.ShowModal();
            return dialog._result;
        }

        public static bool DisplayErrorDialog(string title, string message)
        {
            if (Application.isBatchMode)
            {
                Debug.LogError($"[YUCP PackageManager] Error: {title}\n{message}");
                return true;
            }

            var dialog = CreateErrorDialogWindow(title, message);
            dialog.ShowModal();
            return true;
        }

        internal static YucpEditorDialog CreateErrorDialogWindow(string title, string message)
        {
            return CreateDialogWindow(title, message, "OK", null, isErrorDialog: true);
        }

        private static YucpEditorDialog CreateDialogWindow(
            string title,
            string message,
            string ok,
            string cancel,
            bool isErrorDialog)
        {
            var dialog = CreateInstance<YucpEditorDialog>();
            dialog._title = string.IsNullOrWhiteSpace(title) ? "YUCP Installer" : title.Trim();
            dialog._message = message ?? string.Empty;
            dialog._ok = string.IsNullOrWhiteSpace(ok) ? "OK" : ok.Trim();
            dialog._cancel = string.IsNullOrWhiteSpace(cancel) ? "Cancel" : cancel.Trim();
            dialog._hasCancel = !string.IsNullOrWhiteSpace(cancel);
            dialog._isErrorDialog = isErrorDialog;
            dialog._result = !dialog._hasCancel;
            dialog.titleContent = new GUIContent("YUCP Installer");
            dialog.minSize = new Vector2(DialogWidth, DialogMinHeight);
            dialog.maxSize = new Vector2(DialogWidth, DialogMaxHeight);
            dialog.position = BuildCenteredPosition(dialog._message);
            return dialog;
        }

        private static Rect BuildCenteredPosition(string message)
        {
            int lineCount = string.IsNullOrEmpty(message)
                ? 1
                : message.Split(new[] { '\n' }, StringSplitOptions.None).Length;
            int wrappedLineEstimate = Mathf.CeilToInt((message?.Length ?? 0) / 76f);
            float contentHeight = 302f + Mathf.Max(lineCount, wrappedLineEstimate) * 8f;
            float height = Mathf.Clamp(contentHeight, DialogMinHeight, DialogMaxHeight);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            float x = main.x + Mathf.Max(0f, (main.width - DialogWidth) * 0.5f);
            float y = main.y + Mathf.Max(0f, (main.height - height) * 0.42f);
            return new Rect(x, y, DialogWidth, height);
        }

        internal void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            // Both the confirmation and error dialogs share the dark, banner-led installer surface so
            // they read as one family instead of a stray light-themed popup.
            root.AddToClassList("yucp-dialog-installer-root");

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            if (_isErrorDialog)
            {
                CreateErrorGUI(root);
                return;
            }

            root.Add(CreateBannerSection());

            var content = new VisualElement();
            content.AddToClassList("yucp-dialog-content");

            if (ShouldScrollMessage(_message))
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.AddToClassList("yucp-dialog-body-scroll");
                scroll.Add(CreateMessageLabel());
                content.Add(scroll);
            }
            else
            {
                content.Add(CreateMessageLabel());
            }

            root.Add(content);

            var actions = new VisualElement();
            actions.AddToClassList("yucp-dialog-action-row");

            if (_hasCancel)
            {
                var cancelButton = new Button(() => CloseWithResult(false))
                {
                    text = _cancel
                };
                cancelButton.AddToClassList("yucp-dialog-action");
                cancelButton.AddToClassList("yucp-cta-cancel");
                actions.Add(cancelButton);
            }

            var okButton = new Button(() => CloseWithResult(true))
            {
                text = _ok
            };
            okButton.AddToClassList("yucp-dialog-action");
            okButton.AddToClassList("yucp-cta-button");
            actions.Add(okButton);
            root.Add(actions);

            root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            okButton.Focus();
        }

        private void CreateErrorGUI(VisualElement root)
        {
            // Reuse the installer dialog's banner/hero so the error screen matches the rest of the UI.
            root.Add(CreateBannerSection());

            var content = new VisualElement();
            content.AddToClassList("yucp-dialog-content");

            var summary = new Label("The install did not complete. Copy the details below if you need to report it.");
            summary.AddToClassList("yucp-error-dialog-summary");
            content.Add(summary);

            var detailsScroll = new ScrollView(ScrollViewMode.Vertical);
            detailsScroll.AddToClassList("yucp-error-dialog-details-scroll");

            var details = new TextField
            {
                name = "yucp-error-dialog-details",
                multiline = true,
                isReadOnly = true,
                value = string.IsNullOrWhiteSpace(_message) ? "The install did not complete." : _message
            };
            details.AddToClassList("yucp-error-dialog-details");
            detailsScroll.Add(details);
            content.Add(detailsScroll);

            root.Add(content);

            var actions = new VisualElement();
            actions.AddToClassList("yucp-dialog-action-row");

            var copyButton = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = BuildCopyableErrorText();
            })
            {
                name = "yucp-error-dialog-copy-button",
                text = "Copy Error"
            };
            copyButton.AddToClassList("yucp-dialog-action");
            copyButton.AddToClassList("yucp-cta-cancel");
            actions.Add(copyButton);

            var okButton = new Button(() => CloseWithResult(true))
            {
                name = "yucp-error-dialog-ok-button",
                text = _ok
            };
            okButton.AddToClassList("yucp-dialog-action");
            okButton.AddToClassList("yucp-cta-button");
            actions.Add(okButton);
            root.Add(actions);

            root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            okButton.Focus();
        }

        private string BuildCopyableErrorText()
        {
            return $"{_title}\n\n{(string.IsNullOrWhiteSpace(_message) ? "The install did not complete." : _message)}";
        }

        private VisualElement CreateBannerSection()
        {
            var banner = new VisualElement();
            banner.AddToClassList("yucp-dialog-banner-section");

            var bannerImage = new VisualElement();
            bannerImage.AddToClassList("yucp-dialog-banner-image");
            banner.Add(bannerImage);

            var gradient = new VisualElement();
            gradient.AddToClassList("yucp-dialog-banner-gradient");
            banner.Add(gradient);
            rootVisualElement.schedule.Execute(() => ApplyGradientTexture(gradient));

            var hero = new VisualElement();
            hero.AddToClassList("yucp-dialog-hero-overlay");

            var iconFrame = new VisualElement();
            iconFrame.AddToClassList("yucp-dialog-hero-icon");
            var iconText = new Label("Y");
            iconText.AddToClassList("yucp-dialog-hero-icon-text");
            iconFrame.Add(iconText);
            hero.Add(iconFrame);

            var titleBlock = new VisualElement();
            titleBlock.AddToClassList("yucp-dialog-hero-copy");

            var eyebrow = new Label("YUCP Installer");
            eyebrow.AddToClassList("yucp-dialog-hero-eyebrow");
            titleBlock.Add(eyebrow);

            var title = new Label(_title);
            title.AddToClassList("yucp-dialog-hero-title");
            titleBlock.Add(title);

            hero.Add(titleBlock);
            banner.Add(hero);

            return banner;
        }

        private Label CreateMessageLabel()
        {
            var message = new Label(_message);
            message.AddToClassList("yucp-dialog-body-text");
            return message;
        }

        private static bool ShouldScrollMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            int lineCount = message.Split(new[] { '\n' }, StringSplitOptions.None).Length;
            return lineCount > 8 || message.Length > 900;
        }

        private void ApplyGradientTexture(VisualElement gradient)
        {
            int width = ResolveTextureDimension(gradient.resolvedStyle.width, DialogWidth);
            int height = ResolveTextureDimension(gradient.resolvedStyle.height, BannerHeight);
            _gradientTexture = CreateBannerGradientTexture(width, height);
            gradient.style.backgroundImage = new StyleBackground(_gradientTexture);
        }

        private static int ResolveTextureDimension(float value, float fallback)
        {
            float resolved = float.IsNaN(value) || float.IsInfinity(value) || value < 1f
                ? fallback
                : value;
            return Mathf.Max(1, Mathf.CeilToInt(resolved));
        }

        private static Texture2D CreateBannerGradientTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color tintColor = new Color(60f / 255f, 60f / 255f, 60f / 255f, 1f);
            for (int y = 0; y < height; y++)
            {
                float normalized = height <= 1 ? 1f : y / (float)(height - 1);
                float alpha = Mathf.Lerp(0.12f, 0.92f, normalized);
                var pixel = new Color(tintColor.r, tintColor.g, tintColor.b, alpha);
                for (int x = 0; x < width; x++)
                {
                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply(false, false);
            return texture;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                CloseWithResult(false);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                CloseWithResult(true);
                evt.StopPropagation();
            }
        }

        private void CloseWithResult(bool result)
        {
            _result = result;
            _responded = true;
            Close();
        }

        private void OnDisable()
        {
            if (!_responded && _hasCancel)
            {
                _result = false;
            }

            if (_gradientTexture != null)
            {
                DestroyImmediate(_gradientTexture);
                _gradientTexture = null;
            }
        }
    }
}
#endif
