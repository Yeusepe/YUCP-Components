#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseEnrollmentPlayModeCoordinatorTests
    {
        [SetUp]
        public void SetUp()
        {
            VisemePhraseEnrollmentPlayModeCoordinator.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            VisemePhraseEnrollmentPlayModeCoordinator.ResetForTests();
            foreach (var descriptor in UnityEngine.Resources
                         .FindObjectsOfTypeAll<VRCAvatarDescriptor>()
                         .Where(candidate => candidate != null &&
                                             candidate.gameObject.name.StartsWith(
                                                 "Phrase coordinator test")))
                Object.DestroyImmediate(descriptor.gameObject);
        }

        [Test]
        public void StableLocatorResolvesExactNestedAuthoringComponents()
        {
            var root = CreateAvatar("Phrase coordinator test locator");
            var nested = new GameObject("Nested");
            nested.transform.SetParent(root.transform, false);
            var nestedSecond = new GameObject("Nested Second");
            nestedSecond.transform.SetParent(nested.transform, false);
            var first = AddTrigger(nested, "first", "Open portal");
            var second = AddTrigger(nestedSecond, "second", "Close portal");

            var locator = VisemePhraseEnrollmentPlayModeCoordinator
                .CaptureLocator(root, new[] { first, second });

            Assert.That(locator, Is.Not.Null);
            Assert.That(locator.components, Has.Length.EqualTo(2));
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .TryResolveLocator(locator, out var resolvedRoot,
                    out var resolvedComponents), Is.True);
            Assert.That(resolvedRoot, Is.SameAs(root));
            CollectionAssert.AreEqual(
                new[] { first, second },
                resolvedComponents);
        }

        [Test]
        public void SkipTokenIsIgnoredBySdkAndConsumedByExactlyOnePreviewPipeline()
        {
            var root = CreateAvatar("Phrase coordinator test skip");
            var trigger = AddTrigger(root, "skip", "Explosion");
            var locator = VisemePhraseEnrollmentPlayModeCoordinator
                .CaptureLocator(root, new[] { trigger });
            VisemePhraseEnrollmentPlayModeCoordinator.QueueSkipForTests(locator);

            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipPreflightForTests(root, isPreview: false), Is.False,
                "An SDK build must never consume or honor a preview skip token.");
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipGenerationForTests(root, isPreview: false), Is.False);

            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipPreflightForTests(root, isPreview: true), Is.True);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipGenerationForTests(root, isPreview: true), Is.True,
                "The main processor shares the preflight's per-root skip state.");
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipPreflightForTests(root, isPreview: true), Is.True,
                "Repeated VRCFury callbacks in the same preview remain skipped.");
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipGenerationForTests(root, isPreview: true), Is.True);

            VisemePhraseEnrollmentPlayModeCoordinator.EndPreviewForTests();

            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipPreflightForTests(root, isPreview: true), Is.False,
                "The skip expires when that one preview returns to Edit Mode.");
        }

        [Test]
        public void SkipTokenCannotBeConsumedByAnotherAvatarWithSamePhrase()
        {
            var intended = CreateAvatar("Phrase coordinator test intended");
            var other = CreateAvatar("Phrase coordinator test other");
            var intendedTrigger = AddTrigger(intended, "same", "Explosion");
            AddTrigger(other, "same", "Explosion");
            var locator = VisemePhraseEnrollmentPlayModeCoordinator
                .CaptureLocator(intended, new[] { intendedTrigger });
            VisemePhraseEnrollmentPlayModeCoordinator.QueueSkipForTests(locator);

            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipPreflightForTests(other, isPreview: true), Is.False);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipPreflightForTests(intended, isPreview: true), Is.True);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipGenerationForTests(intended, isPreview: true), Is.True);
        }

        [Test]
        public void InterruptedPreviewSkipsOnlyRemainderOfCurrentCallback()
        {
            var root = CreateAvatar("Phrase coordinator test interrupted");
            AddTrigger(root, "interrupted", "Activate");
            VisemePhraseEnrollmentPlayModeCoordinator.MarkInterruptedForTests(root);

            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipGenerationForTests(root, isPreview: true), Is.True);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipGenerationForTests(root, isPreview: true), Is.False);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .ShouldSkipGenerationForTests(root, isPreview: false), Is.False);
        }

        [Test]
        public void OnlyEnrollmentFailuresRequestPlayModeHandoff()
        {
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .IsEnrollmentIssue("'Avatar/Trigger' has no enrollment profile."), Is.True);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .IsEnrollmentIssue("The compiled model is outdated"), Is.True);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .IsEnrollmentIssue("Animator parameter has the wrong type"), Is.False);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .IsEnrollmentIssue("Add at least one enrolled phrase before building the avatar."),
                Is.False,
                "A structural empty component must not open an empty enrollment wizard.");
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .IsEnrollmentIssue("Every phrase needs non-empty prompt text."), Is.False);
            Assert.That(VisemePhraseEnrollmentPlayModeCoordinator
                .IsEnrollmentIssue("Viseme Phrase Trigger requires an Advanced Viseme Reconstructor on the same avatar."),
                Is.False);
        }

        private static GameObject CreateAvatar(string name)
        {
            var root = new GameObject(name);
            root.AddComponent<VRCAvatarDescriptor>();
            return root;
        }

        private static VisemePhraseTriggerData AddTrigger(
            GameObject owner,
            string id,
            string prompt)
        {
            var trigger = owner.AddComponent<VisemePhraseTriggerData>();
            trigger.phrases.Add(new VisemePhraseDefinition
            {
                id = id,
                parameterKey = id,
                prompt = prompt
            });
            trigger.EnsureDefaults();
            return trigger;
        }
    }
}
#endif
