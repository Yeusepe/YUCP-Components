#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeObservableProjectionTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void ConflictProjectionUsesOnlyObservableNonOrthogonalColumns()
        {
            var fixture = CreatePrimaryFixture();
            var sourceSnapshot = Snapshot(fixture.mesh);
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                result = AdvancedVisemeMeshCalibrator.Build(
                    fixture.mesh,
                    VisemeIndices(fixture.mesh),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.JawOpen,
                            fixture.mesh.GetBlendShapeIndex("BasisVisible")),
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.LipSuck,
                            fixture.mesh.GetBlendShapeIndex("BasisUnobservable"))
                    },
                    new[] { true, false });

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.observableBasisColumns, Is.EqualTo(new[] { true, false }));
                Assert.That(result.basisArticulators, Is.EqualTo(new[]
                {
                    AdvancedVisemeArticulator.JawOpen,
                    AdvancedVisemeArticulator.LipSuck
                }));
                Assert.That(result.basisDirections, Is.EqualTo(new[] { 1, 1 }));

                for (var viseme = 0;
                     viseme < VisemeReconstructionProfile.VisemeCount;
                     viseme++)
                {
                    var authored = Read(
                        fixture.mesh,
                        fixture.mesh.GetBlendShapeIndex(
                            "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme]));
                    var residual = Read(
                        result.mesh,
                        result.mesh.GetBlendShapeIndex(
                            result.residualBlendShapeNames[viseme]));
                    var conflict = ProjectedResidual(
                        result, viseme, fixture.mesh.vertexCount);
                    var retained = Subtract(residual, conflict);

                    // The full authored identity remains exact at g=0: V=UC+R.
                    var atSpeechAuthority = Add(
                        Scale(fixture.visible, result.coefficients[viseme, 0]),
                        Scale(fixture.unobservable, result.coefficients[viseme, 1]),
                        residual);
                    AssertDelta(authored, atSpeechAuthority, "g=0 exact viseme " + viseme);

                    // R is explicitly split for all three mesh delta channels.
                    AssertDelta(residual, Add(retained, conflict), "Rnull + Robs " + viseme);
                    var projectionCoefficient =
                        Dot(residual.vertices, fixture.visible.vertices) /
                        Dot(fixture.visible.vertices, fixture.visible.vertices);
                    AssertDelta(
                        Scale(fixture.visible, projectionCoefficient),
                        conflict,
                        "observable projection " + viseme);

                    // At g=1 the removable residual has no component in the
                    // observable span, while the non-observable nonorthogonal
                    // direction is deliberately not projected away.
                    Assert.That(
                        Mathf.Abs(Dot(retained.vertices, fixture.visible.vertices)),
                        Is.LessThan(2e-6f),
                        "observable projection at g=1 " + viseme);
                    Assert.That(
                        Mathf.Abs(Dot(retained.vertices, fixture.unobservable.vertices)),
                        Is.GreaterThan(1e-6f),
                        "unobservable detail must survive " + viseme);
                }

                AssertSourceUnchanged(fixture.mesh, sourceSnapshot);
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(fixture.mesh);
            }
        }

        [Test]
        public void HiddenPhoneResidualStillUsesTheFullBasisSpan()
        {
            var fixture = CreatePrimaryFixture();
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                result = AdvancedVisemeMeshCalibrator.Build(
                    fixture.mesh,
                    VisemeIndices(fixture.mesh),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.JawOpen,
                            fixture.mesh.GetBlendShapeIndex("BasisVisible")),
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.LipSuck,
                            fixture.mesh.GetBlendShapeIndex("BasisUnobservable"))
                    },
                    new[] { true, false });

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.hiddenPhoneResidualBlendShapeName,
                    Is.Not.Null.And.Not.Empty);
                var hidden = Read(
                    result.mesh,
                    result.mesh.GetBlendShapeIndex(
                        result.hiddenPhoneResidualBlendShapeName));
                Assert.That(Mathf.Abs(Dot(hidden.vertices, fixture.visible.vertices)),
                    Is.LessThan(2e-6f));
                Assert.That(Mathf.Abs(Dot(hidden.vertices, fixture.unobservable.vertices)),
                    Is.LessThan(2e-6f),
                    "The hidden-phone complement must remain orthogonal to even an unobservable basis axis.");
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(fixture.mesh);
            }
        }

        [Test]
        public void CompositePoseOverloadRetainsObservableColumnMetadata()
        {
            var fixture = CreatePrimaryFixture();
            var visibleClip = PoseClip("Visible", "BasisVisible");
            var unobservableClip = PoseClip("Unobservable", "BasisUnobservable");
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                result = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    fixture.mesh,
                    VisemeIndices(fixture.mesh),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.JawOpen, 1, visibleClip, "Face"),
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.MouthX, -1, unobservableClip, "Face")
                    },
                    new[] { true, false });

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.observableBasisColumns, Is.EqualTo(new[] { true, false }));
                Assert.That(result.basisArticulators, Is.EqualTo(new[]
                {
                    AdvancedVisemeArticulator.JawOpen,
                    AdvancedVisemeArticulator.MouthX
                }));
                Assert.That(result.basisDirections, Is.EqualTo(new[] { 1, -1 }));
                var residual = Read(
                    result.mesh,
                    result.mesh.GetBlendShapeIndex(result.residualBlendShapeNames[4]));
                var conflict = ProjectedResidual(
                    result, 4, fixture.mesh.vertexCount);
                Assert.That(
                    Mathf.Abs(Dot(
                        Subtract(residual, conflict).vertices,
                        fixture.visible.vertices)),
                    Is.LessThan(2e-6f));
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(visibleClip);
                UnityEngine.Object.DestroyImmediate(unobservableClip);
                UnityEngine.Object.DestroyImmediate(fixture.mesh);
            }
        }

        [Test]
        public void LinkedCompositeTargetUsesPrimaryCoefficientsAndObservableMask()
        {
            var mesh = NewMesh("Observable linked target");
            var visible = new Delta(
                new[] { new Vector3(0.11f, 0f, 0f), new Vector3(0.03f, 0f, 0f), Vector3.zero, Vector3.zero },
                new[] { new Vector3(0.021f, 0f, 0f), new Vector3(0.006f, 0f, 0f), Vector3.zero, Vector3.zero },
                new[] { new Vector3(0.031f, 0f, 0f), new Vector3(0.008f, 0f, 0f), Vector3.zero, Vector3.zero });
            var detail = new Delta(
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.09f), Vector3.zero },
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.025f), Vector3.zero },
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.034f), Vector3.zero });
            var authored = Add(Scale(visible, 0.1f), detail);
            var basisA = AddShape(mesh, "LinkedBasisA", Scale(visible, 0.4f));
            var basisB = AddShape(mesh, "LinkedBasisB", Scale(visible, 0.6f));
            var visemeA = AddShape(mesh, "LinkedVisemeA", Scale(authored, 0.3f));
            var visemeB = AddShape(mesh, "LinkedVisemeB", Scale(authored, 0.7f));
            var sourceSnapshot = Snapshot(mesh);
            var visemes = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                VisemeReconstructionProfile.VisemeCount];
            visemes[1] = Pose((visemeA, 100f), (visemeB, 100f));
            var basis = new[]
            {
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.JawOpen,
                    1,
                    Pose((basisA, 100f), (basisB, 100f))),
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.TongueY,
                    -1,
                    default)
            };
            var primaryCoefficients = new float[
                VisemeReconstructionProfile.VisemeCount, 2];
            primaryCoefficients[1, 0] = 0.35f;
            primaryCoefficients[1, 1] = 0.82f;
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                result = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                    mesh,
                    visemes,
                    basis,
                    primaryCoefficients,
                    "Observable Coat",
                    new[] { true, false });

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.observableBasisColumns, Is.EqualTo(new[] { true, false }));
                Assert.That(result.basisDirections, Is.EqualTo(new[] { 1, -1 }));
                Assert.That(result.coefficients[1, 0], Is.EqualTo(0.35f));
                Assert.That(result.coefficients[1, 1], Is.EqualTo(0.82f));
                Assert.That(result.independentlyFittedCoefficients[1, 0],
                    Is.EqualTo(0.1f).Within(2e-5f),
                    "The target fit is diagnostic; production must retain the primary coefficient.");

                var residual = Read(
                    result.mesh,
                    result.mesh.GetBlendShapeIndex(result.residualBlendShapeNames[1]));
                var conflict = ProjectedResidual(result, 1, mesh.vertexCount);
                var atSpeechAuthority = Add(Scale(visible, result.coefficients[1, 0]), residual);
                AssertDelta(authored, atSpeechAuthority,
                    "linked g=0 must use primary coefficient alignment");
                Assert.That(
                    Mathf.Abs(Dot(
                        Subtract(residual, conflict).vertices,
                        visible.vertices)),
                    Is.LessThan(2e-6f),
                    "linked g=1 observable projection");
                AssertDelta(
                    residual,
                    Add(Subtract(residual, conflict), conflict),
                    "linked Rnull + Robs");
                AssertSourceUnchanged(mesh, sourceSnapshot);
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void NearCollinearAxesShareAuthorityWithoutCouplingIndependentAxis()
        {
            var mesh = NewMesh("Near-collinear ownership source");
            var jaw = new Delta(
                new[] { new Vector3(0.1f, 0f, 0f), Vector3.zero, Vector3.zero, Vector3.zero },
                new Vector3[4],
                new Vector3[4]);
            var lip = Add(jaw, new Delta(
                new[] { Vector3.zero, new Vector3(0f, 0.000001f, 0f), Vector3.zero, Vector3.zero },
                new Vector3[4],
                new Vector3[4]));
            var tongue = new Delta(
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.08f), Vector3.zero },
                new Vector3[4],
                new Vector3[4]);
            AddShape(mesh, "JawAxis", jaw);
            AddShape(mesh, "LipAxis", lip);
            AddShape(mesh, "TongueAxis", tongue);
            var authored = Add(Scale(jaw, -0.3f), Scale(tongue, -0.2f));
            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
                AddShape(mesh,
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme],
                    authored);

            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                result = AdvancedVisemeMeshCalibrator.Build(
                    mesh,
                    VisemeIndices(mesh),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.JawOpen,
                            mesh.GetBlendShapeIndex("JawAxis")),
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.LipClose,
                            mesh.GetBlendShapeIndex("LipAxis")),
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.TongueOut,
                            mesh.GetBlendShapeIndex("TongueAxis"))
                    },
                    new[] { true, true, true });

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.ownershipBasisRankDeficient, Is.True);
                Assert.That(result.ownershipNonZeroSelectedColumns,
                    Is.EqualTo(new[] { true, true, true }));
                Assert.That(result.ownershipAuthorityGroups[0, 0], Is.True);
                Assert.That(result.ownershipAuthorityGroups[0, 1], Is.True);
                Assert.That(result.ownershipAuthorityGroups[1, 0], Is.True);
                Assert.That(result.ownershipAuthorityGroups[1, 1], Is.True);
                Assert.That(result.ownershipAuthorityGroups[0, 2], Is.False,
                    "An independent tongue ray must not inherit jaw/lip authority.");
                Assert.That(result.ownershipAuthorityGroups[1, 2], Is.False);
                Assert.That(result.ownershipAuthorityGroups[2, 2], Is.True);

                foreach (var value in result.ownershipProjectionCoefficients)
                    Assert.That(value, Is.InRange(-2f, 2f),
                        "Near-collinear calibration emitted an unstable coefficient.");
                foreach (var scale in result.ownershipCarrierScales)
                    Assert.That(scale, Is.InRange(0f, 2f),
                        "Near-collinear calibration emitted an explosive carrier.");
                foreach (var scale in result.ownershipNegativeCarrierScales)
                    Assert.That(scale, Is.InRange(0f, 2f),
                        "Near-collinear calibration emitted an explosive carrier.");

                var residual = Read(
                    result.mesh,
                    result.mesh.GetBlendShapeIndex(result.residualBlendShapeNames[1]));
                var projected = ProjectedResidual(result, 1, mesh.vertexCount);
                AssertDelta(residual, projected,
                    "The selected observable span must own the full conflicting residual.");
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static PrimaryFixture CreatePrimaryFixture()
        {
            var mesh = NewMesh("Observable projection source");
            var visible = new Delta(
                new[] { new Vector3(0.12f, 0f, 0f), new Vector3(0.025f, 0f, 0f), Vector3.zero, Vector3.zero },
                new[] { new Vector3(0.025f, 0f, 0f), new Vector3(0.006f, 0f, 0f), Vector3.zero, Vector3.zero },
                new[] { new Vector3(0.035f, 0f, 0f), new Vector3(0.008f, 0f, 0f), Vector3.zero, Vector3.zero });
            var hiddenDirection = new Delta(
                new[] { new Vector3(0f, 0.18f, 0f), new Vector3(0f, 0.04f, 0f), new Vector3(0f, 0.025f, 0f), Vector3.zero },
                new[] { new Vector3(0f, 0.035f, 0f), new Vector3(0f, 0.009f, 0f), new Vector3(0f, 0.005f, 0f), Vector3.zero },
                new[] { new Vector3(0f, 0.045f, 0f), new Vector3(0f, 0.011f, 0f), new Vector3(0f, 0.007f, 0f), Vector3.zero });
            var unobservable = Add(Scale(visible, 0.6f), hiddenDirection);
            var unique = new Delta(
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.075f), new Vector3(0f, 0f, 0.02f) },
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.022f), new Vector3(0f, 0f, 0.006f) },
                new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 0.029f), new Vector3(0f, 0f, 0.008f) });
            AddShape(mesh, "BasisVisible", visible);
            AddShape(mesh, "BasisUnobservable", unobservable);
            var authoredRay = Add(Scale(unobservable, 0.7f), Scale(visible, -0.8f), unique);
            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
            {
                var scale = 0.55f + viseme * 0.035f;
                AddShape(
                    mesh,
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme],
                    Scale(authoredRay, scale));
            }
            return new PrimaryFixture(mesh, visible, unobservable);
        }

        private static AnimationClip PoseClip(string name, string shape)
        {
            var clip = new AnimationClip { name = name };
            var binding = EditorCurveBinding.FloatCurve(
                "Face", typeof(SkinnedMeshRenderer), "blendShape." + shape);
            AnimationUtility.SetEditorCurve(
                clip, binding, AnimationCurve.Linear(0f, 0f, 1f, 100f));
            return clip;
        }

        private static AdvancedVisemeMeshCalibrator.BlendShapePoseInput Pose(
            params (int index, float weight)[] values)
        {
            var elements = new AdvancedVisemeMeshCalibrator.BlendShapePoseElement[
                values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                elements[index] = new AdvancedVisemeMeshCalibrator.BlendShapePoseElement(
                    values[index].index, values[index].weight);
            }
            return new AdvancedVisemeMeshCalibrator.BlendShapePoseInput(elements);
        }

        private static Mesh NewMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                Vector3.zero, Vector3.right, Vector3.up, Vector3.right + Vector3.up
            };
            mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
            };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            return mesh;
        }

        private static int AddShape(Mesh mesh, string name, Delta delta)
        {
            mesh.AddBlendShapeFrame(
                name, 100f, delta.vertices, delta.normals, delta.tangents);
            return mesh.GetBlendShapeIndex(name);
        }

        private static int[] VisemeIndices(Mesh mesh)
        {
            var indices = new int[VisemeReconstructionProfile.VisemeCount];
            for (var viseme = 0; viseme < indices.Length; viseme++)
            {
                indices[viseme] = mesh.GetBlendShapeIndex(
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme]);
            }
            return indices;
        }

        private static MeshSnapshot Snapshot(Mesh mesh)
        {
            var names = new string[mesh.blendShapeCount];
            var values = new Delta[mesh.blendShapeCount];
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                names[index] = mesh.GetBlendShapeName(index);
                values[index] = Read(mesh, index);
            }
            return new MeshSnapshot(names, values);
        }

        private static void AssertSourceUnchanged(Mesh mesh, MeshSnapshot snapshot)
        {
            Assert.That(mesh.blendShapeCount, Is.EqualTo(snapshot.names.Length));
            for (var index = 0; index < snapshot.names.Length; index++)
            {
                Assert.That(mesh.GetBlendShapeName(index), Is.EqualTo(snapshot.names[index]));
                AssertDelta(snapshot.values[index], Read(mesh, index), "source shape " + index);
            }
        }

        private static Delta Read(Mesh mesh, int blendShapeIndex)
        {
            Assert.That(blendShapeIndex, Is.GreaterThanOrEqualTo(0));
            var vertices = new Vector3[mesh.vertexCount];
            var normals = new Vector3[mesh.vertexCount];
            var tangents = new Vector3[mesh.vertexCount];
            var frame = mesh.GetBlendShapeFrameCount(blendShapeIndex) - 1;
            mesh.GetBlendShapeFrameVertices(
                blendShapeIndex, frame, vertices, normals, tangents);
            var scale = 100f / mesh.GetBlendShapeFrameWeight(blendShapeIndex, frame);
            return new Delta(
                Scale(vertices, scale), Scale(normals, scale), Scale(tangents, scale));
        }

        private static Delta ProjectedResidual(
            AdvancedVisemeMeshCalibrator.Result result,
            int viseme,
            int vertexCount)
        {
            var output = new Delta(
                new Vector3[vertexCount],
                new Vector3[vertexCount],
                new Vector3[vertexCount]);
            for (var column = 0;
                 column < result.ownershipCarrierBlendShapeNames.Length;
                 column++)
            {
                var coefficient = result.ownershipProjectionCoefficients[viseme, column];
                var positiveName = result.ownershipCarrierBlendShapeNames[column];
                var positiveScale = result.ownershipCarrierScales[column];
                if (coefficient < 0f && !string.IsNullOrEmpty(positiveName) &&
                    positiveScale > 1e-7f)
                {
                    var carrier = Read(
                        result.mesh, result.mesh.GetBlendShapeIndex(positiveName));
                    output = Add(output, Scale(carrier, coefficient / positiveScale));
                }

                var negativeName = result.ownershipNegativeCarrierBlendShapeNames[column];
                var negativeScale = result.ownershipNegativeCarrierScales[column];
                if (coefficient > 0f && !string.IsNullOrEmpty(negativeName) &&
                    negativeScale > 1e-7f)
                {
                    var carrier = Read(
                        result.mesh, result.mesh.GetBlendShapeIndex(negativeName));
                    output = Add(output, Scale(carrier, -coefficient / negativeScale));
                }
            }
            return output;
        }

        private static Delta Add(Delta first, Delta second, Delta third)
        {
            return Add(Add(first, second), third);
        }

        private static Delta Add(Delta left, Delta right)
        {
            return new Delta(
                Add(left.vertices, right.vertices),
                Add(left.normals, right.normals),
                Add(left.tangents, right.tangents));
        }

        private static Delta Subtract(Delta left, Delta right)
        {
            return new Delta(
                Subtract(left.vertices, right.vertices),
                Subtract(left.normals, right.normals),
                Subtract(left.tangents, right.tangents));
        }

        private static Delta Scale(Delta value, float scale)
        {
            return new Delta(
                Scale(value.vertices, scale),
                Scale(value.normals, scale),
                Scale(value.tangents, scale));
        }

        private static Vector3[] Add(Vector3[] left, Vector3[] right)
        {
            var output = new Vector3[left.Length];
            for (var index = 0; index < output.Length; index++)
                output[index] = left[index] + right[index];
            return output;
        }

        private static Vector3[] Subtract(Vector3[] left, Vector3[] right)
        {
            var output = new Vector3[left.Length];
            for (var index = 0; index < output.Length; index++)
                output[index] = left[index] - right[index];
            return output;
        }

        private static Vector3[] Scale(Vector3[] values, float scale)
        {
            var output = new Vector3[values.Length];
            for (var index = 0; index < output.Length; index++)
                output[index] = values[index] * scale;
            return output;
        }

        private static float Dot(Vector3[] left, Vector3[] right)
        {
            var value = 0f;
            for (var index = 0; index < left.Length; index++)
                value += Vector3.Dot(left[index], right[index]);
            return value;
        }

        private static void AssertDelta(Delta expected, Delta actual, string message)
        {
            for (var vertex = 0; vertex < expected.vertices.Length; vertex++)
            {
                Assert.That(Vector3.Distance(actual.vertices[vertex], expected.vertices[vertex]),
                    Is.LessThan(Tolerance), message + " vertex " + vertex);
                Assert.That(Vector3.Distance(actual.normals[vertex], expected.normals[vertex]),
                    Is.LessThan(Tolerance), message + " normal " + vertex);
                Assert.That(Vector3.Distance(actual.tangents[vertex], expected.tangents[vertex]),
                    Is.LessThan(Tolerance), message + " tangent " + vertex);
            }
        }

        private readonly struct Delta
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
        }

        private readonly struct MeshSnapshot
        {
            public readonly string[] names;
            public readonly Delta[] values;

            public MeshSnapshot(string[] names, Delta[] values)
            {
                this.names = names;
                this.values = values;
            }
        }

        private readonly struct PrimaryFixture
        {
            public readonly Mesh mesh;
            public readonly Delta visible;
            public readonly Delta unobservable;

            public PrimaryFixture(Mesh mesh, Delta visible, Delta unobservable)
            {
                this.mesh = mesh;
                this.visible = visible;
                this.unobservable = unobservable;
            }
        }
    }
}
#endif
