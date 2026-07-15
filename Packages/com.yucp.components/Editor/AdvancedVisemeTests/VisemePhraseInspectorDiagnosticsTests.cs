using System.Collections.Generic;
using NUnit.Framework;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseInspectorDiagnosticsTests
    {
        [Test]
        public void CompiledDiagnosticsExposeAliasesAndLearnedTiming()
        {
            var model = new VisemePhraseCompiledModel
            {
                variants = new List<VisemePhraseModelVariant>
                {
                    new VisemePhraseModelVariant
                    {
                        states = new List<VisemePhraseModelState>
                        {
                            State(1, new[] { 2 }, 0.04f, 0.08f, 0.13f),
                            State(10, new[] { 11, 12 }, 0.06f, 0.1f, 0.2f),
                            State(7, null, 0.03f, 0.06f, 0.12f)
                        }
                    }
                }
            };

            var branches = VisemePhraseInspectorDiagnostics.Branches(model);
            Assert.That(branches, Has.Count.EqualTo(1));
            Assert.That(branches[0], Does.Contain("[PP | FF]"));
            Assert.That(branches[0], Does.Contain("[aa | E | I]"));
            Assert.That(branches[0], Does.Contain("SS"));

            var timing = VisemePhraseInspectorDiagnostics.Timing(model);
            Assert.That(timing.available, Is.True);
            Assert.That(timing.minimum, Is.EqualTo(0.03f).Within(1e-6f));
            Assert.That(timing.median, Is.EqualTo(0.08f).Within(1e-6f));
            Assert.That(timing.maximum, Is.EqualTo(0.2f).Within(1e-6f));
        }

        [Test]
        public void CalibrationAndMemorySummariesAreExact()
        {
            var model = new VisemePhraseCompiledModel
            {
                negativeCalibration = new VisemePhraseNegativeCalibration
                {
                    calibrated = true,
                    negativeTraceCount = 1,
                    separation = 0.1875f
                }
            };
            Assert.That(
                VisemePhraseInspectorDiagnostics.Calibration(model),
                Does.Contain("0.188 margin"));

            var memory = VisemePhraseInspectorDiagnostics.ParameterMemory(
                117,
                new[] { "YUCP/Phrase/one" },
                new[]
                {
                    "YUCP/Phrase/one",
                    "YUCP/Phrase/two",
                    "YUCP/Phrase/three"
                });
            Assert.That(memory.phraseCount, Is.EqualTo(3));
            Assert.That(memory.existingBits, Is.EqualTo(117));
            Assert.That(memory.newBits, Is.EqualTo(2));
            Assert.That(memory.estimatedTotal, Is.EqualTo(119));
        }

        private static VisemePhraseModelState State(
            int primary,
            int[] aliases,
            float minimum,
            float median,
            float maximum)
        {
            return new VisemePhraseModelState
            {
                primaryViseme = primary,
                aliasVisemes = aliases,
                minimumDurationSeconds = minimum,
                medianDurationSeconds = median,
                maximumDurationSeconds = maximum
            };
        }
    }
}
