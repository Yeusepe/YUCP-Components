#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using com.vrcfury.api;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace YUCP.Components.Editor.VisemePhrase
{
    internal sealed class VisemePhraseGeneratedManifest : ScriptableObject
    {
        public string ownerIdentity;
        public string generatedFolder;
    }

    /// <summary>
    /// Runs immediately before Advanced Viseme changes an owning descriptor to
    /// Parameter Only, preserving a reliable guard against VRChat jaw-flap mode.
    /// </summary>
    public sealed class VisemePhraseTriggerPreflightProcessor :
        IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 189;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            if (VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipPreflight(avatarRoot))
                return true;
            var components = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<VisemePhraseTriggerData>(true)
                : Array.Empty<VisemePhraseTriggerData>();
            if (components.Length == 0) return true;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (!VisemePhraseTriggerContractAdapter.TryCreatePlan(
                    avatarRoot, descriptor, components, out _, out var error))
            {
                if (VisemePhraseEnrollmentPlayModeCoordinator
                    .TryPausePreviewForEnrollment(
                        avatarRoot,
                        components,
                        error))
                    return true;
                ScheduleEnrollmentOverlayIfUseful(avatarRoot, components, error);
                return Fail(avatarRoot, error);
            }
            return true;
        }

        private static void ScheduleEnrollmentOverlayIfUseful(
            GameObject avatarRoot,
            IReadOnlyList<VisemePhraseTriggerData> components,
            string error)
        {
            if (Application.isBatchMode || avatarRoot == null || components == null ||
                string.IsNullOrEmpty(error)) return;
            if (!VisemePhraseEnrollmentPlayModeCoordinator.IsEnrollmentIssue(error))
                return;
            var snapshot = ResolveAuthoringComponents(avatarRoot, components);
            if (snapshot.Length == 0) return;
            var authoringRoot = snapshot[0]
                .GetComponentInParent<VRCAvatarDescriptor>()?.gameObject;
            if (authoringRoot == null) return;
            EditorApplication.delayCall += () =>
            {
                if (authoringRoot != null && snapshot.Any(component => component != null))
                    VisemePhraseEnrollmentOverlay.OpenForAvatar(
                        authoringRoot,
                        snapshot,
                        createProfiles: false,
                        resumePlay: null);
            };
        }

        private static VisemePhraseTriggerData[] ResolveAuthoringComponents(
            GameObject buildAvatar,
            IReadOnlyList<VisemePhraseTriggerData> buildComponents)
        {
            if (buildAvatar != null && buildAvatar.scene.IsValid() &&
                !string.IsNullOrEmpty(buildAvatar.scene.path) &&
                (buildAvatar.hideFlags & HideFlags.DontSave) == 0)
                return buildComponents.Where(component => component != null).ToArray();
            var expected = buildComponents.Where(component => component != null)
                .Select(ComponentFingerprint)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var candidates = UnityEngine.Resources.FindObjectsOfTypeAll<VisemePhraseTriggerData>()
                .Where(component => component != null &&
                                    !EditorUtility.IsPersistent(component) &&
                                    component.gameObject.scene.IsValid())
                .Select(component => new
                {
                    component,
                    root = component.GetComponentInParent<VRCAvatarDescriptor>()?.gameObject
                })
                .Where(item => item.root != null && item.root != buildAvatar)
                .GroupBy(item => item.root)
                .Select(group => group.Select(item => item.component).ToArray())
                .Where(group => group.Select(ComponentFingerprint)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(expected, StringComparer.Ordinal))
                .ToArray();
            return candidates.Length == 1 ? candidates[0] : Array.Empty<VisemePhraseTriggerData>();
        }

        private static string ComponentFingerprint(VisemePhraseTriggerData component)
        {
            var phrases = component.phrases ?? new List<VisemePhraseDefinition>();
            return string.Join("|",
                AssetDatabase.GetAssetPath(component.enrollmentProfile),
                component.NormalizedPrefix,
                component.NormalizedSourcePrefix,
                string.Join(",", phrases.Where(phrase => phrase != null)
                    .Select(phrase => phrase.id + ":" + phrase.PromptFingerprint)
                    .OrderBy(value => value, StringComparer.Ordinal)));
        }

        private static bool Fail(UnityEngine.Object context, string error)
        {
            Debug.LogError("[YUCP Viseme Phrase Trigger] " + error, context);
            return false;
        }
    }

    /// <summary>
    /// Runs directly after Advanced Viseme (+190), aggregates every phrase
    /// component, and contributes one generated VRCFury Full Controller host.
    /// </summary>
    public sealed class VisemePhraseTriggerProcessor :
        IVRCSDKPreprocessAvatarCallback
    {
        internal const string GeneratedRoot =
            "Assets/YUCP/GeneratedAssets/VisemePhraseTrigger";

        public int callbackOrder => int.MinValue + 191;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            if (VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipGeneration(avatarRoot))
                return true;
            var components = avatarRoot != null
                ? avatarRoot.GetComponentsInChildren<VisemePhraseTriggerData>(true)
                : Array.Empty<VisemePhraseTriggerData>();
            if (components.Length == 0) return true;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            try
            {
                using (var transaction = new GeneratedTransaction(avatarRoot))
                {
                    transaction.QuarantinePriorGeneratedHosts();
                    transaction.ConfigureManifest(StableAvatarIdentity(avatarRoot));
                    if (!VisemePhraseTriggerContractAdapter.TryCreatePlan(
                            avatarRoot, descriptor, components, out var plan, out var error))
                        return Fail(avatarRoot, error);
                    var hash = plan.ContentHash();
                    var finalFolder = GeneratedRoot + "/" + Sanitize(avatarRoot.name) + "_" +
                                      hash.Substring(0, 12);
                    VisemePhraseTriggerAnimatorBuilder.Result built;
                    if (VisemePhraseTriggerAnimatorBuilder.TryLoadExisting(
                            finalFolder + "/VisemePhraseTrigger.controller",
                            finalFolder + "/VisemePhraseParameters.asset",
                            plan,
                            out built))
                    {
                        transaction.UseExisting(finalFolder);
                    }
                    else
                    {
                        var stagingFolder = transaction.Stage(finalFolder);
                        built = VisemePhraseTriggerAnimatorBuilder.Build(
                            new VisemePhraseTriggerAnimatorBuilder.Request
                            {
                                controllerPath = stagingFolder + "/VisemePhraseTrigger.controller",
                                parametersPath = stagingFolder + "/VisemePhraseParameters.asset",
                                plan = plan
                            });
                    }

                    var host = new GameObject("__YUCP Viseme Phrase Trigger Controller");
                    transaction.Track(host);
                    host.transform.SetParent(avatarRoot.transform, false);
                    host.transform.SetAsLastSibling();
                    var fullController = FuryComponents.CreateFullController(host);
                    fullController.AddController(
                        built.controller,
                        VRCAvatarDescriptor.AnimLayerType.FX);
                    if (built.parameters?.parameters != null &&
                        built.parameters.parameters.Length > 0)
                        fullController.AddParams(built.parameters);
                    foreach (var parameter in built.globalParameters
                                 .Concat(built.externalParameters)
                                 .Distinct(StringComparer.Ordinal))
                        fullController.AddGlobalParam(parameter);

                    AdvancedVisemeFinalParameterValidator.Mark(avatarRoot);
                    transaction.Complete();
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[YUCP Viseme Phrase Trigger] Could not generate the shared " +
                    "phrase controller: " + exception.Message,
                    avatarRoot);
                Debug.LogException(exception, avatarRoot);
                return false;
            }
        }

        private static bool Fail(UnityEngine.Object context, string error)
        {
            Debug.LogError("[YUCP Viseme Phrase Trigger] " + error, context);
            return false;
        }

        private static string Sanitize(string value)
        {
            var characters = (value ?? "Avatar")
                .Select(character => char.IsLetterOrDigit(character) ||
                                     character == '-' || character == '_'
                    ? character
                    : '_')
                .ToArray();
            return new string(characters);
        }

        private static string StableAvatarIdentity(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(avatarRoot) ?? avatarRoot;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    source, out var guid, out long localId) &&
                !string.IsNullOrEmpty(guid))
                return source.GetType().FullName + ":" + guid + ":" + localId;
            if (avatarRoot.scene.IsValid() && !string.IsNullOrEmpty(avatarRoot.scene.path))
            {
                var global = GlobalObjectId.GetGlobalObjectIdSlow(avatarRoot).ToString();
                if (!string.IsNullOrEmpty(global)) return global;
            }
            // Unsaved/transient avatars have no collision-safe identity. Keep
            // their old folders rather than guessing from a non-unique name.
            return null;
        }

        /// <summary>
        /// Assets are authored in a unique sibling folder and promoted only
        /// after the controller and VRCFury host are complete. A failed build
        /// restores the previous content-addressed folder and removes its host.
        /// </summary>
        private sealed class GeneratedTransaction : IDisposable
        {
            private readonly GameObject avatarRoot;
            private readonly List<GameObject> createdObjects = new List<GameObject>();
            private readonly List<QuarantinedHost> quarantinedHosts =
                new List<QuarantinedHost>();
            private string finalFolder;
            private string stagingFolder;
            private string backupFolder;
            private bool backupMoved;
            private bool promoted;
            private bool complete;
            private string manifestIdentity;

            internal GeneratedTransaction(GameObject avatarRoot)
            {
                this.avatarRoot = avatarRoot;
            }

            internal string Stage(string requestedFinalFolder)
            {
                if (stagingFolder != null)
                    throw new InvalidOperationException("Only one generated phrase folder is allowed per avatar.");
                finalFolder = NormalizeAndValidate(requestedFinalFolder);
                EnsureFolder(GeneratedRoot);
                var suffix = Guid.NewGuid().ToString("N");
                stagingFolder = GeneratedRoot + "/__staging_" + suffix;
                backupFolder = GeneratedRoot + "/__backup_" + suffix;
                EnsureFolder(stagingFolder);
                return stagingFolder;
            }

            internal void ConfigureManifest(string identity)
            {
                manifestIdentity = identity;
            }

            internal void UseExisting(string requestedFinalFolder)
            {
                if (stagingFolder != null || finalFolder != null)
                    throw new InvalidOperationException(
                        "The generated phrase transaction already selected its assets.");
                finalFolder = NormalizeAndValidate(requestedFinalFolder);
                if (!AssetDatabase.IsValidFolder(finalFolder))
                    throw new InvalidOperationException(
                        "The reusable generated phrase folder is missing.");
            }

            internal void QuarantinePriorGeneratedHosts()
            {
                var generated = avatarRoot.GetComponentsInChildren<Transform>(true)
                    .Where(transform => transform != avatarRoot.transform &&
                                        string.Equals(transform.name,
                                            "__YUCP Viseme Phrase Trigger Controller",
                                            StringComparison.Ordinal))
                    .Select(transform => transform.gameObject)
                    .Where(candidate => candidate.GetComponents<Component>()
                        .Any(component => component != null &&
                                          string.Equals(component.GetType().FullName,
                                              "VF.Model.VRCFury",
                                              StringComparison.Ordinal)))
                    .ToArray();
                foreach (var host in generated)
                {
                    quarantinedHosts.Add(new QuarantinedHost(host));
                    host.transform.SetParent(null, true);
                    host.SetActive(false);
                }
            }

            internal void Track(GameObject value)
            {
                if (value != null) createdObjects.Add(value);
            }

            internal void Complete()
            {
                if (complete) return;
                if (!string.IsNullOrEmpty(stagingFolder) &&
                    !AssetDatabase.IsValidFolder(stagingFolder))
                    throw new InvalidOperationException("The staged phrase assets are missing.");
                if (!string.IsNullOrEmpty(stagingFolder) &&
                    AssetDatabase.IsValidFolder(finalFolder))
                {
                    MoveOrThrow(finalFolder, backupFolder);
                    backupMoved = true;
                }
                if (!string.IsNullOrEmpty(stagingFolder))
                {
                    MoveOrThrow(stagingFolder, finalFolder);
                    promoted = true;
                }
                if (backupMoved && AssetDatabase.IsValidFolder(backupFolder) &&
                    !AssetDatabase.DeleteAsset(backupFolder))
                    throw new InvalidOperationException(
                        "Unity could not remove the previous generated phrase assets.");
                foreach (var host in quarantinedHosts)
                    if (host.gameObject != null)
                        UnityEngine.Object.DestroyImmediate(host.gameObject);
                quarantinedHosts.Clear();
                UpdateManifestAndCleanSuperseded();
                complete = true;
            }

            public void Dispose()
            {
                if (complete) return;
                for (var index = createdObjects.Count - 1; index >= 0; index--)
                    if (createdObjects[index] != null)
                        UnityEngine.Object.DestroyImmediate(createdObjects[index]);
                foreach (var host in quarantinedHosts)
                    host.Restore();
                quarantinedHosts.Clear();
                try
                {
                    if (promoted && AssetDatabase.IsValidFolder(finalFolder))
                        AssetDatabase.DeleteAsset(finalFolder);
                    if (backupMoved && AssetDatabase.IsValidFolder(backupFolder))
                        MoveOrThrow(backupFolder, finalFolder);
                    if (!string.IsNullOrEmpty(stagingFolder) &&
                        AssetDatabase.IsValidFolder(stagingFolder))
                        AssetDatabase.DeleteAsset(stagingFolder);
                    if (!string.IsNullOrEmpty(backupFolder) &&
                        AssetDatabase.IsValidFolder(backupFolder))
                        AssetDatabase.DeleteAsset(backupFolder);
                }
                catch (Exception rollbackException)
                {
                    Debug.LogError(
                        "[YUCP Viseme Phrase Trigger] Generated asset rollback failed: " +
                        rollbackException.Message,
                        avatarRoot);
                }
            }

            private sealed class QuarantinedHost
            {
                internal readonly GameObject gameObject;
                private readonly Transform parent;
                private readonly int siblingIndex;
                private readonly Vector3 localPosition;
                private readonly Quaternion localRotation;
                private readonly Vector3 localScale;
                private readonly bool active;

                internal QuarantinedHost(GameObject gameObject)
                {
                    this.gameObject = gameObject;
                    parent = gameObject.transform.parent;
                    siblingIndex = gameObject.transform.GetSiblingIndex();
                    localPosition = gameObject.transform.localPosition;
                    localRotation = gameObject.transform.localRotation;
                    localScale = gameObject.transform.localScale;
                    active = gameObject.activeSelf;
                }

                internal void Restore()
                {
                    if (gameObject == null || parent == null) return;
                    gameObject.transform.SetParent(parent, false);
                    gameObject.transform.localPosition = localPosition;
                    gameObject.transform.localRotation = localRotation;
                    gameObject.transform.localScale = localScale;
                    gameObject.transform.SetSiblingIndex(Mathf.Min(
                        siblingIndex, parent.childCount - 1));
                    gameObject.SetActive(active);
                }
            }

            private static string NormalizeAndValidate(string path)
            {
                var normalized = (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
                if (!normalized.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Refusing to write generated phrase assets outside '" +
                        GeneratedRoot + "'.");
                return normalized;
            }

            private void UpdateManifestAndCleanSuperseded()
            {
                if (string.IsNullOrEmpty(manifestIdentity) ||
                    string.IsNullOrEmpty(finalFolder)) return;
                try
                {
                    var manifestRoot = GeneratedRoot + "/Manifests";
                    EnsureFolder(manifestRoot);
                    var manifestPath = manifestRoot + "/" +
                                       AdvancedVisemeParameterContract.StableFingerprint(
                                           manifestIdentity) + ".asset";
                    var manifest = AssetDatabase.LoadAssetAtPath<
                        VisemePhraseGeneratedManifest>(manifestPath);
                    var oldFolder = manifest != null
                        ? manifest.generatedFolder
                        : string.Empty;
                    if (manifest == null)
                    {
                        manifest = ScriptableObject.CreateInstance<
                            VisemePhraseGeneratedManifest>();
                        manifest.name = "YUCP Viseme Phrase Generated Manifest";
                        AssetDatabase.CreateAsset(manifest, manifestPath);
                    }
                    manifest.ownerIdentity = manifestIdentity;
                    manifest.generatedFolder = finalFolder;
                    EditorUtility.SetDirty(manifest);
                    AssetDatabase.SaveAssetIfDirty(manifest);

                    if (string.IsNullOrEmpty(oldFolder) ||
                        string.Equals(oldFolder, finalFolder, StringComparison.Ordinal) ||
                        !oldFolder.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal) ||
                        !AssetDatabase.IsValidFolder(oldFolder)) return;
                    var referencedElsewhere = AssetDatabase.FindAssets(
                            "t:VisemePhraseGeneratedManifest", new[] { manifestRoot })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Where(path => !string.Equals(path, manifestPath,
                            StringComparison.Ordinal))
                        .Select(AssetDatabase.LoadAssetAtPath<
                            VisemePhraseGeneratedManifest>)
                        .Any(other => other != null && string.Equals(
                            other.generatedFolder, oldFolder, StringComparison.Ordinal));
                    if (!referencedElsewhere) AssetDatabase.DeleteAsset(oldFolder);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[YUCP Viseme Phrase Trigger] Generated assets are valid, but " +
                        "superseded-folder cleanup was skipped: " + exception.Message,
                        avatarRoot);
                }
            }

            private static void EnsureFolder(string folder)
            {
                var normalized = NormalizeFolder(folder);
                if (AssetDatabase.IsValidFolder(normalized)) return;
                var parts = normalized.Split('/');
                var cursor = parts[0];
                for (var index = 1; index < parts.Length; index++)
                {
                    var next = cursor + "/" + parts[index];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        var guid = AssetDatabase.CreateFolder(cursor, parts[index]);
                        if (string.IsNullOrEmpty(guid))
                            throw new InvalidOperationException(
                                "Unity could not create generated folder '" + next + "'.");
                    }
                    cursor = next;
                }
            }

            private static string NormalizeFolder(string folder)
            {
                var normalized = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
                if (normalized != GeneratedRoot &&
                    !normalized.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Generated phrase folder escaped its root: '" + normalized + "'.");
                return normalized;
            }

            private static void MoveOrThrow(string source, string destination)
            {
                var error = AssetDatabase.MoveAsset(source, destination);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException(
                        "Could not move generated phrase assets from '" + source +
                        "' to '" + destination + "': " + error);
            }
        }
    }
}
#endif
