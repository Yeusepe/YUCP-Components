#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeLinkedCalibrationTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void CompositeTargetCalibrationReconstructsRandomizedBlendsAndPreservesSource()
        {
            var fixture = CreateCompositeFixture();
            var snapshot = Snapshot(fixture.mesh);
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                result = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                    fixture.mesh,
                    fixture.visemePoses,
                    fixture.basisPoses,
                    fixture.referenceCoefficients,
                    "Coat / Secondary");

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.mesh, Is.Not.SameAs(fixture.mesh));
                Assert.That(result.generatedNamePrefix,
                    Is.EqualTo("YUCP_AVR_Linked_Coat_Secondary"));
                Assert.That(result.coefficients, Is.Not.SameAs(fixture.referenceCoefficients));

                for (var viseme = 0;
                     viseme < VisemeReconstructionProfile.VisemeCount;
                     viseme++)
                {
                    Assert.That(result.residualBlendShapeNames[viseme],
                        Does.StartWith(result.generatedNamePrefix + "_Residual_"));
                    var residual = ReadShape(
                        result.mesh,
                        result.mesh.GetBlendShapeIndex(
                            result.residualBlendShapeNames[viseme]));
                    var reconstructed = residual;
                    for (var basis = 0; basis < fixture.basisDeltas.Length; basis++)
                    {
                        reconstructed = Add(
                            reconstructed,
                            Scale(
                                fixture.basisDeltas[basis],
                                fixture.referenceCoefficients[viseme, basis]));
                        Assert.That(
                            result.independentlyFittedCoefficients[viseme, basis],
                            Is.EqualTo(fixture.referenceCoefficients[viseme, basis])
                                .Within(2e-4f),
                            $"target-only coefficient [{viseme},{basis}]");
                    }
                    AssertDelta(
                        fixture.visemeDeltas[viseme],
                        reconstructed,
                        $"viseme {VisemeReconstructionProfile.VisemeNames[viseme]}");
                }

                var random = new System.Random(0x61A7E);
                for (var sample = 0; sample < 96; sample++)
                {
                    var weights = RandomSimplex(
                        random, VisemeReconstructionProfile.VisemeCount);
                    var authored = Delta.Zero(fixture.mesh.vertexCount);
                    var reconstructed = Delta.Zero(fixture.mesh.vertexCount);
                    for (var viseme = 0; viseme < weights.Length; viseme++)
                    {
                        AddScaled(authored, fixture.visemeDeltas[viseme], weights[viseme]);
                        var column = ReadShape(
                            result.mesh,
                            result.mesh.GetBlendShapeIndex(
                                result.residualBlendShapeNames[viseme]));
                        for (var basis = 0; basis < fixture.basisDeltas.Length; basis++)
                        {
                            AddScaled(
                                column,
                                fixture.basisDeltas[basis],
                                fixture.referenceCoefficients[viseme, basis]);
                        }
                        AddScaled(reconstructed, column, weights[viseme]);
                    }
                    AssertDelta(authored, reconstructed, "random blend " + sample);
                }

                var retainedCoefficient = result.coefficients[4, 1];
                fixture.referenceCoefficients[4, 1] += 20f;
                Assert.That(result.coefficients[4, 1], Is.EqualTo(retainedCoefficient),
                    "The linked result must own an immutable snapshot of primary coefficients.");
                AssertSourceUnchanged(fixture.mesh, snapshot);
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(fixture.mesh);
            }
        }

        [Test]
        public void MissingTargetVisemesAndBasisPoseRemainColumnAligned()
        {
            var mesh = CreateMesh("Missing Linked Mappings", 5);
            var basisDelta = Delta.Single(
                mesh.vertexCount, 0,
                new Vector3(0.12f, 0.01f, 0f),
                new Vector3(0.02f, 0.003f, 0f),
                new Vector3(0.01f, 0.004f, 0f));
            var basisIndex = AddShape(mesh, "MappedJaw", basisDelta);
            var target = Add(
                Scale(basisDelta, 0.35f),
                Delta.Single(
                    mesh.vertexCount, 3,
                    new Vector3(0.01f, 0.08f, 0.02f),
                    new Vector3(0.002f, 0.01f, 0.003f),
                    new Vector3(0.004f, 0.002f, 0.009f)));
            var targetIndex = AddShape(mesh, "OnlyPP", target);
            var visemes = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                VisemeReconstructionProfile.VisemeCount];
            visemes[1] = Pose((targetIndex, 100f));
            var basis = new[]
            {
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.JawOpen, 1, Pose((basisIndex, 100f))),
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.LipClose, 1, default)
            };
            var reference = new float[VisemeReconstructionProfile.VisemeCount, 2];
            reference[1, 0] = 0.6f;
            reference[1, 1] = 0.91f;
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                result = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                    mesh, visemes, basis, reference, "Partial");

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.residualBlendShapeNames[1], Is.Not.Null.And.Not.Empty);
                for (var viseme = 0;
                     viseme < VisemeReconstructionProfile.VisemeCount;
                     viseme++)
                {
                    if (viseme == 1) continue;
                    Assert.That(result.residualBlendShapeNames[viseme], Is.Null);
                    for (var column = 0;
                         column < result.ownershipProjectionCoefficients.GetLength(1);
                         column++)
                        Assert.That(
                            result.ownershipProjectionCoefficients[viseme, column],
                            Is.Zero);
                }
                Assert.That(result.hiddenPhoneResidualBlendShapeName, Is.Null,
                    "A hidden PP-minus-nn residual requires both mapped phones.");
                Assert.That(result.coefficients[1, 0], Is.EqualTo(0.6f));
                Assert.That(result.coefficients[1, 1], Is.EqualTo(0.91f));
                Assert.That(result.independentlyFittedCoefficients[1, 0],
                    Is.EqualTo(0.35f).Within(1e-5f),
                    "Target-only diagnostics must not replace the primary production coefficient.");
                Assert.That(result.independentlyFittedCoefficients[1, 1], Is.Zero,
                    "A missing target axis must remain a zero geometry column.");

                var residual = ReadShape(
                    result.mesh,
                    result.mesh.GetBlendShapeIndex(result.residualBlendShapeNames[1]));
                AssertDelta(target, Add(residual, Scale(basisDelta, 0.6f)),
                    "mapped phone with missing secondary basis");
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ArticulationOnlySignedBasisRetainsTargetLocalInverse()
        {
            var mesh = CreateMesh("Signed Articulation-Only Link", 5);
            var signedDelta = Delta.Single(
                mesh.vertexCount, 2,
                new Vector3(0.09f, -0.03f, 0.02f),
                new Vector3(0.012f, -0.004f, 0.003f),
                new Vector3(0.008f, -0.002f, 0.005f));
            var signedIndex = AddShape(mesh, "SmileSad", signedDelta);
            var mappedPose = Pose((signedIndex, 100f));
            var signedBasis = new[]
            {
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.SmileSad, 1, mappedPose)
            };
            var unsignedBasis = new[]
            {
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.JawOpen, 1, mappedPose)
            };
            var visemes = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                VisemeReconstructionProfile.VisemeCount];
            var reference = new float[VisemeReconstructionProfile.VisemeCount, 1];
            var snapshot = Snapshot(mesh);
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                Assert.That(
                    AdvancedVisemeReconstructorProcessor.RequiresLinkedRendererCalibration(
                        false, signedBasis),
                    Is.True,
                    "A mapped signed axis needs a target-local -U shape even without visemes.");
                Assert.That(
                    AdvancedVisemeReconstructorProcessor.RequiresLinkedRendererCalibration(
                        false, unsignedBasis),
                    Is.False,
                    "An unsigned articulation-only link should remain native to VRCFury.");

                result = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                    mesh, visemes, signedBasis, reference, "SignedOnly");

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.residualBlendShapeNames.All(string.IsNullOrEmpty), Is.True,
                    "An articulation-only target must not fabricate viseme residuals.");
                Assert.That(result.basisNegativeBlendShapeNames[0], Is.Not.Null.And.Not.Empty);
                AssertDelta(
                    Scale(signedDelta, -1f),
                    ReadShape(
                        result.mesh,
                        result.mesh.GetBlendShapeIndex(
                            result.basisNegativeBlendShapeNames[0])),
                    "signed articulation-only inverse");
                AssertSourceUnchanged(mesh, snapshot);
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void GeneratedNamespaceIsDeterministicAndAvoidsExistingBlendShapes()
        {
            var mesh = CreateMesh("Linked Name Collision", 4);
            var pp = Delta.Single(
                mesh.vertexCount, 1,
                new Vector3(0.05f, 0.02f, 0f),
                new Vector3(0.01f, 0.003f, 0f),
                new Vector3(0.004f, 0.002f, 0f));
            var nn = Delta.Single(
                mesh.vertexCount, 2,
                new Vector3(0.01f, 0.07f, 0f),
                new Vector3(0.002f, 0.012f, 0f),
                new Vector3(0.001f, 0.006f, 0f));
            var ppIndex = AddShape(mesh, "PP", pp);
            var nnIndex = AddShape(mesh, "nn", nn);
            AddShape(mesh, "YUCP_AVR_Linked_Cape_Residual_PP", Delta.Zero(mesh.vertexCount));
            var snapshot = Snapshot(mesh);
            var visemes = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                VisemeReconstructionProfile.VisemeCount];
            visemes[1] = Pose((ppIndex, 100f));
            visemes[8] = Pose((nnIndex, 100f));
            var coefficients = new float[VisemeReconstructionProfile.VisemeCount, 0];
            AdvancedVisemeMeshCalibrator.Result first = null;
            AdvancedVisemeMeshCalibrator.Result second = null;
            try
            {
                first = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                    mesh,
                    visemes,
                    Array.Empty<AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput>(),
                    coefficients,
                    "Cape");
                second = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                    mesh,
                    visemes,
                    Array.Empty<AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput>(),
                    coefficients,
                    "Cape");

                Assert.That(first.success, Is.True, first.error);
                Assert.That(second.success, Is.True, second.error);
                Assert.That(first.generatedNamePrefix,
                    Is.EqualTo("YUCP_AVR_Linked_Cape_2"));
                Assert.That(second.generatedNamePrefix,
                    Is.EqualTo(first.generatedNamePrefix));
                CollectionAssert.AreEqual(
                    first.residualBlendShapeNames,
                    second.residualBlendShapeNames);
                Assert.That(first.hiddenPhoneResidualBlendShapeName,
                    Is.EqualTo("YUCP_AVR_Linked_Cape_2_Hidden_PP_Minus_nn"));

                var generated = first.residualBlendShapeNames
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Concat(new[] { first.hiddenPhoneResidualBlendShapeName })
                    .ToArray();
                Assert.That(generated, Is.Unique);
                Assert.That(generated.All(name =>
                    name.StartsWith(first.generatedNamePrefix + "_", StringComparison.Ordinal)),
                    Is.True);
                AssertSourceUnchanged(mesh, snapshot);
            }
            finally
            {
                if (first?.mesh != null) UnityEngine.Object.DestroyImmediate(first.mesh);
                if (second?.mesh != null) UnityEngine.Object.DestroyImmediate(second.mesh);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void InvalidMappedShapeFailsWithoutModifyingTarget()
        {
            var mesh = CreateMesh("Invalid Linked Mapping", 3);
            var snapshot = Snapshot(mesh);
            var visemes = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                VisemeReconstructionProfile.VisemeCount];
            visemes[4] = Pose((27, 100f));

            var result = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                mesh,
                visemes,
                Array.Empty<AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput>(),
                new float[VisemeReconstructionProfile.VisemeCount, 0],
                "Invalid");

            Assert.That(result.success, Is.False);
            Assert.That(result.mesh, Is.Null);
            Assert.That(result.error, Does.Contain("index 27"));
            AssertSourceUnchanged(mesh, snapshot);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void NonlinearMappedTargetFailsWithoutModifyingSource()
        {
            var mesh = CreateMesh("Nonlinear Linked Mapping", 4);
            var first = Delta.Single(
                mesh.vertexCount, 0,
                new Vector3(0.03f, 0f, 0f), Vector3.zero, Vector3.zero);
            var second = Delta.Single(
                mesh.vertexCount, 1,
                new Vector3(0f, 0.09f, 0f), Vector3.zero, Vector3.zero);
            mesh.AddBlendShapeFrame(
                "NonlinearPP", 50f,
                first.vertices, first.normals, first.tangents);
            mesh.AddBlendShapeFrame(
                "NonlinearPP", 100f,
                second.vertices, second.normals, second.tangents);
            var snapshot = Snapshot(mesh);
            var visemes = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                VisemeReconstructionProfile.VisemeCount];
            visemes[1] = Pose((mesh.GetBlendShapeIndex("NonlinearPP"), 100f));

            var result = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                mesh,
                visemes,
                Array.Empty<AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput>(),
                new float[VisemeReconstructionProfile.VisemeCount, 0],
                "Nonlinear");

            Assert.That(result.success, Is.False);
            Assert.That(result.mesh, Is.Null);
            Assert.That(result.error, Does.Contain("nonlinear"));
            AssertSourceUnchanged(mesh, snapshot);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        private static CompositeFixture CreateCompositeFixture()
        {
            var mesh = CreateMesh("Composite Linked Target", 8);
            var basisDeltas = new[]
            {
                Delta.Single(
                    mesh.vertexCount, 0,
                    new Vector3(0.17f, 0.01f, 0.02f),
                    new Vector3(0.025f, 0.002f, 0.004f),
                    new Vector3(0.014f, 0.005f, 0.003f)),
                Delta.Single(
                    mesh.vertexCount, 1,
                    new Vector3(0.01f, 0.14f, 0.03f),
                    new Vector3(0.003f, 0.021f, 0.005f),
                    new Vector3(0.004f, 0.011f, 0.006f))
            };
            var basisPoses = new[]
            {
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.JawOpen,
                    1,
                    AddCompositePose(mesh, "JawComposite", basisDeltas[0], 80f, 45f)),
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.LipPucker,
                    1,
                    AddCompositePose(mesh, "PuckerComposite", basisDeltas[1], 65f, 55f))
            };
            var coefficients = new float[VisemeReconstructionProfile.VisemeCount, 2];
            var visemeDeltas = new Delta[VisemeReconstructionProfile.VisemeCount];
            var visemePoses = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                VisemeReconstructionProfile.VisemeCount];
            for (var viseme = 0; viseme < visemeDeltas.Length; viseme++)
            {
                coefficients[viseme, 0] = 0.08f + 0.027f * viseme;
                coefficients[viseme, 1] = 0.34f - 0.011f * viseme;
                var detailVertex = 2 + viseme % 6;
                var detail = Delta.Single(
                    mesh.vertexCount,
                    detailVertex,
                    new Vector3(
                        0.0011f * (viseme + 1),
                        -0.0007f * (viseme + 1),
                        0.0009f * (viseme + 1)),
                    new Vector3(
                        -0.0002f * (viseme + 1),
                        0.0004f * (viseme + 1),
                        0.0003f * (viseme + 1)),
                    new Vector3(
                        0.0003f * (viseme + 1),
                        0.0002f * (viseme + 1),
                        -0.0001f * (viseme + 1)));
                visemeDeltas[viseme] = Add(
                    Scale(basisDeltas[0], coefficients[viseme, 0]),
                    Scale(basisDeltas[1], coefficients[viseme, 1]),
                    detail);
                visemePoses[viseme] = AddCompositePose(
                    mesh,
                    "Phone_" + VisemeReconstructionProfile.VisemeNames[viseme],
                    visemeDeltas[viseme],
                    75f,
                    40f);
            }

            return new CompositeFixture
            {
                mesh = mesh,
                basisDeltas = basisDeltas,
                basisPoses = basisPoses,
                visemeDeltas = visemeDeltas,
                visemePoses = visemePoses,
                referenceCoefficients = coefficients
            };
        }

        private static AdvancedVisemeMeshCalibrator.BlendShapePoseInput AddCompositePose(
            Mesh mesh,
            string name,
            Delta target,
            float firstWeight,
            float secondWeight)
        {
            const float firstShare = 0.63f;
            const float secondShare = 1f - firstShare;
            var first = AddShape(
                mesh,
                name + "_A",
                Scale(target, firstShare / (firstWeight / 100f)));
            var second = AddShape(
                mesh,
                name + "_B",
                Scale(target, secondShare / (secondWeight / 100f)));
            return Pose((first, firstWeight), (second, secondWeight));
        }

        private static AdvancedVisemeMeshCalibrator.BlendShapePoseInput Pose(
            params (int index, float weight)[] elements)
        {
            return new AdvancedVisemeMeshCalibrator.BlendShapePoseInput(
                elements.Select(element =>
                    new AdvancedVisemeMeshCalibrator.BlendShapePoseElement(
                        element.index, element.weight)).ToArray());
        }

        private static Mesh CreateMesh(string name, int vertexCount)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = Enumerable.Range(0, vertexCount)
                .Select(index => new Vector3(index * 0.01f, 0f, 0f)).ToArray();
            mesh.normals = Enumerable.Repeat(Vector3.forward, vertexCount).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1f, 0f, 0f, 1f), vertexCount).ToArray();
            var triangles = new List<int>();
            for (var index = 2; index < vertexCount; index++)
            {
                triangles.Add(0);
                triangles.Add(index - 1);
                triangles.Add(index);
            }
            mesh.triangles = triangles.ToArray();
            return mesh;
        }

        private static int AddShape(Mesh mesh, string name, Delta delta)
        {
            mesh.AddBlendShapeFrame(
                name, 100f, delta.vertices, delta.normals, delta.tangents);
            return mesh.GetBlendShapeIndex(name);
        }

        private static Delta ReadShape(Mesh mesh, int index)
        {
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            var vertices = new Vector3[mesh.vertexCount];
            var normals = new Vector3[mesh.vertexCount];
            var tangents = new Vector3[mesh.vertexCount];
            var frame = mesh.GetBlendShapeFrameCount(index) - 1;
            mesh.GetBlendShapeFrameVertices(
                index, frame, vertices, normals, tangents);
            var scale = 100f / mesh.GetBlendShapeFrameWeight(index, frame);
            return new Delta(
                Scale(vertices, scale),
                Scale(normals, scale),
                Scale(tangents, scale));
        }

        private static MeshSnapshot Snapshot(Mesh mesh)
        {
            var shapes = new List<ShapeSnapshot>();
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
            {
                var vertices = new Vector3[mesh.vertexCount];
                var normals = new Vector3[mesh.vertexCount];
                var tangents = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(
                    shape, frame, vertices, normals, tangents);
                shapes.Add(new ShapeSnapshot
                {
                    name = mesh.GetBlendShapeName(shape),
                    frameWeight = mesh.GetBlendShapeFrameWeight(shape, frame),
                    delta = new Delta(vertices, normals, tangents)
                });
            }
            return new MeshSnapshot
            {
                vertices = mesh.vertices,
                normals = mesh.normals,
                tangents = mesh.tangents,
                shapes = shapes.ToArray()
            };
        }

        private static void AssertSourceUnchanged(Mesh mesh, MeshSnapshot snapshot)
        {
            CollectionAssert.AreEqual(snapshot.vertices, mesh.vertices);
            CollectionAssert.AreEqual(snapshot.normals, mesh.normals);
            CollectionAssert.AreEqual(snapshot.tangents, mesh.tangents);
            var expectedShapes = snapshot.shapes
                .GroupBy(frame => frame.name, StringComparer.Ordinal)
                .ToArray();
            Assert.That(mesh.blendShapeCount, Is.EqualTo(expectedShapes.Length));
            for (var shape = 0; shape < expectedShapes.Length; shape++)
            {
                var expectedFrames = expectedShapes[shape].ToArray();
                Assert.That(mesh.GetBlendShapeName(shape),
                    Is.EqualTo(expectedShapes[shape].Key));
                Assert.That(mesh.GetBlendShapeFrameCount(shape),
                    Is.EqualTo(expectedFrames.Length));
                for (var frame = 0; frame < expectedFrames.Length; frame++)
                {
                    Assert.That(mesh.GetBlendShapeFrameWeight(shape, frame),
                        Is.EqualTo(expectedFrames[frame].frameWeight));
                    var vertices = new Vector3[mesh.vertexCount];
                    var normals = new Vector3[mesh.vertexCount];
                    var tangents = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(
                        shape, frame, vertices, normals, tangents);
                    AssertDelta(
                        expectedFrames[frame].delta,
                        new Delta(vertices, normals, tangents),
                        $"source shape {expectedShapes[shape].Key} frame {frame}");
                }
            }
        }

        private static float[] RandomSimplex(System.Random random, int count)
        {
            var values = new float[count];
            var sum = 0f;
            for (var index = 0; index < count; index++)
            {
                values[index] = (float)random.NextDouble() + 0.001f;
                sum += values[index];
            }
            for (var index = 0; index < count; index++) values[index] /= sum;
            return values;
        }

        private static Delta Add(params Delta[] values)
        {
            var output = Delta.Zero(values[0].vertices.Length);
            foreach (var value in values) AddScaled(output, value, 1f);
            return output;
        }

        private static void AddScaled(Delta target, Delta value, float scale)
        {
            for (var vertex = 0; vertex < target.vertices.Length; vertex++)
            {
                target.vertices[vertex] += value.vertices[vertex] * scale;
                target.normals[vertex] += value.normals[vertex] * scale;
                target.tangents[vertex] += value.tangents[vertex] * scale;
            }
        }

        private static Delta Scale(Delta value, float scale)
        {
            return new Delta(
                Scale(value.vertices, scale),
                Scale(value.normals, scale),
                Scale(value.tangents, scale));
        }

        private static Vector3[] Scale(Vector3[] values, float scale)
        {
            return values.Select(value => value * scale).ToArray();
        }

        private static void AssertDelta(Delta expected, Delta actual, string context)
        {
            for (var vertex = 0; vertex < expected.vertices.Length; vertex++)
            {
                Assert.That(Vector3.Distance(expected.vertices[vertex], actual.vertices[vertex]),
                    Is.LessThan(Tolerance), $"{context}, vertex {vertex}");
                Assert.That(Vector3.Distance(expected.normals[vertex], actual.normals[vertex]),
                    Is.LessThan(Tolerance), $"{context}, normal {vertex}");
                Assert.That(Vector3.Distance(expected.tangents[vertex], actual.tangents[vertex]),
                    Is.LessThan(Tolerance), $"{context}, tangent {vertex}");
            }
        }

        private sealed class CompositeFixture
        {
            public Mesh mesh;
            public Delta[] basisDeltas;
            public AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput[] basisPoses;
            public Delta[] visemeDeltas;
            public AdvancedVisemeMeshCalibrator.BlendShapePoseInput[] visemePoses;
            public float[,] referenceCoefficients;
        }

        private sealed class MeshSnapshot
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public ShapeSnapshot[] shapes;
        }

        private sealed class ShapeSnapshot
        {
            public string name;
            public float frameWeight;
            public Delta delta;
        }

        private sealed class Delta
        {
            public readonly Vector3[] vertices;
            public readonly Vector3[] normals;
            public readonly Vector3[] tangents;

            public Delta(Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
            {
                this.vertices = vertices;
                this.normals = normals;
                this.tangents = tangents;
            }

            public static Delta Zero(int vertexCount)
            {
                return new Delta(
                    new Vector3[vertexCount],
                    new Vector3[vertexCount],
                    new Vector3[vertexCount]);
            }

            public static Delta Single(
                int vertexCount,
                int vertex,
                Vector3 position,
                Vector3 normal,
                Vector3 tangent)
            {
                var output = Zero(vertexCount);
                output.vertices[vertex] = position;
                output.normals[vertex] = normal;
                output.tangents[vertex] = tangent;
                return output;
            }
        }
    }
}
#endif
