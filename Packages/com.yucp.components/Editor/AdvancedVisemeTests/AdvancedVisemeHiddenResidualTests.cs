using System;
using NUnit.Framework;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeHiddenResidualTests
    {
        [Test]
        public void HiddenPhoneResidualIsOrthogonalToEveryDrivenBasisAxis()
        {
            using (var fixture = CreateComplementFixture())
            {
                Assert.That(fixture.result.success, Is.True, fixture.result.error);
                Assert.That(fixture.result.hiddenPhoneResidualBlendShapeName, Is.Not.Null.And.Not.Empty);
                Assert.That(fixture.source.blendShapeCount, Is.EqualTo(fixture.sourceBlendShapeCount));
                Assert.That(fixture.source.GetBlendShapeIndex(
                    fixture.result.hiddenPhoneResidualBlendShapeName), Is.EqualTo(-1));

                var hidden = ReadDelta(
                    fixture.result.mesh,
                    fixture.result.mesh.GetBlendShapeIndex(
                        fixture.result.hiddenPhoneResidualBlendShapeName));
                Assert.That(Mathf.Abs(Dot(hidden.vertices, fixture.basisA.vertices)), Is.LessThan(1e-6f));
                Assert.That(Mathf.Abs(Dot(hidden.vertices, fixture.basisB.vertices)), Is.LessThan(1e-6f));

                var expected = Subtract(fixture.ppHidden, fixture.nnHidden);
                AssertDeltasEqual(hidden, expected, 1e-5f);
            }
        }

        [Test]
        public void SignedTransferSwapsOnlyTheUnsupportedPhoneGeometry()
        {
            using (var fixture = CreateComplementFixture())
            {
                Assert.That(fixture.result.success, Is.True, fixture.result.error);
                var hidden = ReadDelta(
                    fixture.result.mesh,
                    fixture.result.mesh.GetBlendShapeIndex(
                        fixture.result.hiddenPhoneResidualBlendShapeName));
                var pp = ReadDelta(fixture.source, fixture.visemes[1]);
                var nn = ReadDelta(fixture.source, fixture.visemes[8]);

                var nnToPp = Add(nn, hidden);
                var expectedNnToPp = Add(
                    Scale(fixture.basisA, fixture.nnBasisA),
                    Scale(fixture.basisB, fixture.nnBasisB),
                    fixture.ppHidden);
                AssertDeltasEqual(nnToPp, expectedNnToPp, 1e-5f);

                var ppToNn = Subtract(pp, hidden);
                var expectedPpToNn = Add(
                    Scale(fixture.basisA, fixture.ppBasisA),
                    Scale(fixture.basisB, fixture.ppBasisB),
                    fixture.nnHidden);
                AssertDeltasEqual(ppToNn, expectedPpToNn, 1e-5f);

                for (var step = -10; step <= 10; step++)
                {
                    var correction = Scale(hidden, step / 10f);
                    Assert.That(Mathf.Abs(Dot(correction.vertices, fixture.basisA.vertices)),
                        Is.LessThan(1e-6f));
                    Assert.That(Mathf.Abs(Dot(correction.vertices, fixture.basisB.vertices)),
                        Is.LessThan(1e-6f));
                }
            }
        }

        [Test]
        public void CollinearAndZeroBasisAxesProduceADeterministicNegligibleSkip()
        {
            var source = CreateRankDeficientMesh();
            var sourceBlendShapeCount = source.blendShapeCount;
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                var visemes = VisemeIndices(source);
                var basis = new[]
                {
                    new AdvancedVisemeMeshCalibrator.BasisInput(
                        AdvancedVisemeArticulator.JawOpen,
                        source.GetBlendShapeIndex("BasisA")),
                    new AdvancedVisemeMeshCalibrator.BasisInput(
                        AdvancedVisemeArticulator.MouthOpen,
                        source.GetBlendShapeIndex("BasisCollinear")),
                    new AdvancedVisemeMeshCalibrator.BasisInput(
                        AdvancedVisemeArticulator.LipClose,
                        source.GetBlendShapeIndex("BasisZero"))
                };

                result = AdvancedVisemeMeshCalibrator.Build(source, visemes, basis);
                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.hiddenPhoneResidualBlendShapeName, Is.Null);
                Assert.That(result.mesh.GetBlendShapeIndex("YUCP_AVR_Hidden_PP_Minus_nn"), Is.EqualTo(-1));
                Assert.That(source.blendShapeCount, Is.EqualTo(sourceBlendShapeCount));
            }
            finally
            {
                if (result != null && result.mesh != null)
                    UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static CalibrationFixture CreateComplementFixture()
        {
            var source = NewTriangleMesh("Hidden residual complement");
            var zero = Zero(source.vertexCount);
            var basisA = new Deltas(
                new[] { new Vector3(0.20f, 0f, 0f), new Vector3(0.04f, 0f, 0f), Vector3.zero },
                new[] { new Vector3(0.02f, 0f, 0f), new Vector3(0.004f, 0f, 0f), Vector3.zero },
                new[] { new Vector3(0.03f, 0f, 0f), new Vector3(0.006f, 0f, 0f), Vector3.zero });
            var basisB = new Deltas(
                new[] { Vector3.zero, new Vector3(0f, 0.25f, 0f), new Vector3(0f, 0.05f, 0f) },
                new[] { Vector3.zero, new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0.006f, 0f) },
                new[] { Vector3.zero, new Vector3(0f, 0.04f, 0f), new Vector3(0f, 0.008f, 0f) });
            var ppHidden = new Deltas(
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.18f) },
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.07f) },
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.05f) });
            var nnHidden = new Deltas(
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -0.12f) },
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -0.04f) },
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -0.03f) });

            AddFrame(source, "BasisA", basisA);
            AddFrame(source, "BasisB", basisB);
            const float ppBasisA = 0.90f;
            const float ppBasisB = 0.20f;
            const float nnBasisA = 0.30f;
            const float nnBasisB = 0.80f;
            for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            {
                var hidden = new Deltas(zero, zero, zero);
                var coefficientA = 0.10f + viseme * 0.015f;
                var coefficientB = 0.25f + viseme * 0.01f;
                if (viseme == 1)
                {
                    coefficientA = ppBasisA;
                    coefficientB = ppBasisB;
                    hidden = ppHidden;
                }
                else if (viseme == 8)
                {
                    coefficientA = nnBasisA;
                    coefficientB = nnBasisB;
                    hidden = nnHidden;
                }
                AddFrame(source,
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme],
                    Add(Scale(basisA, coefficientA), Scale(basisB, coefficientB), hidden));
            }

            var sourceBlendShapeCount = source.blendShapeCount;
            var visemes = VisemeIndices(source);
            var calibrationBasis = new[]
            {
                new AdvancedVisemeMeshCalibrator.BasisInput(
                    AdvancedVisemeArticulator.JawOpen, source.GetBlendShapeIndex("BasisA")),
                new AdvancedVisemeMeshCalibrator.BasisInput(
                    AdvancedVisemeArticulator.LipClose, source.GetBlendShapeIndex("BasisB"))
            };
            return new CalibrationFixture
            {
                source = source,
                result = AdvancedVisemeMeshCalibrator.Build(source, visemes, calibrationBasis),
                sourceBlendShapeCount = sourceBlendShapeCount,
                visemes = visemes,
                basisA = basisA,
                basisB = basisB,
                ppHidden = ppHidden,
                nnHidden = nnHidden,
                ppBasisA = ppBasisA,
                ppBasisB = ppBasisB,
                nnBasisA = nnBasisA,
                nnBasisB = nnBasisB
            };
        }

        private static Mesh CreateRankDeficientMesh()
        {
            var mesh = NewTriangleMesh("Hidden residual rank deficient");
            var zero = Zero(mesh.vertexCount);
            var basisA = new Deltas(
                new[] { new Vector3(0.1f, 0f, 0f), new Vector3(0.02f, 0f, 0f), Vector3.zero },
                new[] { new Vector3(0.01f, 0f, 0f), new Vector3(0.002f, 0f, 0f), Vector3.zero },
                new[] { new Vector3(0.015f, 0f, 0f), new Vector3(0.003f, 0f, 0f), Vector3.zero });
            AddFrame(mesh, "BasisA", basisA);
            AddFrame(mesh, "BasisCollinear", Scale(basisA, 2f));
            AddFrame(mesh, "BasisZero", new Deltas(zero, zero, zero));
            for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            {
                var coefficient = viseme == 1 ? 0.8f : viseme == 8 ? 0.2f : 0.1f + viseme * 0.02f;
                AddFrame(mesh,
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme],
                    Scale(basisA, coefficient));
            }
            return mesh;
        }

        private static Mesh NewTriangleMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }

        private static int[] VisemeIndices(Mesh mesh)
        {
            var indices = new int[VisemeReconstructionProfile.VisemeCount];
            for (var viseme = 0; viseme < indices.Length; viseme++)
                indices[viseme] = mesh.GetBlendShapeIndex(
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme]);
            return indices;
        }

        private static void AddFrame(Mesh mesh, string name, Deltas value)
        {
            mesh.AddBlendShapeFrame(name, 100f, value.vertices, value.normals, value.tangents);
        }

        private static Deltas ReadDelta(Mesh mesh, int blendShapeIndex)
        {
            Assert.That(blendShapeIndex, Is.GreaterThanOrEqualTo(0));
            var vertices = Zero(mesh.vertexCount);
            var normals = Zero(mesh.vertexCount);
            var tangents = Zero(mesh.vertexCount);
            var frame = mesh.GetBlendShapeFrameCount(blendShapeIndex) - 1;
            mesh.GetBlendShapeFrameVertices(blendShapeIndex, frame, vertices, normals, tangents);
            var scale = 100f / mesh.GetBlendShapeFrameWeight(blendShapeIndex, frame);
            return new Deltas(
                Scale(vertices, scale), Scale(normals, scale), Scale(tangents, scale));
        }

        private static Deltas Add(Deltas left, Deltas right, Deltas third)
        {
            return Add(Add(left, right), third);
        }

        private static Deltas Add(Deltas left, Deltas right)
        {
            return new Deltas(
                Add(left.vertices, right.vertices),
                Add(left.normals, right.normals),
                Add(left.tangents, right.tangents));
        }

        private static Deltas Subtract(Deltas left, Deltas right)
        {
            return new Deltas(
                Subtract(left.vertices, right.vertices),
                Subtract(left.normals, right.normals),
                Subtract(left.tangents, right.tangents));
        }

        private static Deltas Scale(Deltas value, float scalar)
        {
            return new Deltas(
                Scale(value.vertices, scalar),
                Scale(value.normals, scalar),
                Scale(value.tangents, scalar));
        }

        private static Vector3[] Add(Vector3[] left, Vector3[] right)
        {
            var output = new Vector3[left.Length];
            for (var i = 0; i < output.Length; i++) output[i] = left[i] + right[i];
            return output;
        }

        private static Vector3[] Subtract(Vector3[] left, Vector3[] right)
        {
            var output = new Vector3[left.Length];
            for (var i = 0; i < output.Length; i++) output[i] = left[i] - right[i];
            return output;
        }

        private static Vector3[] Scale(Vector3[] values, float scalar)
        {
            var output = new Vector3[values.Length];
            for (var i = 0; i < output.Length; i++) output[i] = values[i] * scalar;
            return output;
        }

        private static Vector3[] Zero(int count)
        {
            return new Vector3[count];
        }

        private static float Dot(Vector3[] left, Vector3[] right)
        {
            var value = 0f;
            for (var i = 0; i < left.Length; i++) value += Vector3.Dot(left[i], right[i]);
            return value;
        }

        private static void AssertDeltasEqual(Deltas actual, Deltas expected, float tolerance)
        {
            for (var vertex = 0; vertex < actual.vertices.Length; vertex++)
            {
                Assert.That(Vector3.Distance(actual.vertices[vertex], expected.vertices[vertex]),
                    Is.LessThan(tolerance), "vertex " + vertex);
                Assert.That(Vector3.Distance(actual.normals[vertex], expected.normals[vertex]),
                    Is.LessThan(tolerance), "normal " + vertex);
                Assert.That(Vector3.Distance(actual.tangents[vertex], expected.tangents[vertex]),
                    Is.LessThan(tolerance), "tangent " + vertex);
            }
        }

        private readonly struct Deltas
        {
            public readonly Vector3[] vertices;
            public readonly Vector3[] normals;
            public readonly Vector3[] tangents;

            public Deltas(Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
            {
                this.vertices = vertices;
                this.normals = normals;
                this.tangents = tangents;
            }
        }

        private sealed class CalibrationFixture : IDisposable
        {
            public Mesh source;
            public AdvancedVisemeMeshCalibrator.Result result;
            public int sourceBlendShapeCount;
            public int[] visemes;
            public Deltas basisA;
            public Deltas basisB;
            public Deltas ppHidden;
            public Deltas nnHidden;
            public float ppBasisA;
            public float ppBasisB;
            public float nnBasisA;
            public float nnBasisB;

            public void Dispose()
            {
                if (result != null && result.mesh != null)
                    UnityEngine.Object.DestroyImmediate(result.mesh);
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }
}
