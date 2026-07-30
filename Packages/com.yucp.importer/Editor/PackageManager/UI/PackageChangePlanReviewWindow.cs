using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.PackageManager
{
    internal sealed class PackageChangePlanReviewWindow : EditorWindow
    {
        private static readonly (string Kind, string Label)[] Groups =
        {
            (PackageChangeKind.Added, "Added"),
            (PackageChangeKind.ReplacedUnchanged, "Replaced unchanged"),
            (
                PackageChangeKind.ReplacedWithLocalModifications,
                "Replaced with local modifications"),
            (PackageChangeKind.Removed, "Removed"),
            (
                PackageChangeKind.RemovedWithLocalModifications,
                "Removed with local modifications"),
            (PackageChangeKind.BlockedCollision, "Blocked collisions"),
        };

        private bool _confirmed;
        private List<string> _dirtyAssets = new List<string>();
        private PackageChangePlan _plan;
        private Vector2 _scroll;
        private string _targetLabel = string.Empty;

        internal static bool ShowReview(
            PackageChangePlan plan,
            IEnumerable<string> dirtyAssets,
            string targetLabel)
        {
            var window = CreateInstance<PackageChangePlanReviewWindow>();
            window.titleContent = new GUIContent("Review package changes");
            window.minSize = new Vector2(680, 520);
            window.maxSize = new Vector2(960, 900);
            window.position = new Rect(
                (Screen.currentResolution.width - 760) / 2f,
                (Screen.currentResolution.height - 640) / 2f,
                760,
                640);
            window._plan = plan;
            window._dirtyAssets = (dirtyAssets ??
                    Enumerable.Empty<string>())
                .ToList();
            window._targetLabel = targetLabel ?? string.Empty;
            window.ShowModalUtility();
            bool confirmed = window._confirmed;
            DestroyImmediate(window);
            return confirmed;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField(
                "Review exact project changes",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(_targetLabel)
                    ? "The bootstrap target is pinned for this review."
                    : _targetLabel,
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);

            if (_plan == null)
            {
                EditorGUILayout.HelpBox(
                    "The package change plan is unavailable.",
                    MessageType.Error);
                DrawActions(blocked: true);
                return;
            }
            bool signatureValid =
                PackageChangePlanSigner.Verify(_plan);
            EditorGUILayout.HelpBox(
                signatureValid
                    ? "This exact change plan is cryptographically signed " +
                      "for the current importer session."
                    : "The package change-plan signature is invalid. " +
                      "Close this review and generate it again.",
                signatureValid
                    ? MessageType.Info
                    : MessageType.Error);

            if (_dirtyAssets.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Save or revert the affected dirty assets before " +
                    "continuing:\n" +
                    string.Join("\n", _dirtyAssets.Take(12)),
                    MessageType.Error);
            }
            if (_plan.HasBlockedCollisions)
            {
                EditorGUILayout.HelpBox(
                    "The package would replace files it does not own. " +
                    "Move or remove those collisions, then retry.",
                    MessageType.Error);
            }
            int preservedCount = _plan.entries.Count(
                entry => entry.RequiresPreservedCopy);
            if (preservedCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{preservedCount} locally modified file(s) will be " +
                    "copied to .yucp/preserved-changes and Assets/YUCP " +
                    "Preserved Changes before the package target is applied.",
                    MessageType.Warning);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach ((string kind, string label) in Groups)
            {
                List<PackageChangePlanEntry> entries = _plan.entries
                    .Where(entry => string.Equals(
                        entry.changeKind,
                        kind,
                        StringComparison.Ordinal))
                    .ToList();
                if (entries.Count == 0)
                {
                    continue;
                }
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField(
                    $"{label} ({entries.Count})",
                    EditorStyles.boldLabel);
                foreach (PackageChangePlanEntry entry in entries)
                {
                    EditorGUILayout.SelectableLabel(
                        entry.normalizedPath,
                        EditorStyles.label,
                        GUILayout.Height(
                            EditorGUIUtility.singleLineHeight));
                }
            }
            EditorGUILayout.EndScrollView();
            DrawActions(
                !signatureValid ||
                _dirtyAssets.Count > 0 ||
                _plan.HasBlockedCollisions);
        }

        private void DrawActions(bool blocked)
        {
            EditorGUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(110)))
                {
                    _confirmed = false;
                    Close();
                }
                using (new EditorGUI.DisabledScope(blocked))
                {
                    if (GUILayout.Button(
                            "Confirm changes",
                            GUILayout.Width(150)))
                    {
                        _confirmed = true;
                        Close();
                    }
                }
            }
            EditorGUILayout.Space(10);
        }
    }
}
