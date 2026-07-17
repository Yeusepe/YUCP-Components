using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace YUCP.Components.Editor.Tests
{
    public sealed class ParameterCompressionPlannerTests
    {
        private static readonly ParameterCompressionBusPolicy SixWireThreeDigitPolicy =
            new ParameterCompressionBusPolicy(6, 6, 3, 0.1f);

        [Test]
        public void PlannerUsesSixBitsToMeetAnExactBudget()
        {
            var plan = ParameterCompressionPlanner.CreatePlan(
                258,
                256,
                new[]
                {
                    Float("Setting", required: false)
                },
                SixWireThreeDigitPolicy);

            Assert.That(plan.UsesCompression, Is.True);
            Assert.That(plan.BusBits, Is.EqualTo(6));
            Assert.That(plan.CarrierBits, Is.EqualTo(6));
            Assert.That(plan.FinalBits, Is.EqualTo(256));
            Assert.That(plan.Allocations.Select(allocation => allocation.Name),
                Is.EqualTo(new[] { "Setting" }));
            Assert.That(plan.Allocations[0].Levels, Is.EqualTo(255));
            Assert.That(plan.Radix, Is.EqualTo(19));
            Assert.That(plan.DigitCount, Is.EqualTo(2));
        }

        [Test]
        public void TwentySixVrChatFloatsRemainFullPrecisionInThreeDigits()
        {
            var candidates = Enumerable.Range(0, 26)
                .Select(index => Float("Float/" + index.ToString("D2"), required: true))
                .ToArray();
            var plan = ParameterCompressionPlanner.CreatePlan(
                candidates.Sum(candidate => candidate.CurrentBits),
                256,
                candidates,
                SixWireThreeDigitPolicy);

            Assert.That(plan.Radix, Is.EqualTo(19));
            Assert.That(plan.DigitCount, Is.EqualTo(3));
            Assert.That(plan.TotalCardinality, Is.EqualTo(26L * 255L));
            Assert.That(plan.Allocations, Has.All.Matches<ParameterCompressionAllocation>(
                allocation => allocation.Levels == 255));
            Assert.That(plan.Layout.Entries.Select(entry => entry.Offset),
                Is.EqualTo(Enumerable.Range(0, 26).Select(index => index * 255L)));
        }

        [Test]
        public void PrecisionWaterFillsDeterministicallyAtRadixCliff()
        {
            var candidates = Enumerable.Range(0, 27)
                .Select(index => Float("Float/" + index.ToString("D2"), required: true))
                .ToArray();
            var baseline = ParameterCompressionPlanner.CreatePlan(
                candidates.Sum(candidate => candidate.CurrentBits),
                256,
                candidates,
                SixWireThreeDigitPolicy);

            Assert.That(baseline.DigitCount, Is.EqualTo(3));
            Assert.That(baseline.TotalCardinality, Is.EqualTo(6859));
            Assert.That(baseline.Allocations.Single(allocation =>
                allocation.Name == "Float/00").Levels, Is.EqualTo(255));
            Assert.That(baseline.Allocations.Where(allocation =>
                    allocation.Name != "Float/00"),
                Has.All.Matches<ParameterCompressionAllocation>(allocation =>
                    allocation.Levels == 254));

            var expected = Signature(baseline);
            for (var seed = 0; seed < 40; seed++)
            {
                var random = new Random(seed);
                var shuffled = candidates.OrderBy(_ => random.Next()).ToArray();
                var plan = ParameterCompressionPlanner.CreatePlan(
                    candidates.Sum(candidate => candidate.CurrentBits),
                    256,
                    shuffled,
                    SixWireThreeDigitPolicy);
                Assert.That(Signature(plan), Is.EqualTo(expected), "Seed " + seed);
            }
        }

        [Test]
        public void HigherPriorityWinsOnlyTheDeterministicCliffRemainder()
        {
            var candidates = Enumerable.Range(0, 27)
                .Select(index => new ParameterCompressionCandidate(
                    "Float/" + index.ToString("D2"),
                    ParameterCompressionValueKind.Float,
                    2,
                    255,
                    index == 19 ? 100 : 0,
                    8,
                    true))
                .ToArray();
            var plan = ParameterCompressionPlanner.CreatePlan(
                216, 256, candidates, SixWireThreeDigitPolicy);

            Assert.That(plan.Allocations.Single(allocation =>
                allocation.Name == "Float/19").Levels, Is.EqualTo(255));
            Assert.That(plan.Allocations.Where(allocation =>
                    allocation.Name != "Float/19"),
                Has.All.Matches<ParameterCompressionAllocation>(allocation =>
                    allocation.Levels == 254));
        }

        [Test]
        public void CandidateSelectionIsDeterministicAcrossDiscoveryOrder()
        {
            var candidates = Enumerable.Range(0, 12)
                .Select(index => new ParameterCompressionCandidate(
                    "Candidate/" + index.ToString("D2"),
                    index % 3 == 0
                        ? ParameterCompressionValueKind.Bool
                        : ParameterCompressionValueKind.Float,
                    index % 3 == 0 ? 2 : 32,
                    index % 3 == 0 ? 2 : 255,
                    index % 4,
                    index % 3 == 0 ? 1 : 8))
                .ToArray();
            var baseline = ParameterCompressionPlanner.CreatePlan(
                300, 256, candidates, SixWireThreeDigitPolicy);
            var signature = Signature(baseline);

            for (var seed = 0; seed < 50; seed++)
            {
                var random = new Random(seed + 500);
                var shuffled = candidates.OrderBy(_ => random.Next()).ToArray();
                var plan = ParameterCompressionPlanner.CreatePlan(
                    300, 256, shuffled, SixWireThreeDigitPolicy);
                Assert.That(Signature(plan), Is.EqualTo(signature), "Seed " + seed);
            }
        }

        [Test]
        public void PlannerRejectsMinimumPrecisionBeyondRadixCapacity()
        {
            var candidates = Enumerable.Range(0, 27)
                .Select(index => new ParameterCompressionCandidate(
                    "Exact/" + index.ToString("D2"),
                    ParameterCompressionValueKind.Float,
                    255,
                    255,
                    0,
                    8,
                    true))
                .ToArray();

            Assert.That(ParameterCompressionPlanner.TryCreatePlan(
                    216,
                    256,
                    candidates,
                    SixWireThreeDigitPolicy,
                    out var plan,
                    out var error),
                Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(error, Does.Contain("radix capacity"));
        }

        [Test]
        public void PlannerIsNoOpWhenAlreadyWithinBudgetAndNothingIsRequired()
        {
            var plan = ParameterCompressionPlanner.CreatePlan(
                200,
                256,
                new[] { Float("Optional", required: false) },
                SixWireThreeDigitPolicy);

            Assert.That(plan.UsesCompression, Is.False);
            Assert.That(plan.BusBits, Is.Zero);
            Assert.That(plan.FinalBits, Is.EqualTo(200));
            Assert.That(plan.Allocations, Is.Empty);
            Assert.That(plan.Layout, Is.Null);
        }

        [Test]
        public void EstimatedLatencyMatchesExactFramedWordCount()
        {
            var candidates = Enumerable.Range(0, 26)
                .Select(index => Float("Float/" + index.ToString("D2"), required: true))
                .ToArray();
            var plan = ParameterCompressionPlanner.CreatePlan(
                208, 256, candidates, SixWireThreeDigitPolicy);

            // zero, sync, zero, three (digit, zero) pairs, sync, zero
            Assert.That(plan.EstimatedFrameSeconds, Is.EqualTo(1.1f).Within(1e-6f));
            Assert.That(plan.EstimatedFullRefreshSeconds,
                Is.EqualTo(28.6f).Within(1e-5f));
        }

        private static ParameterCompressionCandidate Float(
            string name,
            bool required)
        {
            return new ParameterCompressionCandidate(
                name,
                ParameterCompressionValueKind.Float,
                2,
                ParameterCompressionProtocol.VrChatFloatCardinality,
                0,
                8,
                required);
        }

        private static string Signature(ParameterCompressionPlan plan)
        {
            return string.Join("|", new[]
            {
                plan.BusBits.ToString(),
                plan.Radix.ToString(),
                plan.DigitCount.ToString(),
                plan.FinalBits.ToString(),
                plan.TotalCardinality.ToString(),
                string.Join(",", plan.Allocations.Select(allocation =>
                    allocation.Name + ":" + allocation.Levels + "@" + allocation.Offset))
            });
        }
    }
}
