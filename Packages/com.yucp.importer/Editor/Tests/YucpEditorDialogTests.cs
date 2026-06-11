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
        public void CreateErrorDialogWindow_RendersPlainCopyableErrorDetails()
        {
            var dialog = YucpEditorDialog.CreateErrorDialogWindow(
                "Complete YUCP Install",
                "The remote server returned an error: (502) Bad Gateway.");
            try
            {
                dialog.CreateGUI();

                Assert.That(dialog.rootVisualElement.Q(className: "yucp-dialog-banner-section"), Is.Null);

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
        public void CreateErrorDialogWindow_UsesInstallerErrorSurfaceInsteadOfGenericDarkShell()
        {
            var dialog = YucpEditorDialog.CreateErrorDialogWindow(
                "Complete YUCP Install",
                "The remote server returned an error: (502) Bad Gateway.");
            try
            {
                dialog.CreateGUI();

                Assert.That(dialog.rootVisualElement.Q(className: "yucp-dialog-banner-section"), Is.Null);
                Assert.That(dialog.rootVisualElement.Q(className: "yucp-error-dialog-status-mark"), Is.Null);
                Assert.That(dialog.rootVisualElement.ClassListContains("yucp-error-dialog-surface"), Is.True);

                var eyebrow = dialog.rootVisualElement.Q<Label>("yucp-error-dialog-eyebrow");
                Assert.That(eyebrow, Is.Not.Null);
                Assert.That(eyebrow.text, Is.EqualTo("YUCP Installer"));

                var title = dialog.rootVisualElement.Q<Label>("yucp-error-dialog-title");
                Assert.That(title, Is.Not.Null);
                Assert.That(title.text, Is.EqualTo("Complete YUCP Install"));

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
