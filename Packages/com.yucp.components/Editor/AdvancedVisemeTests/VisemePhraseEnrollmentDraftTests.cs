using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseEnrollmentDraftTests
    {
        private GameObject root;
        private VisemePhraseTriggerData component;
        private string createdAssetPath;
        private string externalAssetPath;
        private string secondaryCreatedAssetPath;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Phrase Draft Test Avatar");
            component = root.AddComponent<VisemePhraseTriggerData>();
            component.phrases.Add(new VisemePhraseDefinition { prompt = "open the portal" });
            component.EnsureDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(createdAssetPath)) AssetDatabase.DeleteAsset(createdAssetPath);
            if (!string.IsNullOrEmpty(secondaryCreatedAssetPath))
                AssetDatabase.DeleteAsset(secondaryCreatedAssetPath);
            if (!string.IsNullOrEmpty(externalAssetPath)) AssetDatabase.DeleteAsset(externalAssetPath);
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void DraftProfileIsCreatedUnderUserDataAndSavedAfterEachTake()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            Assert.That(createdAssetPath, Does.StartWith(
                VisemePhraseEnrollmentDraft.DraftRoot + "/"));
            Assert.That(component.enrollmentProfile, Is.Not.Null);

            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(index));

            var reloaded = AssetDatabase.LoadAssetAtPath<VisemePhraseEnrollmentProfile>(createdAssetPath);
            var enrollment = reloaded.FindEnrollment(
                component.phrases[0].id,
                component.phrases[0].PromptFingerprint);
            Assert.That(enrollment, Is.Not.Null);
            Assert.That(enrollment.positiveTakes.Count, Is.EqualTo(4));
            Assert.That(enrollment.positiveTakes.All(take => take.frames.Count == 12), Is.True);
            Assert.That(enrollment.positiveTakes.All(take => take.durationSamples > 0), Is.True);
            Assert.That(typeof(VisemePhraseEnrollmentTrace).GetFields().Any(field =>
                field.FieldType == typeof(AudioClip) ||
                field.FieldType == typeof(float[]) ||
                field.FieldType == typeof(byte[])), Is.False,
                "Enrollment assets must never contain microphone PCM or an audio clip.");
        }

        [Test]
        public void AssetlessOpenAndSavePromptDoNotCreateEnrollmentUntilAcceptedTake()
        {
            var draft = new VisemePhraseEnrollmentDraft(
                component,
                component.phrases[0],
                createProfile: false);

            Assert.That(component.enrollmentProfile, Is.Null);
            Assert.That(draft.ProfileAsset, Is.Null);
            Assert.That(draft.AssetPath, Is.Empty);

            draft.SavePrompt();

            Assert.That(component.enrollmentProfile, Is.Null,
                "Opening or arming the automatic guide must remain side-effect free.");
            Assert.That(draft.AssetPath, Is.Empty);

            draft.SaveTake(0, UsefulTake(0));
            createdAssetPath = draft.AssetPath;
            Assert.That(component.enrollmentProfile, Is.Not.Null,
                "The first accepted capture is the point where the personal asset is created.");
            Assert.That(createdAssetPath, Does.StartWith(
                VisemePhraseEnrollmentDraft.DraftRoot + "/"));

            var secondPhrase = new VisemePhraseDefinition
            {
                prompt = "close the portal"
            };
            secondPhrase.EnsureDefaults();
            component.phrases.Add(secondPhrase);
            var secondDraft = new VisemePhraseEnrollmentDraft(
                component,
                secondPhrase,
                createProfile: false);
            var enrollmentCount = component.enrollmentProfile.enrollments.Count;

            secondDraft.SavePrompt();

            Assert.That(component.enrollmentProfile.enrollments.Count,
                Is.EqualTo(enrollmentCount),
                "Opening a new phrase on an existing profile must not add an empty enrollment.");
        }

        [Test]
        public void LoadingDraftDoesNotRewriteCaptureTimestampOrTakeIdentity()
        {
            var first = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = first.AssetPath;
            first.SaveTake(0, UsefulTake(0));
            var enrollment = component.enrollmentProfile.FindEnrollment(
                component.phrases[0].id,
                component.phrases[0].PromptFingerprint);
            var ticks = enrollment.positiveTakes[0].recordedUtcTicks;
            var takeId = enrollment.positiveTakes[0].takeId;

            var loaded = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            Assert.That(loaded.Takes[0], Is.Not.Null);
            Assert.That(enrollment.positiveTakes[0].recordedUtcTicks, Is.EqualTo(ticks));
            Assert.That(enrollment.positiveTakes[0].takeId, Is.EqualTo(takeId));
        }

        [Test]
        public void NonPersonalAssignedProfileIsNeverEditedAndIsReplacedByBlankDraft()
        {
            externalAssetPath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/External Phrase Enrollment.asset");
            var external = ScriptableObject.CreateInstance<VisemePhraseEnrollmentProfile>();
            external.enrollments.Add(new VisemePhraseEnrollment
            {
                phraseId = component.phrases[0].id,
                promptFingerprint = component.phrases[0].PromptFingerprint,
                positiveTakes = new System.Collections.Generic.List<VisemePhraseEnrollmentTrace>
                {
                    VisemePhraseEnrollmentDraft.ToTrace(UsefulTake(0))
                }
            });
            AssetDatabase.CreateAsset(external, externalAssetPath);
            AssetDatabase.SaveAssetIfDirty(external);
            component.enrollmentProfile = external;

            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;

            Assert.That(component.enrollmentProfile, Is.Not.SameAs(external));
            Assert.That(createdAssetPath, Does.StartWith(
                VisemePhraseEnrollmentDraft.DraftRoot + "/"));
            Assert.That(component.enrollmentProfile.enrollments, Is.Empty,
                "A prefab or external author's completed traces must not be copied into personal enrollment.");
            var reloadedExternal = AssetDatabase.LoadAssetAtPath<VisemePhraseEnrollmentProfile>(
                externalAssetPath);
            Assert.That(reloadedExternal.enrollments, Has.Count.EqualTo(1));
            Assert.That(reloadedExternal.enrollments[0].positiveTakes, Has.Count.EqualTo(1));
        }

        [Test]
        public void ProfileInheritedFromPrefabSourceIsReplacedByInstanceEnrollment()
        {
            var sourceDraft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = sourceDraft.AssetPath;
            externalAssetPath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/Viseme Phrase Enrollment Source.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, externalAssetPath);
            Assert.That(prefab, Is.Not.Null);

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                Assert.That(instance, Is.Not.Null);
                var instanceComponent = instance.GetComponent<VisemePhraseTriggerData>();
                var inherited = instanceComponent.enrollmentProfile;
                Assert.That(
                    VisemePhraseEnrollmentDraft.CanUseAsPersonalEnrollment(
                        instanceComponent,
                        inherited),
                    Is.False);

                var instanceDraft = new VisemePhraseEnrollmentDraft(
                    instanceComponent,
                    instanceComponent.phrases[0]);
                secondaryCreatedAssetPath = instanceDraft.AssetPath;
                Assert.That(instanceComponent.enrollmentProfile, Is.Not.SameAs(inherited));
                Assert.That(secondaryCreatedAssetPath, Does.StartWith(
                    VisemePhraseEnrollmentDraft.DraftRoot + "/"));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RetakeReplacesOnlyRequestedSlotAndRecompilesModel()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var enrollment = draft.Enrollment;
            Assert.That(enrollment.compiledModel, Is.Not.Null);
            var before = enrollment.positiveTakes.Select(take => take.takeId).ToArray();

            draft.SaveTake(2, UsefulTake(1));

            Assert.That(enrollment.compiledModel, Is.Not.Null);
            Assert.That(enrollment.positiveTakes[0].takeId, Is.EqualTo(before[0]));
            Assert.That(enrollment.positiveTakes[1].takeId, Is.EqualTo(before[1]));
            Assert.That(enrollment.positiveTakes[2].takeId, Is.Not.EqualTo(before[2]));
            Assert.That(enrollment.positiveTakes[3].takeId, Is.EqualTo(before[3]));
        }

        [Test]
        public void ClearingNegativeSampleImmediatelyRecompilesReadyEnrollment()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            Assert.That(draft.Enrollment.compiledModel, Is.Not.Null);

            draft.SaveNegativeSample(UsefulTake(8));
            draft.ClearNegativeSample();

            Assert.That(draft.Enrollment.negativeTraces, Is.Empty);
            Assert.That(draft.Enrollment.compiledModel, Is.Not.Null);
        }

        [Test]
        public void SavingChangedRecognitionSettingsRebakesCompletedTakes()
        {
            var phrase = component.phrases[0];
            var draft = new VisemePhraseEnrollmentDraft(component, phrase);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var enrollment = draft.Enrollment;
            Assert.That(enrollment.compiledModel, Is.Not.Null);
            Assert.That(enrollment.compiledModel.contextMode,
                Is.EqualTo(VisemePhraseContextMode.NaturalSpeech));

            phrase.mode = VisemePhraseContextMode.PausedCommand;
            phrase.strictness = 0.91f;
            var preview = VisemePhraseEnrollmentStatus.Evaluate(component, phrase);
            Assert.That(preview.IsReady, Is.True,
                "A deterministic rebake preview should not send completed takes back into " +
                "the microphone wizard merely because recognition settings changed.");
            Assert.That(enrollment.compiledModel.contextMode,
                Is.EqualTo(VisemePhraseContextMode.NaturalSpeech),
                "Status evaluation previews the rebake without mutating the stored asset.");
            draft.SavePrompt();

            Assert.That(enrollment.compiledModel, Is.Not.Null);
            Assert.That(enrollment.compiledModel.contextMode,
                Is.EqualTo(VisemePhraseContextMode.PausedCommand));
            Assert.That(enrollment.compiledModel.strictness, Is.EqualTo(0.91f).Within(1e-6f));
            Assert.That(enrollment.compiledModel.requiresLeadingPause, Is.True);
            Assert.That(enrollment.compiledModel.requiresTrailingPause, Is.True);
            Assert.That(VisemePhraseEnrollmentStatus.Evaluate(component, phrase).IsReady, Is.True);
        }

        [Test]
        public void EnrollmentOverlayStartsWithOneConsumerFacingTask()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });
            try
            {
                var rootElement = window.rootVisualElement;
                Assert.That(rootElement.Q<VisualElement>("phrase-enrollment-page"), Is.Null);
                Assert.That(rootElement.Q<VisualElement>("microphone-enrollment-page"), Is.Not.Null);
                Assert.That(rootElement.Q<VisualElement>("takes-enrollment-page"), Is.Null);
                Assert.That(rootElement.Q<VisualElement>("review-enrollment-page"), Is.Null);
                Assert.That(rootElement.Q<Button>("enrollment-primary-action"), Is.Null,
                    "Microphone startup and take progression are automatic.");
                Assert.That(rootElement.Q<VisualElement>("recording-stage"), Is.Not.Null);
                Assert.That(rootElement.Q<VisualElement>("microphone-picker"), Is.Not.Null);
                Assert.That(rootElement.Q<TextField>(), Is.Null,
                    "The avatar creator teaches a configured prefab; the enrollment wizard must not edit its prompt.");
                Assert.That(rootElement.Query<Label>().ToList().Count(label =>
                    label.text == "Teach Avatar Phrases"), Is.Zero,
                    "The native EditorWindow title must not be repeated inside the content surface.");
                Assert.That(rootElement.Q<VisualElement>("enrollment-footer"), Is.Not.Null);
                Assert.That(rootElement.Q<Button>("enrollment-skip-action").text,
                    Is.EqualTo("Skip for now"),
                    "Every automatic step needs a quiet escape without discarding saved drafts.");
                Assert.That(rootElement.Query<Button>().ToList().Select(button => button.text),
                    Does.Not.Contain("Build & Test").And.Not.Contain("Open VRChat SDK"),
                    "Enrollment should finish enrollment, not redirect into unrelated SDK tasks.");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void EnrollmentOverlayAutomaticallyRoutesTheDiagnosedTakeAndRemovesItsCheckmark()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });
            try
            {
                var diagnostics = draft.Enrollment.compiledModel.diagnostics;
                diagnostics.valid = false;
                diagnostics.messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Error,
                    "take_runtime_nonreplay",
                    "Retake slot 3: its exact Viseme trace cannot be represented."));

                Rebuild(window);

                Assert.That(window.rootVisualElement.Q<VisualElement>(
                    "takes-enrollment-page"), Is.Not.Null,
                    "A diagnosed take should be selected automatically without a review button click.");
                var problemSlot = window.rootVisualElement.Q<VisualElement>("take-slot-3");
                Assert.That(problemSlot, Is.Not.Null);
                Assert.That(problemSlot.ClassListContains(
                    "yucp-phrase-take-slot-problem"), Is.True);
                Assert.That(problemSlot.ClassListContains(
                    "yucp-phrase-take-slot-ready"), Is.False,
                    "A take awaiting replacement must not simultaneously display a success checkmark.");
                Assert.That(problemSlot.Q<Label>(
                    className: "yucp-phrase-take-slot-mark").text, Is.EqualTo("!"));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void EnrollmentOverlayDoesNotBlameAnArbitraryTakeForAPhraseLevelIssue()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });
            try
            {
                var diagnostics = draft.Enrollment.compiledModel.diagnostics;
                diagnostics.valid = false;
                diagnostics.messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Error,
                    "negative_runtime_margin",
                    "The optional ordinary-speech sample is too close to this phrase."));

                Rebuild(window);

                Assert.That(window.rootVisualElement.Q<VisualElement>(
                    "review-enrollment-page"), Is.Not.Null,
                    "A phrase-level issue must remain on review instead of selecting an unrelated take.");
                Assert.That(window.rootVisualElement.Q<VisualElement>(
                    "takes-enrollment-page"), Is.Null);
                Assert.That(window.rootVisualElement.Q<Button>(
                    "enrollment-rerecord-action"), Is.Null,
                    "An invalid review needs only one prominent correction action.");
                Assert.That(window.rootVisualElement.Q<Button>(
                    "enrollment-primary-action").text, Is.EqualTo("Re-record"));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void ReadyReviewFinishesCleanlyAndLetsTheCreatorChooseAnExactRetake()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var before = draft.Enrollment.positiveTakes.Select(take => take.takeId).ToArray();
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });
            try
            {
                var rootElement = window.rootVisualElement;
                Assert.That(rootElement.Q<Button>("enrollment-primary-action").text,
                    Is.EqualTo("Done"));
                Assert.That(rootElement.Q<Button>("enrollment-rerecord-action").text,
                    Is.EqualTo("Re-record all"));
                Assert.That(rootElement.Q<Button>("enrollment-skip-action").text,
                    Is.EqualTo("Skip for now"));
                Assert.That(rootElement.Query<Button>().ToList().Select(button => button.text),
                    Does.Not.Contain("Build & Test").And.Not.Contain("Open VRChat SDK"));

                var thirdSlot = rootElement.Q<VisualElement>("take-slot-3");
                Assert.That(thirdSlot.ClassListContains(
                    "yucp-phrase-take-slot-clickable"), Is.True);
                Assert.That(thirdSlot.focusable, Is.True);

                typeof(VisemePhraseEnrollmentOverlay)
                    .GetMethod("SelectTakeForReplacement",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(window, new object[] { 2 });

                var flow = (VisemePhraseEnrollmentFlow)typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("flow", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(window);
                Assert.That(flow, Is.Not.Null);
                Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
                Assert.That(flow.TakeIndex, Is.EqualTo(2));
                Assert.That(draft.Enrollment.positiveTakes.Select(take => take.takeId),
                    Is.EqualTo(before),
                    "Choosing a retake must keep the old recording until a replacement succeeds.");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void ReadyReviewRerecordAllQueuesEveryTakeAndKeepsSingleSlotRetakesSeparate()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var before = draft.Enrollment.positiveTakes.Select(take => take.takeId).ToArray();
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });

            try
            {
                var overlayType = typeof(VisemePhraseEnrollmentOverlay);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var flowField = overlayType.GetField("flow", flags);
                var acceptedTakeField = overlayType.GetField("acceptedTake", flags);
                var rerecordAllField = overlayType.GetField("rerecordAllRequested", flags);
                var resumeMethod = overlayType.GetMethod("ResumeAutomaticEnrollment", flags);

                overlayType.GetMethod("HandleRerecord", flags)?.Invoke(window, null);

                var flow = (VisemePhraseEnrollmentFlow)flowField?.GetValue(window);
                Assert.That(flow, Is.Not.Null);
                Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
                Assert.That(flow.TakeIndex, Is.EqualTo(0));
                Assert.That(rerecordAllField?.GetValue(window), Is.EqualTo(true));
                Assert.That(draft.Enrollment.positiveTakes.Select(take => take.takeId),
                    Is.EqualTo(before),
                    "Starting a full re-record must preserve every saved take until its replacement succeeds.");

                for (var completedIndex = 0; completedIndex < 4; completedIndex++)
                {
                    acceptedTakeField?.SetValue(window, completedIndex);
                    resumeMethod?.Invoke(window, null);

                    flow = (VisemePhraseEnrollmentFlow)flowField?.GetValue(window);
                    if (completedIndex < 3)
                    {
                        Assert.That(flow?.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
                        Assert.That(flow?.TakeIndex, Is.EqualTo(completedIndex + 1));
                        Assert.That(rerecordAllField?.GetValue(window), Is.EqualTo(true));
                    }
                }

                Assert.That(flow?.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Review));
                Assert.That(rerecordAllField?.GetValue(window), Is.EqualTo(false));

                overlayType.GetMethod("SelectTakeForReplacement", flags)
                    ?.Invoke(window, new object[] { 2 });
                Assert.That(flow?.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
                Assert.That(flow?.TakeIndex, Is.EqualTo(2));
                Assert.That(rerecordAllField?.GetValue(window), Is.EqualTo(false));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void SkipForNowClosesWithoutDiscardingSavedTakes()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var before = draft.Enrollment.positiveTakes.Select(take => take.takeId).ToArray();
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });

            window.HandleSkipForNow();

            Assert.That(draft.Enrollment.positiveTakes.Select(take => take.takeId),
                Is.EqualTo(before));
        }

        [Test]
        public void AutomaticCompletionKeepsPhraseLevelIssueOnReview()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });
            try
            {
                var diagnostics = draft.Enrollment.compiledModel.diagnostics;
                diagnostics.valid = false;
                diagnostics.messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Error,
                    "negative_runtime_margin",
                    "The optional ordinary-speech sample is too close to this phrase."));

                var flow = (VisemePhraseEnrollmentFlow)typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("flow", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(window);
                Assert.That(flow, Is.Not.Null);
                flow.SelectTake(3);
                typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("acceptedTake", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(window, 3);

                typeof(VisemePhraseEnrollmentOverlay)
                    .GetMethod("ResumeAutomaticEnrollment",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(window, null);

                Assert.That(window.rootVisualElement.Q<VisualElement>(
                    "review-enrollment-page"), Is.Not.Null,
                    "Automatic progression must leave a phrase-level problem on review.");
                Assert.That(window.rootVisualElement.Q<VisualElement>(
                    "takes-enrollment-page"), Is.Null);
                Assert.That(window.rootVisualElement.Query<VisualElement>(
                        className: "yucp-phrase-take-slot-problem").ToList(),
                    Is.Empty,
                    "A global issue must not mark the most recently completed take as faulty.");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void RejectedReplacementReturnsToReviewWithoutRearmingOrLosingSavedTake()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++) draft.SaveTake(index, UsefulTake(0));
            var before = draft.Enrollment.positiveTakes.Select(take => take.takeId).ToArray();
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });
            try
            {
                var flow = (VisemePhraseEnrollmentFlow)typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("flow", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(window);
                Assert.That(flow, Is.Not.Null);
                flow.SelectTake(1);
                typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("rejectedTake", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(window, 1);
                typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("returnToReviewAfterRejectedReplacement",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(window, true);

                typeof(VisemePhraseEnrollmentOverlay)
                    .GetMethod("ResumeAutomaticEnrollment",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(window, null);

                Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Review));
                Assert.That((bool)typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("retakeRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(window), Is.False,
                    "A rejected optional replacement must not auto-arm the same slot forever.");
                Assert.That(draft.Enrollment.positiveTakes.Select(take => take.takeId),
                    Is.EqualTo(before));
                Assert.That(window.rootVisualElement.Q<VisualElement>(
                    "review-enrollment-page"), Is.Not.Null);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void NaturalReadyReplacementIsAcceptedAsBoundedExactPath()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 4; index++)
                draft.SaveTake(index, StableTake(12, 14, 1));
            Assert.That(draft.Enrollment.compiledModel?.diagnostics?.valid, Is.True);
            var before = draft.Enrollment.positiveTakes.Select(take => take.takeId).ToArray();
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });
            try
            {
                var accepted = (bool)typeof(VisemePhraseEnrollmentOverlay)
                    .GetMethod("TrySavePositiveTake",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { 3, StableTake(2, 3, 4) });

                Assert.That(accepted, Is.True);
                var after = draft.Enrollment.positiveTakes.Select(take => take.takeId).ToArray();
                Assert.That(after.Take(3), Is.EqualTo(before.Take(3)));
                Assert.That(after[3], Is.Not.EqualTo(before[3]),
                    "A natural alternate take must replace only its requested slot.");
                Assert.That(draft.Enrollment.compiledModel?.diagnostics?.valid, Is.True);
                var replay = VisemePhraseModelCompiler.Compile(
                    component.phrases[0], draft.Enrollment);
                Assert.That(replay.success, Is.True);
                Assert.That(replay.processedPositiveTakes.All(take =>
                    VisemePhraseModelCompiler.Score(replay.model, take, false) <=
                    replay.model.acceptanceCost + 1e-5f), Is.True);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void FailedRequiredCompilerRetakeRemainsArmedInsteadOfDeadEnding()
        {
            var draft = new VisemePhraseEnrollmentDraft(component, component.phrases[0]);
            createdAssetPath = draft.AssetPath;
            for (var index = 0; index < 3; index++)
                draft.SaveTake(index, StableTake(12, 14, 1));
            draft.SaveTake(3, StableTake(
                2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14));
            Assert.That(draft.Enrollment.compiledModel?.diagnostics?.valid, Is.False);
            var window = VisemePhraseEnrollmentOverlay.OpenForDrafts(
                new IVisemePhraseEnrollmentDraft[] { draft });
            try
            {
                var flow = (VisemePhraseEnrollmentFlow)typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("flow", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(window);
                Assert.That(flow, Is.Not.Null);

                typeof(VisemePhraseEnrollmentOverlay)
                    .GetMethod("PrepareAutomaticTake",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(window, new object[] { 3 });
                Assert.That((bool)typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("returnToReviewAfterRejectedReplacement",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(window), Is.False,
                    "A required compiler repair is not an optional replacement of a ready model.");

                typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("rejectedTake", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(window, 3);
                typeof(VisemePhraseEnrollmentOverlay)
                    .GetMethod("ResumeAutomaticEnrollment",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(window, null);

                Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
                Assert.That((bool)typeof(VisemePhraseEnrollmentOverlay)
                    .GetField("retakeRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(window), Is.True,
                    "The required slot must remain armed after an unusable attempt.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void Rebuild(VisemePhraseEnrollmentOverlay window)
        {
            typeof(VisemePhraseEnrollmentOverlay)
                .GetMethod("BuildUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(window, null);
        }

        private static VisemePhraseCapturedTake UsefulTake(int offset)
        {
            var take = new VisemePhraseCapturedTake
            {
                backend = "Oculus LipSync",
                durationSeconds = 12 * 1024d / 48000d
            };
            for (var i = 0; i < 12; i++)
            {
                var run = i / 3;
                take.frames.Add(new VisemePhraseCapturedFrame(
                    1 + (run + offset) % 14,
                    0.55f,
                    i * 1024L,
                    48000));
            }
            return take;
        }

        private static VisemePhraseCapturedTake StableTake(params int[] visemes)
        {
            var take = new VisemePhraseCapturedTake
            {
                backend = "Oculus LipSync"
            };
            var clock = 0L;
            foreach (var viseme in visemes)
            for (var block = 0; block < 4; block++)
            {
                take.frames.Add(new VisemePhraseCapturedFrame(
                    viseme,
                    0.55f,
                    clock,
                    48000));
                clock += 1024L;
            }
            take.durationSeconds = clock / 48000d;
            return take;
        }
    }
}
