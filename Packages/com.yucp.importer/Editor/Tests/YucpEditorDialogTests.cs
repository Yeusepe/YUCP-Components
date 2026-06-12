#if !YUCP_PACKAGE_MANAGER_DISABLED
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class YucpEditorDialogTests
    {
        [Test]
        public void CreateErrorDialogWindow_RendersCopyableErrorDetails()
        {
            var dialog = YucpEditorDialog.CreateErrorDialogWindow(
                "Complete YUCP Install",
                "The remote server returned an error: (502) Bad Gateway.");
            try
            {
                dialog.CreateGUI();

                var details = dialog.rootVisualElement.Q<TextField>("yucp-error-dialog-details");
                Assert.That(details, Is.Not.Null);
                Assert.That(details.isReadOnly, Is.True);
                Assert.That(details.multiline, Is.True);
                Assert.That(details.value, Does.Contain("(502) Bad Gateway"));

                var copyButton = dialog.rootVisualElement.Q<Button>("yucp-error-dialog-copy-button");
                Assert.That(copyButton, Is.Not.Null);
                Assert.That(copyButton.text, Is.EqualTo("Copy Error"));
            }
            finally
            {
                Object.DestroyImmediate(dialog);
            }
        }

        [Test]
        public void CreateErrorDialogWindow_SharesInstallerBannerSurface()
        {
            var dialog = YucpEditorDialog.CreateErrorDialogWindow(
                "Complete YUCP Install",
                "The remote server returned an error: (502) Bad Gateway.");
            try
            {
                dialog.CreateGUI();

                // The error dialog now reuses the installer's dark, banner-led chrome instead of a
                // stray light-themed popup, so it reads as one family.
                Assert.That(dialog.rootVisualElement.ClassListContains("yucp-dialog-installer-root"), Is.True);
                Assert.That(dialog.rootVisualElement.Q(className: "yucp-dialog-banner-section"), Is.Not.Null);

                var heroTitle = dialog.rootVisualElement.Q<Label>(className: "yucp-dialog-hero-title");
                Assert.That(heroTitle, Is.Not.Null);
                Assert.That(heroTitle.text, Is.EqualTo("Complete YUCP Install"));

                var copyButton = dialog.rootVisualElement.Q<Button>("yucp-error-dialog-copy-button");
                var okButton = dialog.rootVisualElement.Q<Button>("yucp-error-dialog-ok-button");
                Assert.That(copyButton, Is.Not.Null);
                Assert.That(okButton, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(dialog);
            }
        }
    }
}
#endif
