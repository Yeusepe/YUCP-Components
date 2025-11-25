using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components.Editor.Utils;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(MirroredArmatureLinkData))]
	public class MirroredArmatureLinkDataEditor : UnityEditor.Editor
	{
		private ReorderableList customList;
		private static Material previewMat;
		private static readonly System.Collections.Generic.Dictionary<SkinnedMeshRenderer, Mesh> bakedMeshCache = new System.Collections.Generic.Dictionary<SkinnedMeshRenderer, Mesh>();

		// Foldouts (match AttachToBlendshape style)
		private bool foldTarget = true;
		private bool foldBuiltins = true;
		private bool foldCustoms = true;
		private bool foldMenu = true;
		private bool foldConstraint = true;
		private bool foldAdvanced = false;

		private void OnEnable()
		{
			var so = serializedObject;
			customList = new ReorderableList(so, so.FindProperty("customTargets"), true, true, true, true);
			customList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Custom Targets");
			customList.elementHeight = EditorGUIUtility.singleLineHeight * 8 + 24;
			customList.drawElementCallback = (rect, index, active, focused) =>
			{
				var element = customList.serializedProperty.GetArrayElementAtIndex(index);
				rect.y += 2;
				var line = EditorGUIUtility.singleLineHeight + 2;

				EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), $"Custom Target {index + 1}", EditorStyles.boldLabel);
				var y = rect.y + line;

				EditorGUI.PropertyField(new Rect(rect.x, y, rect.width * 0.5f - 4, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("displayName"), new GUIContent("Display Name"));
				EditorGUI.PropertyField(new Rect(rect.x + rect.width * 0.5f + 2, y, rect.width * 0.5f - 2, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("globalBoolParam"), new GUIContent("Global Param"));
				y += line;

				EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("targetType"), new GUIContent("Target Type"));
				y += line;

				var typeProp = element.FindPropertyRelative("targetType");
				switch ((MirroredArmatureLinkData.TargetType)typeProp.enumValueIndex)
				{
					case MirroredArmatureLinkData.TargetType.HumanoidBone:
						EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("humanoidBone"), new GUIContent("Humanoid Bone"));
						break;
					case MirroredArmatureLinkData.TargetType.Transform:
						EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("transform"), new GUIContent("Transform"));
						break;
					case MirroredArmatureLinkData.TargetType.ArmaturePath:
						EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("armaturePath"), new GUIContent("Armature Path"));
						break;
				}
				y += line;
				EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("offsetPath"), new GUIContent("Offset Path"));
				y += line;
				
				// Animation clip with Record button
				var animClipRect = new Rect(rect.x, y, rect.width - 60, EditorGUIUtility.singleLineHeight);
				var animClipProp = element.FindPropertyRelative("animationClip");
				EditorGUI.PropertyField(animClipRect, animClipProp, new GUIContent("Animation Clip"));
				var recordRect = new Rect(rect.x + rect.width - 55, y, 50, EditorGUIUtility.singleLineHeight);
				if (GUI.Button(recordRect, "Record"))
				{
					var data = target as MirroredArmatureLinkData;
					if (data != null)
					{
						// Capture values to avoid GUI state issues
						var go = data.gameObject;
						var capturedIndex = index; // Capture index for lambda
						// Use EditorApplication.delayCall to avoid GUI state issues
						EditorApplication.delayCall += () =>
						{
							var so = new SerializedObject(data);
							var prop = so.FindProperty("customTargets").GetArrayElementAtIndex(capturedIndex);
							AnimationClipRecorder.RecordMuscleAnimation(prop.FindPropertyRelative("animationClip"), go, "CustomTargetAnimation");
						};
					}
				}
				y += line;
				
				EditorGUI.PropertyField(new Rect(rect.x, y, rect.width * 0.33f - 2, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("defaultOn"), new GUIContent("Default On"));
				EditorGUI.PropertyField(new Rect(rect.x + rect.width * 0.33f + 2, y, rect.width * 0.33f - 2, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("keepTransforms"), new GUIContent("Keep Transforms"));
				EditorGUI.PropertyField(new Rect(rect.x + rect.width * 0.66f + 4, y, rect.width * 0.34f - 4, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("exclusiveOffState"), new GUIContent("Exclusive Off"));
			};

			SceneView.duringSceneGui += OnSceneGUI;

			// Load foldout states
			var t = target as MirroredArmatureLinkData;
			if (t != null)
			{
				int id = t.GetInstanceID();
				foldTarget = SessionState.GetBool($"MirLink_Target_{id}", true);
				foldBuiltins = SessionState.GetBool($"MirLink_Builtins_{id}", true);
				foldCustoms = SessionState.GetBool($"MirLink_Customs_{id}", true);
				foldMenu = SessionState.GetBool($"MirLink_Menu_{id}", true);
				foldConstraint = SessionState.GetBool($"MirLink_Constraint_{id}", true);
				foldAdvanced = SessionState.GetBool($"MirLink_Advanced_{id}", false);
			}
		}

		private void OnDisable()
		{
			SceneView.duringSceneGui -= OnSceneGUI;
			var t = target as MirroredArmatureLinkData;
			if (t != null)
			{
				int id = t.GetInstanceID();
				SessionState.SetBool($"MirLink_Target_{id}", foldTarget);
				SessionState.SetBool($"MirLink_Builtins_{id}", foldBuiltins);
				SessionState.SetBool($"MirLink_Customs_{id}", foldCustoms);
				SessionState.SetBool($"MirLink_Menu_{id}", foldMenu);
				SessionState.SetBool($"MirLink_Constraint_{id}", foldConstraint);
				SessionState.SetBool($"MirLink_Advanced_{id}", foldAdvanced);
			}
		}

		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();
			root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Mirrored Armature Link"));
			var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(MirroredArmatureLinkData));
			if (betaWarning != null) root.Add(betaWarning);
			var container = new IMGUIContainer(() => { OnInspectorGUIContent(); });
			root.Add(container);
			return root;
		}

		public override void OnInspectorGUI()
		{
			BetaWarningHelper.DrawBetaWarningIMGUI(typeof(MirroredArmatureLinkData));
			OnInspectorGUIContent();
		}

		private void OnInspectorGUIContent()
		{
			serializedObject.Update();

			// Target section
			foldTarget = DrawFoldoutSection("Target Configuration", foldTarget, () => {
				EditorGUILayout.PropertyField(serializedObject.FindProperty("part"), new GUIContent("Symmetric Body Part"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("offset"), new GUIContent("Offset Path"));
			});

			EditorGUILayout.Space(4);
			// Built-ins
			foldBuiltins = DrawFoldoutSection("Built-in Left/Right Options", foldBuiltins, () => {
				var includeLeft = serializedObject.FindProperty("includeLeft");
				var includeRight = serializedObject.FindProperty("includeRight");
				EditorGUILayout.PropertyField(includeLeft, new GUIContent("Include Left"));
				if (includeLeft.boolValue)
				{
					EditorGUI.indentLevel++;
					EditorGUILayout.PropertyField(serializedObject.FindProperty("leftParam"), new GUIContent("Left Global Param"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("leftDefaultOn"), new GUIContent("Left Default On"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("leftExclusiveOffState"), new GUIContent("Left Exclusive Off State"));
					
					// Left Animation with Record button
					EditorGUILayout.BeginHorizontal();
					var leftAnimProp = serializedObject.FindProperty("leftAnimation");
					EditorGUILayout.PropertyField(leftAnimProp, new GUIContent("Left Animation Clip"), GUILayout.ExpandWidth(true));
					if (GUILayout.Button("Record", GUILayout.Width(60)))
					{
						var data = target as MirroredArmatureLinkData;
						if (data != null)
						{
							// Capture values to avoid GUI state issues
							var go = data.gameObject;
							// Use EditorApplication.delayCall to avoid GUI state issues
							EditorApplication.delayCall += () =>
							{
								var so = new SerializedObject(data);
								AnimationClipRecorder.RecordMuscleAnimation(so.FindProperty("leftAnimation"), go, "LeftAnimation");
							};
						}
					}
					EditorGUILayout.EndHorizontal();
					
					EditorGUI.indentLevel--;
				}
				EditorGUILayout.PropertyField(includeRight, new GUIContent("Include Right"));
				if (includeRight.boolValue)
				{
					EditorGUI.indentLevel++;
					EditorGUILayout.PropertyField(serializedObject.FindProperty("rightParam"), new GUIContent("Right Global Param"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("rightDefaultOn"), new GUIContent("Right Default On"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("rightExclusiveOffState"), new GUIContent("Right Exclusive Off State"));
					
					// Right Animation with Record button
					EditorGUILayout.BeginHorizontal();
					var rightAnimProp = serializedObject.FindProperty("rightAnimation");
					EditorGUILayout.PropertyField(rightAnimProp, new GUIContent("Right Animation Clip"), GUILayout.ExpandWidth(true));
					if (GUILayout.Button("Record", GUILayout.Width(60)))
					{
						var data = target as MirroredArmatureLinkData;
						if (data != null)
						{
							// Capture values to avoid GUI state issues
							var go = data.gameObject;
							// Use EditorApplication.delayCall to avoid GUI state issues
							EditorApplication.delayCall += () =>
							{
								var so = new SerializedObject(data);
								AnimationClipRecorder.RecordMuscleAnimation(so.FindProperty("rightAnimation"), go, "RightAnimation");
							};
						}
					}
					EditorGUILayout.EndHorizontal();
					
					EditorGUI.indentLevel--;
				}
			});

			EditorGUILayout.Space(4);
			// Customs list
			foldCustoms = DrawFoldoutSection("Custom Targets", foldCustoms, () => {
				customList.DoLayoutList();
			});

			EditorGUILayout.Space(4);
			// Menu & Save
			foldMenu = DrawFoldoutSection("Toggles & Menu", foldMenu, () => {
				EditorGUILayout.PropertyField(serializedObject.FindProperty("menuPath"), new GUIContent("Menu Path"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("saved"), new GUIContent("Saved"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("exclusiveTag"), new GUIContent("Exclusive Tag"));
			});

			EditorGUILayout.Space(4);
			// Constraint Mode
			foldConstraint = DrawFoldoutSection("Constraint Mode", foldConstraint, () => {
				EditorGUILayout.PropertyField(serializedObject.FindProperty("constraintMode"), new GUIContent("Mode"));
			});

			EditorGUILayout.Space(4);
			// Advanced
			foldAdvanced = DrawFoldoutSection("Advanced", foldAdvanced, () => {
				EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), new GUIContent("Debug Logging"));
			});

			serializedObject.ApplyModifiedProperties();
		}

		private bool DrawFoldoutSection(string title, bool foldout, System.Action content)
		{
			EditorGUILayout.Space(2);
			var rect = EditorGUILayout.GetControlRect(false, 25);
			var boxRect = new Rect(rect.x - 2, rect.y, rect.width + 4, rect.height);
			var originalColor = GUI.backgroundColor;
			GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
			GUI.Box(boxRect, "", EditorStyles.helpBox);
			GUI.backgroundColor = originalColor;
			var foldoutRect = new Rect(rect.x + 5, rect.y + 4, rect.width - 10, 16);
			var style = new GUIStyle(EditorStyles.foldout);
			style.fontStyle = FontStyle.Bold;
			style.fontSize = 12;
			bool newFoldout = EditorGUI.Foldout(foldoutRect, foldout, title, true, style);
			if (newFoldout)
			{
				EditorGUILayout.Space(2);
				var contentColor = GUI.backgroundColor;
				GUI.backgroundColor = new Color(0f, 0f, 0f, 0.1f);
				EditorGUILayout.BeginVertical(EditorStyles.helpBox);
				GUI.backgroundColor = contentColor;
				EditorGUILayout.Space(5);
				content?.Invoke();
				EditorGUILayout.Space(5);
				EditorGUILayout.EndVertical();
			}
			return newFoldout;
		}

		private void OnSceneGUI(SceneView sceneView)
		{
			var data = target as MirroredArmatureLinkData;
			if (data == null || data.gameObject == null) return;
			// Show preview when this object or a child is selected, or if it's in multi-selection
			var sel = Selection.activeGameObject;
			bool shouldShow = false;
			if (sel != null)
			{
				shouldShow = (sel == data.gameObject || sel.transform.IsChildOf(data.transform));
			}
			// Check all selected objects (for multi-select and locked inspector scenarios)
			if (!shouldShow && Selection.gameObjects.Length > 0)
			{
				foreach (var go in Selection.gameObjects)
				{
					if (go == data.gameObject || go.transform.IsChildOf(data.transform))
					{
						shouldShow = true;
						break;
					}
				}
			}
			// Show preview if this component is being inspected (works for locked inspector)
			// The editor is active when the component is selected or inspector is locked on it
			if (!shouldShow && target != null)
			{
				shouldShow = true;
			}
			if (!shouldShow) return;

			var animator = data.GetComponentInParent<Animator>();
			if (animator == null || animator.avatar == null) return;

			EnsurePreviewMaterial();

			bool prevInvert = GL.invertCulling;
			GL.invertCulling = true; // handle negative scale reflection if any
			try
			{
				var meshRenderers = data.GetComponentsInChildren<MeshRenderer>(true);
				var skinnedRenderers = data.GetComponentsInChildren<SkinnedMeshRenderer>(true);

				// Compute relative matrices from object root
				var objRoot = data.transform;
				var objRootInv = objRoot.worldToLocalMatrix;

				// Resolve built-in left/right targets
				Transform leftT = null, rightT = null;
				if (MirroredArmatureLinkData.TryMapBodyPartToSides(data.part, out var leftB, out var rightB))
				{
					leftT = animator.GetBoneTransform(leftB);
					rightT = animator.GetBoneTransform(rightB);
					leftT = ApplyOffset(leftT, data.offset);
					rightT = ApplyOffset(rightT, data.offset);
				}

				// Determine base side (closest to object) for mirroring offset
				Transform baseSide = null, mirrorSide = null;
				if (leftT != null && rightT != null)
				{
					float dL = Vector3.Distance(objRoot.position, leftT.position);
					float dR = Vector3.Distance(objRoot.position, rightT.position);
					baseSide = (dL <= dR) ? leftT : rightT;
					mirrorSide = (baseSide == leftT) ? rightT : leftT;
				}

				// Draw mirrored ghost on the opposite side bone (if both sides exist)
				if (baseSide != null && mirrorSide != null)
				{
					// Calculate local offset relative to the base side
					ComputeLocalOffset(objRoot, baseSide, out var localPos, out var localRot);
					// Mirror ONLY X component in base-side local space
					var mirroredLocalPos = new Vector3(-localPos.x, localPos.y, localPos.z);
					var mirroredLocalRot = MirrorLocalRotationX(localRot);
					// Build preview matrix per constraint mode
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

					// Draw object meshes at mGhost
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

				// Draw ghosts at each custom target honoring keepTransforms and constraintMode
				var customs = ResolveCustomTargets(animator, data);
				if (customs.Count > 0)
				{
					// Build a lookup of keepTransforms per resolved transform (by index order)
					int idx = 0;
					foreach (var ct in data.customTargets)
					{
						if (idx >= customs.Count) break;
						var t = customs[idx++];
						Matrix4x4 mGhost;
						if (ct.keepTransforms)
						{
							ComputeLocalOffset(objRoot, t, out var lp, out var lr);
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
							// No offset: snap to target per mode
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

						// Draw object meshes at mGhost
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

		// Utilities duplicated locally for preview parity
		private static void ComputeLocalOffset(Transform obj, Transform source, out Vector3 localPos, out Quaternion localRot)
		{
			if (obj == null || source == null)
			{
				localPos = Vector3.zero;
				localRot = Quaternion.identity;
				return;
			}
			var m = source.worldToLocalMatrix * obj.localToWorldMatrix;
			localPos = m.MultiplyPoint3x4(Vector3.zero);
			var forward = new Vector3(m.m02, m.m12, m.m22);
			var upwards = new Vector3(m.m01, m.m11, m.m21);
			if (forward.sqrMagnitude < 1e-6f || upwards.sqrMagnitude < 1e-6f)
			{
				localRot = Quaternion.identity;
			}
			else
			{
				localRot = Quaternion.LookRotation(forward, upwards);
			}
		}

		// (unused)
		private static Matrix4x4 BuildRelativeNoScale(Transform root, Transform child) { return Matrix4x4.identity; }

		private static Matrix4x4 CorrectScale(Matrix4x4 m, Vector3 desiredScale)
		{
			// Extract rotation by normalizing basis vectors
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
			// Implement R' = S R S where S = diag(-1,1,1) in local space
			var R = Matrix4x4.Rotate(q);
			var S = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
			var Rp = S * R * S;
			return Rp.rotation;
		}

	}
}


