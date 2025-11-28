using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.Utils;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(MirroredArmatureLinkData))]
	public class MirroredArmatureLinkDataEditor : UnityEditor.Editor
	{
		private static Material previewMat;
		private static readonly System.Collections.Generic.Dictionary<SkinnedMeshRenderer, Mesh> bakedMeshCache = new System.Collections.Generic.Dictionary<SkinnedMeshRenderer, Mesh>();

		// Foldouts (match AttachToBlendshape style)
		private bool foldTarget = true;
		private bool foldBuiltins = true;
		private bool foldCustoms = true;
		private bool foldMenu = true;
		private bool foldConstraint = true;
		private bool foldAdvanced = false;
		
		private VisualElement customTargetsContainer;
		private SerializedProperty customTargetsProp;
		private int lastCustomTargetsArraySize = -1;

		private void OnEnable()
		{
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
			serializedObject.Update();
			
			var root = new VisualElement();
			YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
			root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Mirrored Armature Link"));
			
			var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(MirroredArmatureLinkData));
			if (betaWarning != null) root.Add(betaWarning);

			var targetCard = YUCPUIToolkitHelper.CreateCard("Target Configuration", "Configure the symmetric body part and offset");
			var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
			targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("part"), "Symmetric Body Part"));
			targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("offset"), "Offset Path"));
			root.Add(targetCard);

			var builtinsCard = YUCPUIToolkitHelper.CreateCard("Built-in Left/Right Options", "Configure left and right side options");
			var builtinsContent = YUCPUIToolkitHelper.GetCardContent(builtinsCard);
			
			var includeLeft = serializedObject.FindProperty("includeLeft");
			var includeRight = serializedObject.FindProperty("includeRight");
			builtinsContent.Add(YUCPUIToolkitHelper.CreateField(includeLeft, "Include Left"));
			
			var leftContainer = new VisualElement();
			leftContainer.style.paddingLeft = 15;
			leftContainer.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("leftParam"), "Left Global Param"));
			leftContainer.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("leftDefaultOn"), "Left Default On"));
			leftContainer.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("leftExclusiveOffState"), "Left Exclusive Off State"));
			
			var leftAnimContainer = new VisualElement();
			leftAnimContainer.style.flexDirection = FlexDirection.Row;
			leftAnimContainer.style.marginBottom = 5;
			var leftAnimField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("leftAnimation"), "Left Animation Clip");
			leftAnimField.style.flexGrow = 1;
			leftAnimField.style.marginRight = 5;
			leftAnimContainer.Add(leftAnimField);
			
			var leftRecordButton = YUCPUIToolkitHelper.CreateButton("Record", () =>
			{
				var data = target as MirroredArmatureLinkData;
				if (data != null)
				{
					var go = data.gameObject;
					EditorApplication.delayCall += () =>
					{
						var so = new SerializedObject(data);
						AnimationClipRecorder.RecordMuscleAnimation(so.FindProperty("leftAnimation"), go, "LeftAnimation");
					};
				}
			}, YUCPUIToolkitHelper.ButtonVariant.Secondary);
			leftRecordButton.style.width = 60;
			leftAnimContainer.Add(leftRecordButton);
			leftContainer.Add(leftAnimContainer);
			builtinsContent.Add(leftContainer);
			
			builtinsContent.Add(YUCPUIToolkitHelper.CreateField(includeRight, "Include Right"));
			
			var rightContainer = new VisualElement();
			rightContainer.style.paddingLeft = 15;
			rightContainer.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rightParam"), "Right Global Param"));
			rightContainer.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rightDefaultOn"), "Right Default On"));
			rightContainer.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rightExclusiveOffState"), "Right Exclusive Off State"));
			
			var rightAnimContainer = new VisualElement();
			rightAnimContainer.style.flexDirection = FlexDirection.Row;
			rightAnimContainer.style.marginBottom = 5;
			var rightAnimField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rightAnimation"), "Right Animation Clip");
			rightAnimField.style.flexGrow = 1;
			rightAnimField.style.marginRight = 5;
			rightAnimContainer.Add(rightAnimField);
			
			var rightRecordButton = YUCPUIToolkitHelper.CreateButton("Record", () =>
			{
				var data = target as MirroredArmatureLinkData;
				if (data != null)
				{
					var go = data.gameObject;
					EditorApplication.delayCall += () =>
					{
						var so = new SerializedObject(data);
						AnimationClipRecorder.RecordMuscleAnimation(so.FindProperty("rightAnimation"), go, "RightAnimation");
					};
				}
			}, YUCPUIToolkitHelper.ButtonVariant.Secondary);
			rightRecordButton.style.width = 60;
			rightAnimContainer.Add(rightRecordButton);
			rightContainer.Add(rightAnimContainer);
			builtinsContent.Add(rightContainer);
			
			root.schedule.Execute(() =>
			{
				leftContainer.style.display = includeLeft.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
				rightContainer.style.display = includeRight.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
			}).Every(100);
			
			root.Add(builtinsCard);

			var customsCard = YUCPUIToolkitHelper.CreateCard("Custom Targets", "Configure custom symmetric targets");
			var customsContent = YUCPUIToolkitHelper.GetCardContent(customsCard);
			customTargetsProp = serializedObject.FindProperty("customTargets");
			
			var addButton = YUCPUIToolkitHelper.CreateButton("Add Custom Target", () =>
			{
				customTargetsProp.arraySize++;
				serializedObject.ApplyModifiedProperties();
			}, YUCPUIToolkitHelper.ButtonVariant.Secondary);
			addButton.style.marginBottom = 10;
			customsContent.Add(addButton);
			
			customTargetsContainer = new VisualElement();
			customTargetsContainer.name = "customTargetsContainer";
			customsContent.Add(customTargetsContainer);
			
			lastCustomTargetsArraySize = -1;
			UpdateCustomTargetsUI(customTargetsContainer, customTargetsProp);
			
			root.schedule.Execute(() =>
			{
				serializedObject.Update();
				if (customTargetsProp.arraySize != lastCustomTargetsArraySize)
				{
					lastCustomTargetsArraySize = customTargetsProp.arraySize;
					UpdateCustomTargetsUI(customTargetsContainer, customTargetsProp);
				}
			}).Every(100);
			
			root.Add(customsCard);

			var menuCard = YUCPUIToolkitHelper.CreateCard("Toggles & Menu", "Configure menu and toggle settings");
			var menuContent = YUCPUIToolkitHelper.GetCardContent(menuCard);
			menuContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("menuPath"), "Menu Path"));
			menuContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("saved"), "Saved"));
			menuContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("exclusiveTag"), "Exclusive Tag"));
			root.Add(menuCard);

			var constraintCard = YUCPUIToolkitHelper.CreateCard("Constraint Mode", "Configure constraint behavior");
			var constraintContent = YUCPUIToolkitHelper.GetCardContent(constraintCard);
			constraintContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("constraintMode"), "Mode"));
			root.Add(constraintCard);

			var advancedFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced", foldAdvanced);
			advancedFoldout.RegisterValueChangedCallback(evt => { foldAdvanced = evt.newValue; });
			advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Logging"));
			root.Add(advancedFoldout);

			root.schedule.Execute(() => serializedObject.ApplyModifiedProperties()).Every(100);

			return root;
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

		private void UpdateCustomTargetsUI(VisualElement container, SerializedProperty customTargetsProp)
		{
			serializedObject.Update();
			container.Clear();
			
			if (customTargetsProp.arraySize == 0)
			{
				var emptyLabel = new Label("No custom targets. Click 'Add Custom Target' to add one.");
				emptyLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
				emptyLabel.style.fontSize = 12;
				emptyLabel.style.marginTop = 5;
				emptyLabel.style.marginBottom = 5;
				container.Add(emptyLabel);
				return;
			}
			
			for (int i = 0; i < customTargetsProp.arraySize; i++)
			{
				var elementProp = customTargetsProp.GetArrayElementAtIndex(i);
				var displayNameProp = elementProp.FindPropertyRelative("displayName");
				var displayName = string.IsNullOrEmpty(displayNameProp.stringValue) ? "Custom" : displayNameProp.stringValue;
				var targetFoldout = YUCPUIToolkitHelper.CreateFoldout($"Target {i + 1}: {displayName}", true);
				
				var targetContent = new VisualElement();
				targetContent.style.paddingLeft = 10;
				targetContent.style.paddingTop = 5;
				targetContent.style.paddingBottom = 5;
				targetContent.Bind(serializedObject);
				
				var targetTypeProp = elementProp.FindPropertyRelative("targetType");
				var displayNameField = YUCPUIToolkitHelper.CreateField(displayNameProp, "Display Name");
				var targetTypeField = YUCPUIToolkitHelper.CreateField(targetTypeProp, "Target Type");
				var globalBoolParamField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("globalBoolParam"), "Global Bool Param");
				
				targetContent.Add(displayNameField);
				targetContent.Add(globalBoolParamField);
				targetContent.Add(targetTypeField);
				
				var humanoidBoneField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("humanoidBone"), "Humanoid Bone");
				var transformField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("transform"), "Transform");
				var armaturePathField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("armaturePath"), "Armature Path");
				
				var humanoidBoneContainer = new VisualElement();
				humanoidBoneContainer.Add(humanoidBoneField);
				humanoidBoneContainer.style.paddingLeft = 15;
				
				var transformContainer = new VisualElement();
				transformContainer.Add(transformField);
				transformContainer.style.paddingLeft = 15;
				
				var armaturePathContainer = new VisualElement();
				armaturePathContainer.Add(armaturePathField);
				armaturePathContainer.style.paddingLeft = 15;
				
				targetContent.Add(humanoidBoneContainer);
				targetContent.Add(transformContainer);
				targetContent.Add(armaturePathContainer);
				
				var offsetPathField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("offsetPath"), "Offset Path");
				var defaultOnField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("defaultOn"), "Default On");
				var keepTransformsField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("keepTransforms"), "Keep Transforms");
				var exclusiveOffStateField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("exclusiveOffState"), "Exclusive Off State");
				
				targetContent.Add(offsetPathField);
				targetContent.Add(defaultOnField);
				targetContent.Add(keepTransformsField);
				targetContent.Add(exclusiveOffStateField);
				
				var animContainer = new VisualElement();
				animContainer.style.flexDirection = FlexDirection.Row;
				animContainer.style.marginBottom = 5;
				var animField = YUCPUIToolkitHelper.CreateField(elementProp.FindPropertyRelative("animationClip"), "Animation Clip");
				animField.style.flexGrow = 1;
				animField.style.marginRight = 5;
				animContainer.Add(animField);
				targetContent.Add(animContainer);
				
				var index = i;
				var removeButtonContainer = new VisualElement();
				removeButtonContainer.style.flexDirection = FlexDirection.Row;
				removeButtonContainer.style.justifyContent = Justify.FlexEnd;
				removeButtonContainer.style.marginTop = 8;
				removeButtonContainer.style.paddingTop = 8;
				removeButtonContainer.style.borderTopWidth = 1;
				removeButtonContainer.style.borderTopColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f, 0.5f));
				
				var removeButton = YUCPUIToolkitHelper.CreateButton("Remove", () =>
				{
					customTargetsProp.DeleteArrayElementAtIndex(index);
					serializedObject.ApplyModifiedProperties();
				}, YUCPUIToolkitHelper.ButtonVariant.Danger);
				removeButton.style.fontSize = 12;
				removeButton.style.height = 26;
				removeButton.style.paddingLeft = 12;
				removeButton.style.paddingRight = 12;
				removeButton.style.paddingTop = 4;
				removeButton.style.paddingBottom = 4;
				
				removeButtonContainer.Add(removeButton);
				targetContent.Add(removeButtonContainer);
				
				targetFoldout.Add(targetContent);
				
				var targetIndex = index;
				System.Action updateVisibility = () =>
				{
					serializedObject.Update();
					if (targetIndex >= customTargetsProp.arraySize) return;
					
					var currentElement = customTargetsProp.GetArrayElementAtIndex(targetIndex);
					var currentTargetType = (MirroredArmatureLinkData.TargetType)currentElement.FindPropertyRelative("targetType").enumValueIndex;
					var currentDisplayName = currentElement.FindPropertyRelative("displayName").stringValue;
					if (string.IsNullOrEmpty(currentDisplayName))
					{
						currentDisplayName = "Custom";
					}
					
					humanoidBoneContainer.style.display = currentTargetType == MirroredArmatureLinkData.TargetType.HumanoidBone ? DisplayStyle.Flex : DisplayStyle.None;
					transformContainer.style.display = currentTargetType == MirroredArmatureLinkData.TargetType.Transform ? DisplayStyle.Flex : DisplayStyle.None;
					armaturePathContainer.style.display = currentTargetType == MirroredArmatureLinkData.TargetType.ArmaturePath ? DisplayStyle.Flex : DisplayStyle.None;
					
					targetFoldout.text = $"Target {targetIndex + 1}: {currentDisplayName}";
				};
				
				targetFoldout.schedule.Execute(() => updateVisibility()).Every(100);
				
				updateVisibility();
				
				container.Add(targetFoldout);
				
				if (i < customTargetsProp.arraySize - 1)
				{
					container.Add(YUCPUIToolkitHelper.CreateDivider());
				}
			}
		}
		

	}
}


