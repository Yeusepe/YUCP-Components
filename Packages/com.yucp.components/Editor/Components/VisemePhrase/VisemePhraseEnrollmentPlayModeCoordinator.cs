#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components.Editor.VisemePhrase
{
    /// <summary>
    /// Bridges VRCFury's preprocess callback in Play Mode back to the restored
    /// edit-scene avatar. The bridge exists only while a handoff is pending; it
    /// deliberately does not install another permanent Play Mode listener.
    /// </summary>
    [InitializeOnLoad]
    internal static class VisemePhraseEnrollmentPlayModeCoordinator
    {
        private const string PendingHandoffKey =
            "YUCP.VisemePhrase.PendingPlayModeEnrollment.V1";
        private const string SkipPreviewKey =
            "YUCP.VisemePhrase.SkipNextPreview.V1";
        private const double HandoffTimeoutSeconds = 20d;
        private const double ResumeTokenTimeoutMinutes = 10d;
        private const int RequiredSettledEditFrames = 2;
        private const int MaximumHandoffUpdates = 1200;

        private static readonly HashSet<int> InterruptedPreviewRoots =
            new HashSet<int>();
        private static readonly Dictionary<int, string> ResumedSkipRoots =
            new Dictionary<int, string>();

        private static bool handoffUpdateAttached;
        private static bool skipCleanupUpdateAttached;
        private static int handoffUpdates;
        private static int settledEditFrames;

        [Serializable]
        internal sealed class AvatarLocator
        {
            public int version = 1;
            public string avatarGlobalId = string.Empty;
            public string scenePath = string.Empty;
            public int[] hierarchyPath = Array.Empty<int>();
            public string avatarName = string.Empty;
            public ComponentLocator[] components = Array.Empty<ComponentLocator>();
        }

        [Serializable]
        internal sealed class ComponentLocator
        {
            public string globalId = string.Empty;
            public int[] relativeHierarchyPath = Array.Empty<int>();
            public int componentOrdinal;
            public string fingerprint = string.Empty;
        }

        [Serializable]
        private sealed class PendingHandoff
        {
            public AvatarLocator avatar;
            public long expiresUtcTicks;
        }

        [Serializable]
        private sealed class SkipPreviewRequest
        {
            public AvatarLocator avatar;
            public string nonce = string.Empty;
            public long expiresUtcTicks;
            public bool activated;
            public int previewFrameHighWater;
        }

        static VisemePhraseEnrollmentPlayModeCoordinator()
        {
            // SessionState survives the domain reload caused by leaving Play.
            // This delay call is one-shot and subscribes update only if there is
            // an actual unfinished handoff to resolve.
            EditorApplication.delayCall += RestorePendingHandoffAfterReload;
            EditorApplication.delayCall += ClearFinishedSkipAfterReload;
        }

        internal static bool TryPausePreviewForEnrollment(
            GameObject avatarRoot,
            IReadOnlyList<VisemePhraseTriggerData> components,
            string error)
        {
            if (!Application.isPlaying || Application.isBatchMode ||
                avatarRoot == null || components == null ||
                !IsEnrollmentIssue(error))
                return false;

            var locator = CaptureLocator(avatarRoot, components);
            if (locator == null || locator.components.Length == 0) return false;

            var pending = new PendingHandoff
            {
                avatar = locator,
                expiresUtcTicks = DateTime.UtcNow
                    .AddSeconds(HandoffTimeoutSeconds).Ticks
            };
            SessionState.SetString(PendingHandoffKey, JsonUtility.ToJson(pending));

            // Let the remainder of this single VRCFury callback return cleanly
            // while Unity performs the requested Play -> Edit transition.
            InterruptedPreviewRoots.Add(avatarRoot.GetInstanceID());
            ArmPendingHandoffUpdate();
            EditorApplication.isPlaying = false;
            Debug.Log(
                "[YUCP Viseme Phrase Trigger] Play Mode paused so this avatar's phrases can be taught. " +
                "Finish or skip the guide to continue.",
                avatarRoot);
            return true;
        }

        internal static bool ShouldSkipPreflight(GameObject avatarRoot)
        {
            return ShouldSkipPreflight(avatarRoot, Application.isPlaying);
        }

        internal static bool ShouldSkipGeneration(GameObject avatarRoot)
        {
            return ShouldSkipGeneration(avatarRoot, Application.isPlaying);
        }

        private static bool ShouldSkipPreflight(
            GameObject avatarRoot,
            bool isPreview)
        {
            if (!isPreview || avatarRoot == null) return false;
            var rootId = avatarRoot.GetInstanceID();
            if (InterruptedPreviewRoots.Contains(rootId) ||
                ResumedSkipRoots.ContainsKey(rootId))
                return true;

            if (!TryActivateSkipRequest(avatarRoot, out var nonce)) return false;
            ResumedSkipRoots[rootId] = nonce;
            return true;
        }

        private static bool ShouldSkipGeneration(
            GameObject avatarRoot,
            bool isPreview)
        {
            if (avatarRoot == null) return false;
            var rootId = avatarRoot.GetInstanceID();
            // EditorApplication.isPlaying=false is deferred by Unity, but keep
            // the current callback safe even if a host reports the transition
            // synchronously after the preflight requested exit.
            if (InterruptedPreviewRoots.Remove(rootId)) return true;
            if (!isPreview) return false;
            if (ResumedSkipRoots.ContainsKey(rootId)) return true;

            // Be defensive if another build pipeline invokes the generator
            // without first invoking our preflight callback.
            if (!TryActivateSkipRequest(avatarRoot, out var nonce)) return false;
            ResumedSkipRoots[rootId] = nonce;
            return true;
        }

        private static bool TryActivateSkipRequest(
            GameObject avatarRoot,
            out string nonce)
        {
            nonce = string.Empty;
            if (!TryReadSkipRequest(out var request)) return false;
            if (!LocatorMatches(request.avatar, avatarRoot)) return false;
            if (request.activated &&
                Time.frameCount + 1 < request.previewFrameHighWater)
            {
                // With domain reload and scene reload both disabled, static and
                // SessionState data survive. Time.frameCount still restarts for
                // the next preview, so a prior preview can never be skipped.
                ClearActiveSkip();
                return false;
            }
            if (!request.activated)
            {
                request.activated = true;
                request.previewFrameHighWater = Time.frameCount;
            }
            else
            {
                request.previewFrameHighWater = Math.Max(
                    request.previewFrameHighWater,
                    Time.frameCount);
            }
            SessionState.SetString(SkipPreviewKey,
                JsonUtility.ToJson(request));
            ArmSkipCleanupUpdate();
            nonce = request.nonce ?? string.Empty;
            return true;
        }

        private static void ClearFinishedSkipAfterReload()
        {
            if (!TryReadSkipRequest(out var request) || request == null) return;
            if (Application.isPlaying)
            {
                if (request.activated) ArmSkipCleanupUpdate();
                return;
            }

            // An armed request briefly exists in Edit Mode between clicking
            // Skip and Unity entering Play. Only activated requests belong to a
            // preview that has already run and may be cleared here.
            if (request.activated) ClearActiveSkip();
        }

        private static void ArmSkipCleanupUpdate()
        {
            if (skipCleanupUpdateAttached) return;
            skipCleanupUpdateAttached = true;
            // This is attached only for the one preview the creator explicitly
            // resumed. It is removed as soon as that preview returns to Edit.
            EditorApplication.update += TickActiveSkip;
        }

        private static void DisarmSkipCleanupUpdate()
        {
            if (!skipCleanupUpdateAttached) return;
            EditorApplication.update -= TickActiveSkip;
            skipCleanupUpdateAttached = false;
        }

        private static void TickActiveSkip()
        {
            if (!TryReadSkipRequest(out var request) || request == null)
            {
                DisarmSkipCleanupUpdate();
                return;
            }
            if (!Application.isPlaying &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ClearActiveSkip();
                return;
            }
            if (Application.isPlaying)
            {
                request.previewFrameHighWater = Math.Max(
                    request.previewFrameHighWater,
                    Time.frameCount);
                SessionState.SetString(SkipPreviewKey,
                    JsonUtility.ToJson(request));
            }
        }

        private static void ClearActiveSkip()
        {
            SessionState.EraseString(SkipPreviewKey);
            ResumedSkipRoots.Clear();
            InterruptedPreviewRoots.Clear();
            DisarmSkipCleanupUpdate();
        }

        private static bool TryReadSkipRequest(out SkipPreviewRequest request)
        {
            request = null;
            var json = SessionState.GetString(SkipPreviewKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                request = JsonUtility.FromJson<SkipPreviewRequest>(json);
            }
            catch
            {
                request = null;
            }

            if (request?.avatar != null &&
                request.expiresUtcTicks >= DateTime.UtcNow.Ticks)
                return true;
            SessionState.EraseString(SkipPreviewKey);
            request = null;
            return false;
        }

        private static void RestorePendingHandoffAfterReload()
        {
            if (!string.IsNullOrWhiteSpace(
                    SessionState.GetString(PendingHandoffKey, string.Empty)))
                ArmPendingHandoffUpdate();
        }

        private static void ArmPendingHandoffUpdate()
        {
            if (handoffUpdateAttached) return;
            handoffUpdateAttached = true;
            handoffUpdates = 0;
            settledEditFrames = 0;
            EditorApplication.update += TickPendingHandoff;
        }

        private static void DisarmPendingHandoffUpdate()
        {
            if (!handoffUpdateAttached) return;
            EditorApplication.update -= TickPendingHandoff;
            handoffUpdateAttached = false;
            handoffUpdates = 0;
            settledEditFrames = 0;
        }

        private static void TickPendingHandoff()
        {
            handoffUpdates++;
            if (!TryReadPendingHandoff(out var pending))
            {
                DisarmPendingHandoffUpdate();
                return;
            }

            if (pending.expiresUtcTicks < DateTime.UtcNow.Ticks ||
                handoffUpdates > MaximumHandoffUpdates)
            {
                SessionState.EraseString(PendingHandoffKey);
                DisarmPendingHandoffUpdate();
                Debug.LogWarning(
                    "[YUCP Viseme Phrase Trigger] The Play Mode enrollment handoff timed out. " +
                    "Open Record / Improve from the component inspector to continue.");
                return;
            }

            if (Application.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                settledEditFrames = 0;
                return;
            }

            // Unity restores scene objects over more than one editor update on
            // some domain/scene reload combinations.
            if (++settledEditFrames < RequiredSettledEditFrames) return;
            if (!TryResolveLocator(pending.avatar, out var avatarRoot,
                    out var components))
                return;

            SessionState.EraseString(PendingHandoffKey);
            DisarmPendingHandoffUpdate();
            var locator = CaptureLocator(avatarRoot, components) ?? pending.avatar;
            EditorApplication.delayCall += () =>
            {
                if (!TryResolveLocator(locator, out var restoredRoot,
                        out var restoredComponents))
                    return;
                VisemePhraseEnrollmentOverlay.OpenForAvatar(
                    restoredRoot,
                    restoredComponents,
                    createProfiles: false,
                    resumePlay: skipPhraseGeneration =>
                        ResumePreview(
                            CaptureLocator(restoredRoot, restoredComponents) ?? locator,
                            skipPhraseGeneration));
            };
        }

        private static bool TryReadPendingHandoff(out PendingHandoff pending)
        {
            pending = null;
            var json = SessionState.GetString(PendingHandoffKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                pending = JsonUtility.FromJson<PendingHandoff>(json);
            }
            catch
            {
                pending = null;
            }

            if (pending?.avatar != null) return true;
            SessionState.EraseString(PendingHandoffKey);
            return false;
        }

        private static void ResumePreview(
            AvatarLocator locator,
            bool skipPhraseGeneration)
        {
            InterruptedPreviewRoots.Clear();
            ResumedSkipRoots.Clear();
            SessionState.EraseString(PendingHandoffKey);
            if (skipPhraseGeneration)
            {
                var request = new SkipPreviewRequest
                {
                    avatar = locator,
                    nonce = Guid.NewGuid().ToString("N"),
                    expiresUtcTicks = DateTime.UtcNow
                        .AddMinutes(ResumeTokenTimeoutMinutes).Ticks,
                    activated = false,
                    previewFrameHighWater = 0
                };
                SessionState.SetString(SkipPreviewKey,
                    JsonUtility.ToJson(request));
            }
            else
            {
                SessionState.EraseString(SkipPreviewKey);
            }

            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                    !Application.isPlaying)
                    EditorApplication.isPlaying = true;
            };
        }

        internal static bool IsEnrollmentIssue(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;
            var structural = new[]
            {
                "Add at least one enrolled phrase",
                "has no phrases",
                "missing phrase entry",
                "non-empty prompt",
                "requires an Advanced Viseme Reconstructor",
                "Jaw Flap",
                "avatar root is missing",
                "no VRC Avatar Descriptor",
                "at most 4 phrase",
                "parameter",
                "Stable phrase ID"
            };
            if (structural.Any(value => error.IndexOf(
                    value, StringComparison.OrdinalIgnoreCase) >= 0))
                return false;

            var enrollment = new[]
            {
                "no enrollment profile",
                "enrollment must be personal",
                "enrollment is inherited",
                "enrollment profile uses an unsupported schema",
                "no matching enrollment",
                "unsupported enrollment schema",
                "unsupported compiled-model schema",
                "unsupported trace schema",
                "compiled model",
                "compiled enrollment model",
                "raw enrollment no longer compiles",
                "enrollment traces changed",
                "invalid compiled model",
                "settings changed after enrollment",
                "compiled timing variants",
                "take diagnostics",
                "Record its takes",
                "Recompile or re-record its enrollment",
                "Re-record the affected take"
            };
            return enrollment.Any(value => error.IndexOf(
                value, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static AvatarLocator CaptureLocator(
            GameObject avatarRoot,
            IReadOnlyList<VisemePhraseTriggerData> components)
        {
            if (avatarRoot == null || components == null) return null;
            var validComponents = components
                .Where(component => component != null &&
                                    component.transform.IsChildOf(avatarRoot.transform))
                .ToArray();
            return new AvatarLocator
            {
                avatarGlobalId = StableGlobalId(avatarRoot),
                scenePath = avatarRoot.scene.IsValid()
                    ? avatarRoot.scene.path ?? string.Empty
                    : string.Empty,
                hierarchyPath = HierarchyPathFromSceneRoot(avatarRoot.transform),
                avatarName = avatarRoot.name ?? string.Empty,
                components = validComponents.Select(component =>
                    new ComponentLocator
                    {
                        globalId = StableGlobalId(component),
                        relativeHierarchyPath = RelativeHierarchyPath(
                            avatarRoot.transform,
                            component.transform),
                        componentOrdinal = ComponentOrdinal(component),
                        fingerprint = ComponentFingerprint(component)
                    }).ToArray()
            };
        }

        internal static bool TryResolveLocator(
            AvatarLocator locator,
            out GameObject avatarRoot,
            out VisemePhraseTriggerData[] components)
        {
            avatarRoot = null;
            components = Array.Empty<VisemePhraseTriggerData>();
            if (locator == null) return false;

            avatarRoot = ResolveGlobalObject<GameObject>(locator.avatarGlobalId);
            if (!IsValidEditAvatar(avatarRoot) || !LocatorMatches(locator, avatarRoot))
                avatarRoot = ResolveByScenePath(locator);
            if (!IsValidEditAvatar(avatarRoot) || !LocatorMatches(locator, avatarRoot))
                avatarRoot = ResolveUniqueFingerprintMatch(locator);
            if (!IsValidEditAvatar(avatarRoot))
            {
                avatarRoot = null;
                return false;
            }

            components = ResolveComponents(locator, avatarRoot);
            if (components.Length != locator.components.Length)
            {
                avatarRoot = null;
                components = Array.Empty<VisemePhraseTriggerData>();
                return false;
            }
            return true;
        }

        private static bool LocatorMatches(
            AvatarLocator locator,
            GameObject avatarRoot)
        {
            if (locator == null || avatarRoot == null) return false;
            var expected = (locator.components ?? Array.Empty<ComponentLocator>())
                .Select(component => component.fingerprint ?? string.Empty)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var actual = avatarRoot
                .GetComponentsInChildren<VisemePhraseTriggerData>(true)
                .Select(ComponentFingerprint)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal)) return false;

            var actualGlobalId = StableGlobalId(avatarRoot);
            if (!string.IsNullOrEmpty(locator.avatarGlobalId) &&
                !string.IsNullOrEmpty(actualGlobalId) &&
                string.Equals(locator.avatarGlobalId, actualGlobalId,
                    StringComparison.Ordinal))
                return true;
            return string.Equals(locator.scenePath ?? string.Empty,
                       avatarRoot.scene.path ?? string.Empty,
                       StringComparison.OrdinalIgnoreCase) &&
                   (locator.hierarchyPath ?? Array.Empty<int>())
                   .SequenceEqual(HierarchyPathFromSceneRoot(avatarRoot.transform));
        }

        private static GameObject ResolveByScenePath(AvatarLocator locator)
        {
            var candidates = new List<GameObject>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded ||
                    !string.Equals(scene.path ?? string.Empty,
                        locator.scenePath ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                var resolved = ResolveHierarchyPath(scene, locator.hierarchyPath);
                if (resolved != null) candidates.Add(resolved);
            }
            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static GameObject ResolveUniqueFingerprintMatch(
            AvatarLocator locator)
        {
            var matches = UnityEngine.Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>()
                .Where(descriptor => descriptor != null &&
                                     IsValidEditAvatar(descriptor.gameObject) &&
                                     string.Equals(descriptor.gameObject.name,
                                         locator.avatarName,
                                         StringComparison.Ordinal) &&
                                     FingerprintsMatch(locator, descriptor.gameObject))
                .Select(descriptor => descriptor.gameObject)
                .Distinct()
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static bool FingerprintsMatch(
            AvatarLocator locator,
            GameObject avatarRoot)
        {
            if (locator == null || avatarRoot == null) return false;
            var expected = (locator.components ?? Array.Empty<ComponentLocator>())
                .Select(component => component.fingerprint ?? string.Empty)
                .OrderBy(value => value, StringComparer.Ordinal);
            var actual = avatarRoot
                .GetComponentsInChildren<VisemePhraseTriggerData>(true)
                .Select(ComponentFingerprint)
                .OrderBy(value => value, StringComparer.Ordinal);
            return expected.SequenceEqual(actual, StringComparer.Ordinal);
        }

        private static VisemePhraseTriggerData[] ResolveComponents(
            AvatarLocator locator,
            GameObject avatarRoot)
        {
            var resolved = new List<VisemePhraseTriggerData>();
            foreach (var componentLocator in locator.components ??
                     Array.Empty<ComponentLocator>())
            {
                var component = ResolveGlobalObject<VisemePhraseTriggerData>(
                    componentLocator.globalId);
                if (!IsComponentMatch(component, componentLocator, avatarRoot))
                {
                    var target = ResolveRelativeHierarchyPath(
                        avatarRoot.transform,
                        componentLocator.relativeHierarchyPath);
                    var local = target != null
                        ? target.GetComponents<VisemePhraseTriggerData>()
                        : Array.Empty<VisemePhraseTriggerData>();
                    component = componentLocator.componentOrdinal >= 0 &&
                                componentLocator.componentOrdinal < local.Length
                        ? local[componentLocator.componentOrdinal]
                        : null;
                }
                if (!IsComponentMatch(component, componentLocator, avatarRoot))
                {
                    component = avatarRoot
                        .GetComponentsInChildren<VisemePhraseTriggerData>(true)
                        .FirstOrDefault(candidate => !resolved.Contains(candidate) &&
                            string.Equals(ComponentFingerprint(candidate),
                                componentLocator.fingerprint,
                                StringComparison.Ordinal));
                }
                if (!IsComponentMatch(component, componentLocator, avatarRoot))
                    return Array.Empty<VisemePhraseTriggerData>();
                resolved.Add(component);
            }
            return resolved.ToArray();
        }

        private static bool IsComponentMatch(
            VisemePhraseTriggerData component,
            ComponentLocator locator,
            GameObject avatarRoot)
        {
            return component != null && locator != null && avatarRoot != null &&
                   component.transform.IsChildOf(avatarRoot.transform) &&
                   string.Equals(ComponentFingerprint(component),
                       locator.fingerprint,
                       StringComparison.Ordinal);
        }

        private static bool IsValidEditAvatar(GameObject avatarRoot)
        {
            return avatarRoot != null && !EditorUtility.IsPersistent(avatarRoot) &&
                   avatarRoot.scene.IsValid() &&
                   (avatarRoot.hideFlags & HideFlags.DontSave) == 0 &&
                   avatarRoot.GetComponent<VRCAvatarDescriptor>() != null;
        }

        private static string ComponentFingerprint(
            VisemePhraseTriggerData component)
        {
            if (component == null) return string.Empty;
            var phrases = component.phrases ?? new List<VisemePhraseDefinition>();
            return string.Join("|",
                AssetDatabase.GetAssetPath(component.enrollmentProfile),
                component.NormalizedPrefix,
                component.NormalizedSourcePrefix,
                string.Join(",", phrases.Where(phrase => phrase != null)
                    .Select(phrase => phrase.id + ":" + phrase.PromptFingerprint)
                    .OrderBy(value => value, StringComparer.Ordinal)));
        }

        private static int ComponentOrdinal(VisemePhraseTriggerData component)
        {
            var siblings = component.GetComponents<VisemePhraseTriggerData>();
            return Math.Max(0, Array.IndexOf(siblings, component));
        }

        private static int[] HierarchyPathFromSceneRoot(Transform transform)
        {
            if (transform == null) return Array.Empty<int>();
            var result = new List<int>();
            for (var current = transform; current != null; current = current.parent)
                result.Add(current.GetSiblingIndex());
            result.Reverse();
            return result.ToArray();
        }

        private static int[] RelativeHierarchyPath(
            Transform ancestor,
            Transform target)
        {
            if (ancestor == null || target == null ||
                !target.IsChildOf(ancestor))
                return Array.Empty<int>();
            var result = new List<int>();
            for (var current = target; current != ancestor; current = current.parent)
                result.Add(current.GetSiblingIndex());
            result.Reverse();
            return result.ToArray();
        }

        private static GameObject ResolveHierarchyPath(
            Scene scene,
            IReadOnlyList<int> path)
        {
            if (!scene.IsValid() || path == null || path.Count == 0) return null;
            var roots = scene.GetRootGameObjects();
            if (path[0] < 0 || path[0] >= roots.Length) return null;
            var transform = roots[path[0]].transform;
            for (var i = 1; i < path.Count; i++)
            {
                if (path[i] < 0 || path[i] >= transform.childCount) return null;
                transform = transform.GetChild(path[i]);
            }
            return transform.gameObject;
        }

        private static Transform ResolveRelativeHierarchyPath(
            Transform ancestor,
            IReadOnlyList<int> path)
        {
            if (ancestor == null || path == null) return null;
            var current = ancestor;
            for (var i = 0; i < path.Count; i++)
            {
                if (path[i] < 0 || path[i] >= current.childCount) return null;
                current = current.GetChild(path[i]);
            }
            return current;
        }

        private static string StableGlobalId(UnityEngine.Object value)
        {
            if (value == null) return string.Empty;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
            return string.IsNullOrWhiteSpace(id) ||
                   id.EndsWith("-0-0", StringComparison.Ordinal)
                ? string.Empty
                : id;
        }

        private static T ResolveGlobalObject<T>(string value)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !GlobalObjectId.TryParse(value, out var id))
                return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as T;
        }

        // Narrow test seams: they exercise SessionState persistence and the
        // two-callback consumption contract without entering real Play Mode.
        internal static void QueueSkipForTests(AvatarLocator locator)
        {
            SessionState.SetString(SkipPreviewKey, JsonUtility.ToJson(
                new SkipPreviewRequest
                {
                    avatar = locator,
                    nonce = Guid.NewGuid().ToString("N"),
                    expiresUtcTicks = DateTime.UtcNow.AddMinutes(1d).Ticks,
                    activated = false,
                    previewFrameHighWater = 0
                }));
        }

        internal static bool ShouldSkipPreflightForTests(
            GameObject avatarRoot,
            bool isPreview)
        {
            return ShouldSkipPreflight(avatarRoot, isPreview);
        }

        internal static bool ShouldSkipGenerationForTests(
            GameObject avatarRoot,
            bool isPreview)
        {
            return ShouldSkipGeneration(avatarRoot, isPreview);
        }

        internal static void MarkInterruptedForTests(GameObject avatarRoot)
        {
            if (avatarRoot != null)
                InterruptedPreviewRoots.Add(avatarRoot.GetInstanceID());
        }

        internal static void EndPreviewForTests()
        {
            ClearActiveSkip();
        }

        internal static void ResetForTests()
        {
            SessionState.EraseString(PendingHandoffKey);
            SessionState.EraseString(SkipPreviewKey);
            InterruptedPreviewRoots.Clear();
            ResumedSkipRoots.Clear();
            DisarmPendingHandoffUpdate();
            DisarmSkipCleanupUpdate();
        }
    }
}
#endif
