using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseCaptureTests
    {
        [Test]
        public void SampleClockCaptureIsIndependentOfBacklogDeliveryChunking()
        {
            const int sampleRate = 48000;
            const int frameSamples = 1024;
            var immediate = new VisemePhraseCaptureBuffer();
            var backlogged = new VisemePhraseCaptureBuffer();

            for (var i = 0; i < 36; i++)
                AppendSynthetic(immediate, i, frameSamples, sampleRate);

            // Simulate editor ticks delivering 1, 8, 3 and then 24 queued analyzer
            // blocks. The recorder sees every block and timestamps by sample clock.
            var cursor = 0;
            foreach (var chunk in new[] { 1, 8, 3, 24 })
            {
                for (var i = 0; i < chunk; i++)
                    AppendSynthetic(backlogged, cursor++, frameSamples, sampleRate);
            }

            var a = immediate.Finish();
            var b = backlogged.Finish();
            Assert.That(b.frames.Count, Is.EqualTo(a.frames.Count));
            Assert.That(b.durationSeconds, Is.EqualTo(a.durationSeconds).Within(1e-9));
            for (var i = 0; i < a.frames.Count; i++)
            {
                Assert.That(b.frames[i].sampleClock, Is.EqualTo(a.frames[i].sampleClock));
                Assert.That(b.frames[i].viseme, Is.EqualTo(a.frames[i].viseme));
                Assert.That(b.frames[i].voice, Is.EqualTo(a.frames[i].voice).Within(1e-6f));
            }
        }

        [Test]
        public void CaptureRejectsDuplicateOrRegressingSampleClocks()
        {
            var capture = new VisemePhraseCaptureBuffer();
            Assert.That(capture.Append(1, 0.4f, 1024, 48000, "Oculus LipSync"), Is.True);
            Assert.That(capture.Append(2, 0.5f, 1024, 48000, "Oculus LipSync"), Is.False);
            Assert.That(capture.Append(2, 0.5f, 512, 48000, "Oculus LipSync"), Is.False);
            Assert.That(capture.Append(2, 0.5f, 2048, 44100, "Oculus LipSync"), Is.False);
            Assert.That(capture.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConfirmedOnsetSlicesRejectedSpikeButFullCapturePreservesIt()
        {
            const int sampleRate = 48000;
            const int step = 1024;
            var capture = new VisemePhraseCaptureBuffer();
            for (var i = 0; i < 32; i++)
            {
                var rejectedSpike = i == 1;
                var phrase = i >= 24 && i <= 29;
                capture.Append(
                    rejectedSpike ? 14 : phrase ? 10 : 0,
                    rejectedSpike ? 0.7f : phrase ? 0.55f : 0f,
                    i * (long)step,
                    sampleRate,
                    "Oculus LipSync");
            }

            var onsetClock = 24L * step;
            var positive = capture.Finish(onsetClock, 0.045d);
            var full = capture.Finish(-1L, 0.045d);

            Assert.That(full.frames, Has.Count.EqualTo(32));
            Assert.That(full.frames.Exists(frame => frame.viseme == 14), Is.True);
            Assert.That(positive.frames.Count, Is.LessThan(full.frames.Count));
            Assert.That(positive.frames.Exists(frame => frame.viseme == 14), Is.False);
            Assert.That(positive.frames[0].sampleClock, Is.Zero);
            Assert.That(positive.frames[0].viseme, Is.Zero,
                "The positive trace should retain a short silence pre-roll.");
            Assert.That(positive.frames.FindIndex(frame => frame.viseme == 10),
                Is.GreaterThan(0));
            Assert.That(full.durationSeconds,
                Is.EqualTo(31d * step / sampleRate).Within(1e-9));
        }

        [Test]
        public void LosslessEnrollmentUsesLongRingAndPreservesSingleWrapBacklog()
        {
            var root = new GameObject("Lossless Capture Test");
            try
            {
                var emulator = root.AddComponent<VisemeTestEmulatorData>();
                Assert.That(
                    VisemeTestPreviewSession.MicrophoneBufferSeconds(emulator),
                    Is.EqualTo(VisemeTestPreviewSession.PreviewMicrophoneBufferSeconds));
                using (VisemeTestPreviewSession.BeginLosslessAnalysis(emulator))
                {
                    Assert.That(
                        VisemeTestPreviewSession.MicrophoneBufferSeconds(emulator),
                        Is.GreaterThanOrEqualTo(30));
                    Assert.That(
                        VisemeTestPreviewSession.AvailableMicrophoneSamples(
                            29 * 48000,
                            2 * 48000,
                            30 * 48000),
                        Is.EqualTo(3 * 48000));
                }
                Assert.That(
                    VisemeTestPreviewSession.MicrophoneBufferSeconds(emulator),
                    Is.EqualTo(VisemeTestPreviewSession.PreviewMicrophoneBufferSeconds));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnrollmentRequiresExactlyFourUsefulTakes()
        {
            var takes = new List<VisemePhraseCapturedTake>
            {
                UsefulTake(), UsefulTake(), UsefulTake(), UsefulTake()
            };
            var ready = VisemePhraseEnrollmentStatus.Evaluate("hello avatar", takes);
            Assert.That(ready.IsReady, Is.True);
            Assert.That(ready.CompletedTakes, Is.EqualTo(4));

            takes[2] = null;
            var incomplete = VisemePhraseEnrollmentStatus.Evaluate("hello avatar", takes);
            Assert.That(incomplete.IsReady, Is.False);
            Assert.That(incomplete.BlockingReason, Does.Contain("slot 3"));

            takes.Add(UsefulTake());
            var tooMany = VisemePhraseEnrollmentStatus.Evaluate("hello avatar", takes);
            Assert.That(tooMany.IsReady, Is.False);
            Assert.That(tooMany.issues.Exists(issue => issue.message.Contains("exactly 4")), Is.True);
        }

        [Test]
        public void FallbackClassifierIsAWarningRatherThanABlocker()
        {
            var takes = new List<VisemePhraseCapturedTake>
            {
                UsefulTake("YUCP fallback"), UsefulTake(), UsefulTake(), UsefulTake()
            };
            var status = VisemePhraseEnrollmentStatus.Evaluate("hello avatar", takes);
            Assert.That(status.IsReady, Is.True);
            Assert.That(status.issues.Exists(issue =>
                issue.level == VisemePhraseEnrollmentIssueLevel.Warning &&
                issue.message.Contains("fallback")), Is.True);
        }

        private static void AppendSynthetic(
            VisemePhraseCaptureBuffer capture,
            int index,
            int frameSamples,
            int sampleRate)
        {
            var viseme = index % 7 == 0 ? 0 : 1 + index % 14;
            capture.Append(
                viseme,
                viseme == 0 ? 0.01f : 0.55f,
                (long)(index + 1) * frameSamples,
                sampleRate,
                "Oculus LipSync");
        }

        private static VisemePhraseCapturedTake UsefulTake(string backend = "Oculus LipSync")
        {
            var take = new VisemePhraseCapturedTake
            {
                backend = backend,
                durationSeconds = 0.5d
            };
            for (var i = 0; i < 8; i++)
                take.frames.Add(new VisemePhraseCapturedFrame(
                    i % 2 == 0 ? 1 + i / 2 : 1 + (i - 1) / 2,
                    0.5f,
                    i * 1024L,
                    48000));
            return take;
        }
    }
}
