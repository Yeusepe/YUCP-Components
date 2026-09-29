using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public class LiveGripCloseTests
    {
        private const float Thickness = 0.05f;

        /// <summary>Planar finger: three unit segments from the origin along +x, each joint curling 90 degrees towards -y.</summary>
        private static Vector3[] Finger(float[] curl)
        {
            var points = new Vector3[4];
            var angle = 0f;
            for (var j = 0; j < 3; j++)
            {
                angle += curl[j] * Mathf.PI / 2f;
                points[j + 1] = points[j] + new Vector3(Mathf.Cos(angle), -Mathf.Sin(angle), 0f);
            }
            return points;
        }

        private static Func<Vector3, float> Sphere(Vector3 center, float radius) =>
            p => Mathf.Max(0f, Vector3.Distance(p, center) - radius);

        private static void AssertNeverPenetrates(float[] curl, Func<Vector3, float> gap)
        {
            var points = Finger(curl);
            for (var s = 0; s < 3; s++)
            for (var t = 0.25f; t <= 1f; t += 0.25f)
                Assert.GreaterOrEqual(gap(Vector3.Lerp(points[s], points[s + 1], t)), Thickness, $"segment {s} sinks in at t={t}");
        }

        [Test]
        public void FingertipStopsOnSurfaceAhead()
        {
            Func<Vector3, float> floor = p => Mathf.Max(0f, p.y + 1.2f);
            var curl = LiveGripProcessor.CloseUntilContact(Finger, floor, Thickness, out var touching);

            Assert.AreEqual(2, touching);
            AssertNeverPenetrates(curl, floor);
            Assert.Less(floor(Finger(curl)[3]), 0.3f, "tip should rest near the surface");
        }

        [Test]
        public void FingerWrapsAfterProximalTouches()
        {
            var gap = Sphere(new Vector3(0.6f, -0.45f, 0f), 0.35f);
            var curl = LiveGripProcessor.CloseUntilContact(Finger, gap, Thickness, out var touching);

            Assert.GreaterOrEqual(touching, 1);
            Assert.Less(curl[0], curl[1], "outer joints keep closing after the proximal segment stops");
            AssertNeverPenetrates(curl, gap);
        }

        [Test]
        public void DescentReachesMinimumInsideMuscleRange()
        {
            var values = new[] { 0f, 9f, 0f, 0f };
            // Only the chosen coordinates move, and the optimum of the third lies past +1.
            LiveGripProcessor.Descend(values, new[] { 0, 2, 3 }, () =>
                (values[0] - 0.3f) * (values[0] - 0.3f) + (values[2] - 1.7f) * (values[2] - 1.7f) + (values[3] + 0.6f) * (values[3] + 0.6f));

            Assert.AreEqual(0.3f, values[0], 0.01f);
            Assert.AreEqual(9f, values[1], "untouched coordinate");
            Assert.AreEqual(1f, values[2], 1e-6f, "clamped to the muscle range");
            Assert.AreEqual(-0.6f, values[3], 0.01f);
        }

        [Test]
        public void BakedMusclesUseClipAttributeNames()
        {
            // The names VRChat's own hand clips use; a wrong one bakes a pose that never plays.
            Assert.AreEqual("LeftHand.Index.1 Stretched", LiveGripProcessor.MuscleAttribute("Left Index 1 Stretched"));
            Assert.AreEqual("RightHand.Thumb.Spread", LiveGripProcessor.MuscleAttribute("Right Thumb Spread"));
            var fingerMuscles = HumanTrait.MuscleName.Where(m => m.EndsWith("Stretched") || m.EndsWith("Spread"))
                .Where(m => LiveGripProcessor.FingerNames.Any(f => m.Contains(" " + f + " "))).ToList();
            Assert.AreEqual(40, fingerMuscles.Count);
            foreach (var muscle in fingerMuscles)
                StringAssert.IsMatch(@"^(Left|Right)Hand\.(Thumb|Index|Middle|Ring|Little)\.(\d Stretched|Spread)$", LiveGripProcessor.MuscleAttribute(muscle));
        }

        [Test]
        public void UnreachableObjectClosesFully()
        {
            var gap = Sphere(new Vector3(10f, 0f, 0f), 1f);
            var curl = LiveGripProcessor.CloseUntilContact(Finger, gap, Thickness, out var touching);

            Assert.AreEqual(-1, touching);
            Assert.IsTrue(curl.All(c => Mathf.Approximately(c, 1f)));
        }
    }
}
