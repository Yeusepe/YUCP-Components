using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;

namespace YUCP.Components.Editor
{
	/// <summary>
	/// Builds a ParentConstraint with N sources (Left/Right/custom) and generates VRCFury toggles
	/// that exclusively select which source is active by animating source weights.
	/// </summary>
	public class MirroredArmatureLinkProcessor : IVRCSDKPreprocessAvatarCallback
	{
		public int callbackOrder => int.MinValue + 10;

		public bool OnPreprocessAvatar(GameObject avatarRoot)
		{
			var dataList = avatarRoot.GetComponentsInChildren<MirroredArmatureLinkData>(true);
			if (dataList.Length == 0) return true;

			var progress = YUCP.Components.Editor.UI.YUCPProgressWindow.Create();
			try
			{
				var animator = avatarRoot.GetComponentInChildren<Animator>();
				if (animator == null)
				{
					Debug.LogError("[MirroredArmatureLinkProcessor] Animator missing on avatar.", avatarRoot);
					return true;
				}

				for (int i = 0; i < dataList.Length; i++)
				{
					var data = dataList[i];
					try
					{
						ProcessOne(data, animator);
					}
					catch (Exception ex)
					{
						Debug.LogError($"[MirroredArmatureLinkProcessor] Error processing '{data.name}': {ex.Message}", data);
						Debug.LogException(ex);
					}

					float p = (float)(i + 1) / dataList.Length;
					progress.Progress(p, $"Processed mirrored link {i + 1}/{dataList.Length}");
				}
			}
			finally
			{
				progress.CloseWindow();
			}

			return true;
		}

		private void ProcessOne(MirroredArmatureLinkData data, Animator animator)
		{
			// Resolve all targets (Left/Right/custom)
			var targets = new List<(string name, Transform t, bool defaultOn, string globalParam, bool keepTransforms, bool isCustom, AnimationClip animation)>();
			var usedParams = new HashSet<string>();
			var usedMenuNames = new HashSet<string>();

			// Pre-resolve built-in side transforms for base-side offset mirroring
			Transform leftResolved = null, rightResolved = null;

			// Left/Right mapping
				if (MirroredArmatureLinkData.TryMapBodyPartToSides(data.part, out var leftBone, out var rightBone))
			{
					if (data.includeLeft)
				{
					leftResolved = animator.GetBoneTransform(leftBone);
					leftResolved = ApplyOffset(leftResolved, data.offset);
					if (leftResolved != null)
					{
					var gp = EnsureUniqueParam(EnsureParamOrDefault(data.leftParam, data, "Left"), usedParams);
							targets.Add(("Left", leftResolved, data.leftDefaultOn, gp, true, false, data.leftAnimation));
					}
					else
					{
						Debug.LogWarning($"[MirroredArmatureLinkProcessor] Left bone not found for {data.part}", data);
					}
				}
					if (data.includeRight)
				{
					rightResolved = animator.GetBoneTransform(rightBone);
					rightResolved = ApplyOffset(rightResolved, data.offset);
					if (rightResolved != null)
					{
					var gp = EnsureUniqueParam(EnsureParamOrDefault(data.rightParam, data, "Right"), usedParams);
							targets.Add(("Right", rightResolved, data.rightDefaultOn, gp, true, false, data.rightAnimation));
					}
					else
					{
						Debug.LogWarning($"[MirroredArmatureLinkProcessor] Right bone not found for {data.part}", data);
					}
				}
			}

			// Custom targets
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
				if (resolved == null)
				{
					Debug.LogWarning($"[MirroredArmatureLinkProcessor] Custom target '{ct.displayName}' could not be resolved", data);
					continue;
				}
				var name = MakeUniqueMenuName(string.IsNullOrEmpty(ct.displayName) ? "Custom" : ct.displayName, usedMenuNames);
				var gparam = EnsureUniqueParam(EnsureParamOrDefault(ct.globalBoolParam, data, name), usedParams);
				targets.Add((name, resolved, ct.defaultOn, gparam, ct.keepTransforms, true, ct.animationClip));
			}

			if (targets.Count == 0)
			{
				Debug.LogError("[MirroredArmatureLinkProcessor] No valid targets resolved; skipping.", data);
				return;
			}

			// Always use ParentConstraint for consistent offsets across modes
			var parC = data.GetComponent<ParentConstraint>();
			if (parC == null) parC = data.gameObject.AddComponent<ParentConstraint>();
			parC.enabled = true;
			ClearSources(parC);
			// Choose base side (closest) for symmetric offsets across Left/Right
			Transform baseSide = null;
			if (leftResolved != null && rightResolved != null)
			{
				float dL = Vector3.Distance(data.transform.position, leftResolved.position);
				float dR = Vector3.Distance(data.transform.position, rightResolved.position);
				baseSide = (dL <= dR) ? leftResolved : rightResolved;
			}
			else if (leftResolved != null) baseSide = leftResolved; else if (rightResolved != null) baseSide = rightResolved;

			Vector3 baseLocalPos = Vector3.zero;
			Quaternion baseLocalRot = Quaternion.identity;
			if (baseSide != null)
			{
				ComputeLocalOffset(data.transform, baseSide, out baseLocalPos, out baseLocalRot);
			}

			for (int s = 0; s < targets.Count; s++)
			{
				var src = new ConstraintSource { sourceTransform = targets[s].t, weight = 0f };
				var index = parC.AddSource(src);
				if (targets[s].keepTransforms)
				{
					// Built-ins: use symmetric base offsets with X-only mirroring for opposite side
					if (!targets[s].isCustom && baseSide != null)
					{
						Vector3 posOff = baseLocalPos;
						if (targets[s].t != baseSide)
						{
							posOff.x = -posOff.x; // mirror only X
						}
						parC.SetTranslationOffset(index, posOff);
						var rotOff = (targets[s].t == baseSide) ? baseLocalRot : MirrorLocalRotationX(baseLocalRot);
						parC.SetRotationOffset(index, rotOff.eulerAngles);
					}
					else
					{
						ComputeLocalOffset(data.transform, targets[s].t, out var localPos, out var localRot);
						parC.SetTranslationOffset(index, localPos);
						parC.SetRotationOffset(index, localRot.eulerAngles);
					}
				}
				else
				{
					// Snap behavior: zero offsets
					parC.SetTranslationOffset(index, Vector3.zero);
					parC.SetRotationOffset(index, Vector3.zero);
				}
			}
			parC.locked = true;
			parC.constraintActive = true;

			// Generate one clip per source to select exactly that source (weight=1) and zero others
			var clips = new List<(string name, AnimationClip clip)>();
			var root = animator.transform;
			var path = AnimationUtility.CalculateTransformPath(data.transform, root);
			for (int select = 0; select < targets.Count; select++)
			{
				var clip = new AnimationClip();
				clip.name = $"MirLink_{SanitizeName(data.gameObject.name)}_{SanitizeName(targets[select].name)}";
				for (int s = 0; s < targets.Count; s++)
				{
					var binding = EditorCurveBinding.FloatCurve(path, typeof(ParentConstraint), $"m_Sources.Array.data[{s}].weight");
					var curve = new AnimationCurve(new Keyframe(0, s == select ? 1f : 0f));
					AnimationUtility.SetEditorCurve(clip, binding, curve);
				}
				clips.Add((targets[select].name, clip));
			}

			// Create VRCFury toggles
			var sharedTag = string.IsNullOrEmpty(data.exclusiveTag)
				? $"YUCP:MirArm:{AnimationUtility.CalculateTransformPath(data.transform, animator.transform)}"
				: data.exclusiveTag;

			for (int idx = 0; idx < targets.Count; idx++)
			{
				var target = targets[idx];
				var clip = clips[idx].clip;
				var toggle = FuryComponents.CreateToggle(data.gameObject);
				var toggleName = targets[idx].name;
				var menuName = string.IsNullOrEmpty(data.menuPath) ? toggleName : (data.menuPath.TrimEnd('/') + "/" + toggleName);
				toggle.SetMenuPath(menuName);
				if (data.saved) toggle.SetSaved();
				if (target.defaultOn) toggle.SetDefaultOn();
				toggle.AddExclusiveTag(sharedTag);
				// Exclusive Off State per option
				if (!target.isCustom)
				{
					if (string.Equals(toggleName, "Left", StringComparison.OrdinalIgnoreCase) && data.leftExclusiveOffState)
					{
						toggle.SetExclusiveOffState();
					}
					if (string.Equals(toggleName, "Right", StringComparison.OrdinalIgnoreCase) && data.rightExclusiveOffState)
					{
						toggle.SetExclusiveOffState();
					}
				}
				else
				{
					// Find matching custom to check flag
					foreach (var ct in data.customTargets)
					{
						var name = string.IsNullOrEmpty(ct.displayName) ? "Custom" : ct.displayName;
						if (name == toggleName && ct.exclusiveOffState)
						{
							toggle.SetExclusiveOffState();
							break;
						}
					}
				}
				toggle.SetGlobalParameter(target.globalParam);
				var actions = toggle.GetActions();
				actions.AddAnimationClip(clip);
				// Add custom animation clip if provided
				if (target.animation != null)
				{
					actions.AddAnimationClip(target.animation);
				}
			}

			if (data.debugMode)
			{
				Debug.Log($"[MirroredArmatureLinkProcessor] Built {targets.Count} sources and toggles for '{data.name}'", data);
			}
		}

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
			// Extract rotation from matrix
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

		private static Quaternion MirrorLocalRotationX(Quaternion q)
		{
			var R = Matrix4x4.Rotate(q);
			var S = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
			var Rp = S * R * S;
			return Rp.rotation;
		}

		private static void ClearSources(ParentConstraint c)
		{
			for (int i = c.sourceCount - 1; i >= 0; i--)
			{
				c.RemoveSource(i);
			}
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

		private static string EnsureParamOrDefault(string param, MirroredArmatureLinkData data, string option)
		{
			if (!string.IsNullOrEmpty(param)) return param;
			var baseName = SanitizeName(data.gameObject.name);
			return $"{baseName}_MirLink_{SanitizeName(option)}";
		}

		private static string EnsureUniqueParam(string candidate, HashSet<string> used)
		{
			var result = candidate;
			int i = 2;
			while (!used.Add(result))
			{
				result = candidate + "_" + i++;
			}
			return result;
		}

		private static string MakeUniqueMenuName(string candidate, HashSet<string> used)
		{
			var result = candidate;
			int i = 2;
			while (!used.Add(result))
			{
				result = candidate + " (" + i++ + ")";
			}
			return result;
		}

		private static string SanitizeName(string s)
		{
			if (string.IsNullOrEmpty(s)) return "Item";
			s = s.Replace('/', '_').Replace(' ', '_');
			foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
			return s;
		}
	}
}


