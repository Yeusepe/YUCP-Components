using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.Editor.Utils;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(MirroredArmatureLinkData))]
	public class MirroredArmatureLinkDataEditor : UnityEditor.Editor
	{
		private const string ShowPreviewKey = "YUCP.MirroredArmatureLink.ShowPreview";
		private static readonly string[] TargetTypes = { "Humanoid Bone", "Transform", "Armature Path" };

		private static Material previewMat;
		private static readonly Dictionary<SkinnedMeshRenderer, Mesh> bakedMeshCache = new Dictionary<SkinnedMeshRenderer, Mesh>();

		private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
		private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

		public override VisualElement CreateInspectorGUI()
		{
			var data = (MirroredArmatureLinkData)target;
			var ui = YUCPInspector.Create(this, "Mirrored Armature Link");
			ui.Status(() => Problems(data));

			var part = ui.Section("Body part");
			ui.Field(part, "part", "Body Part");
			ui.Field(part, "offset", "Offset Path");
			ui.Segmented(part, "constraintMode", "Constraint", "Parent", "Position", "Rotation");
			// A view setting, not part of the component: kept in EditorPrefs, never serialized.
			var preview = YUCPInspector.Checkbox("Show Preview",
				"Shows a ghost of the object on the other side and on each custom target in the Scene view.");
			preview.value = EditorPrefs.GetBool(ShowPreviewKey, true);
			preview.RegisterValueChangedCallback(e =>
			{
				EditorPrefs.SetBool(ShowPreviewKey, e.newValue);
				SceneView.RepaintAll();
			});
			part.Add(preview);

			var sides = ui.Section("Left and right");
			foreach (var side in new[] { "Left", "Right" })
			{
				var key = side.ToLowerInvariant();
				var options = ToggleSettingsInspector.Option(ui, sides, "include" + side, side);
				TargetToggle(ui, options, key + "Param", key + "DefaultOn", key + "ExclusiveOffState", key + "Animation", side + "Animation");
			}

			CustomTargets(ui);

			ToggleSettingsInspector.Section(ui, menuPath: "menuPath", saved: "saved", exclusiveTags: "exclusiveTag");

			ui.Field(ui.Advanced(), "debugMode", "Verbose Logging");
			return ui.Root;
		}

		private static IEnumerable<YUCPInspector.Message> Problems(MirroredArmatureLinkData data)
		{
			var animator = data.GetComponentInParent<Animator>(true);
			if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
				yield return YUCPInspector.Message.Error("Put this inside a humanoid avatar.");
			if (!data.includeLeft && !data.includeRight && data.customTargets.Count == 0)
				yield return YUCPInspector.Message.Error("Turn on Left or Right, or add a custom target.");
		}

		/// <summary>The toggle settings every target has, left, right or custom.</summary>
		private static void TargetToggle(YUCPInspector ui, VisualElement parent, string parameter, string defaultOn,
			string exclusiveOffState, string animation, string clipName)
		{
			ui.Field(parent, parameter, "Global Parameter");
			ui.Field(parent, defaultOn, "On by Default");
			ui.Field(parent, exclusiveOffState, "Exclusive Off State");
			ui.Field(parent, animation, "Animation");
			ui.Button(parent, "Record Animation", () =>
			{
				var component = (Component)ui.Object.targetObject;
				EditorApplication.delayCall += () =>
				{
					if (component == null) return;
					AnimationClipRecorder.RecordMuscleAnimation(new SerializedObject(component).FindProperty(animation), component.gameObject, clipName);
				};
			}, tooltip: "Record the pose with VRCFury's recorder. Creates a clip first if none is set.", icon: "Animation.Record");
		}

		/// <summary>
		/// Custom targets as a list: select one to edit it below. The detail is rebuilt only when the
		/// selection or the number of targets changes, from plain bound fields, so no refreshers pile up.
		/// </summary>
		private static void CustomTargets(YUCPInspector ui)
		{
			const string array = "customTargets";
			var section = ui.Section("Custom targets");
			var names = new List<string>();
			var selected = -1;
			var shown = (index: -2, count: -1);
			var detail = new VisualElement();
			SegmentedField type = null;
			VisualElement bone = null, transform = null, path = null;

			var list = ui.List(section, names, () =>
			{
				var row = new VisualElement();
				row.AddToClassList("yucp-list-item");
				var name = new Label();
				name.AddToClassList("yucp-list-item-name");
				row.Add(name);
				return row;
			}, (row, i) => row.Q<Label>().text = names[i],
				"Add a custom target to link the object to any bone or object.",
				() => Change(p => p.arraySize++, () => ui.Object.FindProperty(array).arraySize - 1),
				i => Change(p => p.DeleteArrayElementAtIndex(i), () => Mathf.Min(i, ui.Object.FindProperty(array).arraySize - 1)),
				"Add Custom Target", "Remove Custom Target");
			list.View.selectionChanged += _ =>
			{
				selected = list.View.selectedIndex;
				ui.Refresh();
			};
			list.View.itemIndexChanged += (from, to) => Change(p => p.MoveArrayElement(from, to), () => to);
			section.Add(detail);

			void Change(Action<SerializedProperty> edit, Func<int> select)
			{
				ui.Object.Update();
				edit(ui.Object.FindProperty(array));
				ui.Object.ApplyModifiedProperties();
				selected = select();
				shown.index = -2;
				ui.Refresh();
			}

			void UpdateType()
			{
				var property = type == null ? null : ui.Object.FindProperty($"{array}.Array.data[{shown.index}].targetType");
				if (property == null) return;
				var value = property.enumValueIndex;
				if (type.value != value) type.SetValueWithoutNotify(value);
				Show(bone, value == (int)MirroredArmatureLinkData.TargetType.HumanoidBone);
				Show(transform, value == (int)MirroredArmatureLinkData.TargetType.Transform);
				Show(path, value == (int)MirroredArmatureLinkData.TargetType.ArmaturePath);
			}

			void BuildDetail()
			{
				shown = (selected, names.Count);
				detail.Clear();
				type = null;
				if (selected < 0) return;
				var element = $"{array}.Array.data[{selected}].";
				var group = ui.Group(detail);
				ui.Field(group, element + "displayName", "Name");
				var typePath = element + "targetType";
				type = new SegmentedField("Target Type", TargetTypes) { tooltip = ui.Object.FindProperty(typePath).tooltip };
				type.RegisterValueChangedCallback(e =>
				{
					ui.Object.Update();
					ui.Object.FindProperty(typePath).enumValueIndex = e.newValue;
					ui.Object.ApplyModifiedProperties();
					UpdateType();
				});
				group.Add(type);
				bone = ui.Field(group, element + "humanoidBone", "Humanoid Bone");
				transform = ui.Field(group, element + "transform", "Transform");
				path = ui.Field(group, element + "armaturePath", "Armature Path");
				ui.Field(group, element + "offsetPath", "Offset Path");
				ui.Field(group, element + "keepTransforms", "Keep Transforms");
				TargetToggle(ui, group, element + "globalBoolParam", element + "defaultOn", element + "exclusiveOffState",
					element + "animationClip", names[selected].Replace(" ", "") + "Animation");
				UpdateType();
			}

			ui.OnRefresh(() =>
			{
				var targets = ui.Object.FindProperty(array);
				var current = new List<string>();
				for (var i = 0; i < targets.arraySize; i++)
				{
					var name = targets.GetArrayElementAtIndex(i).FindPropertyRelative("displayName").stringValue;
					current.Add(string.IsNullOrEmpty(name) ? "Custom" : name);
				}
				if (!current.SequenceEqual(names))
				{
					names.Clear();
					names.AddRange(current);
					list.Refresh();
				}
				if (selected >= names.Count) selected = names.Count - 1;
				if (selected < 0 && names.Count > 0) selected = 0;
				list.Select(selected);
				if (shown.index != selected || shown.count != names.Count) BuildDetail();
				else UpdateType();
			});
			ui.Refresh();
		}

		private static void Show(VisualElement element, bool visible)
		{
			var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			if (element != null && element.style.display != display) element.style.display = display;
		}

		private void OnSceneGUI(SceneView sceneView)
		{
			var data = target as MirroredArmatureLinkData;
			// The editor lives only while the component is inspected, so that is when the ghost shows; Show Preview hides it.
			if (data == null || data.gameObject == null || !EditorPrefs.GetBool(ShowPreviewKey, true)) return;

			var animator = data.GetComponentInParent<Animator>();
			if (animator == null || animator.avatar == null) return;

			EnsurePreviewMaterial();

			bool prevInvert = GL.invertCulling;
			GL.invertCulling = true;
			try
			{
				var meshRenderers = data.GetComponentsInChildren<MeshRenderer>(true);
				var skinnedRenderers = data.GetComponentsInChildren<SkinnedMeshRenderer>(true);


				var objRoot = data.transform;
				var objRootInv = objRoot.worldToLocalMatrix;


				Transform leftT = null, rightT = null;
				if (MirroredArmatureLinkData.TryMapBodyPartToSides(data.part, out var leftB, out var rightB))
				{
					leftT = animator.GetBoneTransform(leftB);
					rightT = animator.GetBoneTransform(rightB);
					leftT = ApplyOffset(leftT, data.offset);
					rightT = ApplyOffset(rightT, data.offset);
				}


				Transform baseSide = null, mirrorSide = null;
				if (leftT != null && rightT != null)
				{
					float dL = Vector3.Distance(objRoot.position, leftT.position);
					float dR = Vector3.Distance(objRoot.position, rightT.position);
					baseSide = (dL <= dR) ? leftT : rightT;
					mirrorSide = (baseSide == leftT) ? rightT : leftT;
				}


				if (baseSide != null && mirrorSide != null)
				{

					MirroredArmatureLinkProcessor.ComputeLocalOffset(objRoot, baseSide, out var localPos, out var localRot);

					var mirroredLocalPos = new Vector3(-localPos.x, localPos.y, localPos.z);
					var mirroredLocalRot = MirrorLocalRotationX(localRot);

					Matrix4x4 GetPreviewMatrixParent()
					{
						var m = Matrix4x4.TRS(mirroredLocalPos, mirroredLocalRot, Vector3.one);
						return mirrorSide.localToWorldMatrix * m;
					}
					Matrix4x4 GetPreviewMatrixPosition()
					{
						var pos = mirrorSide.TransformPoint(mirroredLocalPos);
						return Matrix4x4.TRS(pos, objRoot.rotation, Vector3.one);
					}
					Matrix4x4 GetPreviewMatrixRotation()
					{
						var rot = mirrorSide.rotation * localRot;
						return Matrix4x4.TRS(objRoot.position, rot, Vector3.one);
					}
					Matrix4x4 mGhost;
					switch (data.constraintMode)
					{
						case MirroredArmatureLinkData.ConstraintMode.PositionOnly: mGhost = GetPreviewMatrixPosition(); break;
						case MirroredArmatureLinkData.ConstraintMode.RotationOnly: mGhost = GetPreviewMatrixRotation(); break;
						default: mGhost = GetPreviewMatrixParent(); break;
					}


					foreach (var mr in meshRenderers)
					{
						var mf = mr.GetComponent<MeshFilter>();
						if (mf == null || mf.sharedMesh == null) continue;
						var relative = objRootInv * mf.transform.localToWorldMatrix;
						var m = mGhost * relative;
						m = CorrectScale(m, mf.transform.localToWorldMatrix.lossyScale);
						var mesh = mf.sharedMesh;
						int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
						for (int s = 0; s < subMeshCount; s++) Graphics.DrawMesh(mesh, m, previewMat, mr.gameObject.layer, null, s);
					}
					foreach (var smr in skinnedRenderers)
					{
						if (smr.sharedMesh == null) continue;
						if (!bakedMeshCache.TryGetValue(smr, out var baked)) { baked = new Mesh(); baked.indexFormat = smr.sharedMesh.indexFormat; bakedMeshCache[smr] = baked; }
						smr.BakeMesh(baked, false);
						var relative = objRootInv * smr.transform.localToWorldMatrix;
						var m = mGhost * relative;
						m = CorrectScale(m, smr.transform.localToWorldMatrix.lossyScale);
						int subMeshCount = Mathf.Max(1, baked.subMeshCount);
						for (int s = 0; s < subMeshCount; s++) Graphics.DrawMesh(baked, m, previewMat, smr.gameObject.layer, null, s);
					}
				}


				var customs = ResolveCustomTargets(animator, data);
				if (customs.Count > 0)
				{

					int idx = 0;
					foreach (var ct in data.customTargets)
					{
						if (idx >= customs.Count) break;
						var t = customs[idx++];
						Matrix4x4 mGhost;
						if (ct.keepTransforms)
						{
							MirroredArmatureLinkProcessor.ComputeLocalOffset(objRoot, t, out var lp, out var lr);
							if (data.constraintMode == MirroredArmatureLinkData.ConstraintMode.PositionOnly)
							{
								var pos = t.TransformPoint(lp);
								mGhost = Matrix4x4.TRS(pos, objRoot.rotation, Vector3.one);
							}
							else if (data.constraintMode == MirroredArmatureLinkData.ConstraintMode.RotationOnly)
							{
								var rot = t.rotation * lr;
								mGhost = Matrix4x4.TRS(objRoot.position, rot, Vector3.one);
							}
							else
							{
								var mLocal = Matrix4x4.TRS(lp, lr, Vector3.one);
								mGhost = t.localToWorldMatrix * mLocal;
							}
						}
						else
						{

							if (data.constraintMode == MirroredArmatureLinkData.ConstraintMode.PositionOnly)
							{
								mGhost = Matrix4x4.TRS(t.position, objRoot.rotation, Vector3.one);
							}
							else if (data.constraintMode == MirroredArmatureLinkData.ConstraintMode.RotationOnly)
							{
								mGhost = Matrix4x4.TRS(objRoot.position, t.rotation, Vector3.one);
							}
							else
							{
								mGhost = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
							}
						}


						foreach (var mr in meshRenderers)
						{
							var mf = mr.GetComponent<MeshFilter>();
							if (mf == null || mf.sharedMesh == null) continue;
							var relative = objRootInv * mf.transform.localToWorldMatrix;
							var m = mGhost * relative;
							m = CorrectScale(m, mf.transform.localToWorldMatrix.lossyScale);
							var mesh = mf.sharedMesh;
							int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
							for (int s = 0; s < subMeshCount; s++) Graphics.DrawMesh(mesh, m, previewMat, mr.gameObject.layer, null, s);
						}
						foreach (var smr in skinnedRenderers)
						{
							if (smr.sharedMesh == null) continue;
							if (!bakedMeshCache.TryGetValue(smr, out var baked2)) { baked2 = new Mesh(); baked2.indexFormat = smr.sharedMesh.indexFormat; bakedMeshCache[smr] = baked2; }
							smr.BakeMesh(baked2, false);
							var relative = objRootInv * smr.transform.localToWorldMatrix;
							var m = mGhost * relative;
							m = CorrectScale(m, smr.transform.localToWorldMatrix.lossyScale);
							int subMeshCount = Mathf.Max(1, baked2.subMeshCount);
							for (int s = 0; s < subMeshCount; s++) Graphics.DrawMesh(baked2, m, previewMat, smr.gameObject.layer, null, s);
						}
					}
				}
			}
			finally
			{
				GL.invertCulling = prevInvert;
			}
		}

		private static System.Collections.Generic.List<Transform> ResolveCustomTargets(Animator animator, MirroredArmatureLinkData data)
		{
			var list = new System.Collections.Generic.List<Transform>();
			foreach (var ct in data.customTargets)
			{
				Transform resolved = null;
				switch (ct.targetType)
				{
					case MirroredArmatureLinkData.TargetType.HumanoidBone:
						resolved = animator.GetBoneTransform(ct.humanoidBone);
						break;
					case MirroredArmatureLinkData.TargetType.Transform:
						resolved = ct.transform;
						break;
					case MirroredArmatureLinkData.TargetType.ArmaturePath:
						resolved = FindByPath(animator.transform, ct.armaturePath);
						break;
				}
				resolved = ApplyOffset(resolved, ct.offsetPath);
				if (resolved != null) list.Add(resolved);
			}
			return list;
		}

		private static Transform ApplyOffset(Transform t, string offsetPath)
		{
			if (t == null) return null;
			if (string.IsNullOrEmpty(offsetPath)) return t;
			var child = t.Find(offsetPath);
			return child != null ? child : t;
		}

		private static Transform FindByPath(Transform root, string path)
		{
			if (root == null || string.IsNullOrEmpty(path)) return null;
			return root.Find(path);
		}

		private static void EnsurePreviewMaterial()
		{
			if (previewMat != null) return;
			var shader = Shader.Find("Hidden/Internal-Colored");
			previewMat = new Material(shader);
			previewMat.hideFlags = HideFlags.HideAndDontSave;
			previewMat.SetColor("_Color", new Color(0.2f, 0.9f, 0.8f, 0.25f));
			previewMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
			previewMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			previewMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
			previewMat.SetInt("_ZWrite", 0);
			previewMat.renderQueue = 3000;
		}

		private static Matrix4x4 CorrectScale(Matrix4x4 m, Vector3 desiredScale)
		{

			var x = new Vector3(m.m00, m.m10, m.m20);
			var y = new Vector3(m.m01, m.m11, m.m21);
			var z = new Vector3(m.m02, m.m12, m.m22);
			if (x.sqrMagnitude < 1e-12f || y.sqrMagnitude < 1e-12f || z.sqrMagnitude < 1e-12f)
			{
				return m;
			}
			x.Normalize(); y.Normalize(); z.Normalize();
			var rot = Quaternion.LookRotation(z, y);
			var pos = new Vector3(m.m03, m.m13, m.m23);
			return Matrix4x4.TRS(pos, rot, desiredScale);
		}

		private static Quaternion MirrorLocalRotationX(Quaternion q)
		{

			var R = Matrix4x4.Rotate(q);
			var S = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
			var Rp = S * R * S;
			return Rp.rotation;
		}
	}
}
