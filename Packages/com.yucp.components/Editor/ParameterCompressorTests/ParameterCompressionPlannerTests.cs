using System;
using System.Linq;
using NUnit.Framework;

namespace YUCP.Components.Editor.Tests
{
    public sealed class ParameterCompressionPlannerTests
    {
        [Test]
        public void BusTakesValuesUntilItPaysForItself()
        {
            var plan = ParameterCompressionPlanner.CreatePlan(258, 256, new[]
            {
                Signed("A"), Signed("B"), Signed("C")
            });

            // One Float saves 8 bits but the Int code costs 8; two need a page bit.
            Assert.That(plan.Allocations.Select(allocation => allocation.Name),
                Is.EqualTo(new[] { "A", "B" }));
            Assert.That(plan.PageBits, Is.EqualTo(1));
            Assert.That(plan.BusBits, Is.EqualTo(9));
            Assert.That(plan.FinalBits, Is.EqualTo(258 + 9 - 16));
        }

        [Test]
        public void ThirtyTwoRadialsShareTwelveBitsAtNativePrecision()
        {
            var candidates = Enumerable.Range(0, 32)
                .Select(index => new ParameterCompressionCandidate(
                    "Radial/" + index.ToString("D2"), 128, 0, 8, true))
                .ToArray();
            var plan = ParameterCompressionPlanner.CreatePlan(256, 256, candidates);

            Assert.That(plan.BusBits, Is.EqualTo(12), "log2(32 * 128) = 12");
            Assert.That(plan.FinalBits, Is.EqualTo(12));
            AssertDisjoint(plan);
        }

        [Test]
        public void LayoutIsDisjointAndIndependentOfDiscoveryOrder()
        {
            var random = new Random(20261013);
            var candidates = Enumerable.Range(0, 40)
                .Select(index => new ParameterCompressionCandidate(
                    "Value/" + index.ToString("D2"),
                    new[] { 2, 2, 6, 128, 255, 256 }[random.Next(6)],
                    index % 3,
                    index % 4 == 0 ? 1 : 8))
                .ToArray();
            var expected = Signature(ParameterCompressionPlanner.CreatePlan(400, 200, candidates));

            for (var seed = 0; seed < 30; seed++)
            {
                var shuffle = new Random(seed);
                var plan = ParameterCompressionPlanner.CreatePlan(400, 200,
                    candidates.OrderBy(_ => shuffle.Next()).ToArray());
                Assert.That(Signature(plan), Is.EqualTo(expected), "Seed " + seed);
                AssertDisjoint(plan);
            }
        }

        [Test]
        public void RequiredValuesRideTheBusEvenWithinBudget()
        {
            var plan = ParameterCompressionPlanner.CreatePlan(100, 256, new[]
            {
                new ParameterCompressionCandidate("Required", 2, 0, 1, true),
                Signed("Optional")
            });

            Assert.That(plan.Allocations.Select(allocation => allocation.Name),
                Is.EqualTo(new[] { "Required" }));
        }

        [Test]
        public void NoBusWhenAlreadyWithinBudget()
        {
            var plan = ParameterCompressionPlanner.CreatePlan(200, 256, new[] { Signed("Optional") });

            Assert.That(plan.UsesCompression, Is.False);
            Assert.That(plan.BusBits, Is.Zero);
            Assert.That(plan.FinalBits, Is.EqualTo(200));
        }

        [Test]
        public void FailsWhenEveryValueStillDoesNotFit()
        {
            Assert.That(ParameterCompressionPlanner.TryCreatePlan(300, 256,
                    new[] { Signed("A"), Signed("B") }, out var plan, out var error),
                Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(error, Does.Contain("still leaves"));
        }

        private static ParameterCompressionCandidate Signed(string name)
        {
            return new ParameterCompressionCandidate(name, 255, 0, 8);
        }

        private static void AssertDisjoint(ParameterCompressionPlan plan)
        {
            Assert.That(plan.Allocations.Max(allocation => allocation.Page),
                Is.LessThan(1 << plan.PageBits));
            foreach (var page in plan.Allocations.GroupBy(allocation => allocation.Page))
            {
                var end = 0;
                foreach (var allocation in page.OrderBy(allocation => allocation.Offset))
                {
                    Assert.That(allocation.Offset, Is.GreaterThanOrEqualTo(end), allocation.Name);
                    end = allocation.Offset + allocation.Levels;
                }
                Assert.That(end, Is.LessThanOrEqualTo(ParameterCompressionPlanner.PageSize));
            }
        }

        private static string Signature(ParameterCompressionPlan plan)
        {
            return plan.FinalBits + "|" + plan.PageBits + "|" + string.Join(",",
                plan.Allocations.Select(allocation =>
                    allocation.Name + "@" + allocation.Page + ":" + allocation.Offset));
        }
    }
}
