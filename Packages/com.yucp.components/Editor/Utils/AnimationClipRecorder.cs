using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.Utils
{
	/// <summary>
	/// Generic utility for recording animation clips compatible with avatar muscles and VRCFury.
	/// Can be used by any editor that needs to record animations.
	/// </summary>
	public static class AnimationClipRecorder
	{
		/// <summary>
		/// Records an animation clip using VRCFury's recorder, compatible with avatar muscles.
		/// Creates the clip if it doesn't exist, initializes it with current muscle values, then opens VRCFury recorder.
		/// </summary>
		/// <param name="animClipProp">SerializedProperty pointing to the AnimationClip field</param>
		/// <param name="targetObject">The GameObject to record from (usually the component's GameObject)</param>
		/// <param name="defaultName">Default name for new animation clips</param>
		public static void RecordMuscleAnimation(SerializedProperty animClipProp, GameObject targetObject, string defaultName = "NewMuscleAnimation")
		{
			if (targetObject == null)
			{
				EditorUtility.DisplayDialog("Error", "Target object is null.", "OK");
				return;
			}

			var animator = targetObject.GetComponentInParent<Animator>();
			if (animator == null)
			{
				animator = Object.FindObjectOfType<Animator>();
			}

			if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
			{
				EditorUtility.DisplayDialog("Error", "No humanoid avatar found. This requires a humanoid avatar with an Animator.", "OK");
				return;
			}

			// Create or get animation clip
			AnimationClip clip = animClipProp.objectReferenceValue as AnimationClip;
			if (clip == null)
			{
				string path = EditorUtility.SaveFilePanelInProject("Save Muscle Animation", defaultName, "anim", "Path to save animation");
				if (string.IsNullOrEmpty(path)) return;
				
				clip = new AnimationClip();
				AssetDatabase.CreateAsset(clip, path);
				animClipProp.objectReferenceValue = clip;
				animClipProp.serializedObject.ApplyModifiedProperties();
			}

			// Use VRCFury recorder via reflection
			// Let Unity's Animation Window handle muscle recording naturally
			CallVRCFuryRecorder(clip, targetObject);
		}

		/// <summary>
		/// Records a regular animation clip using VRCFury's recorder (for non-muscle animations).
		/// </summary>
		/// <param name="animClipProp">SerializedProperty pointing to the AnimationClip field</param>
		/// <param name="targetObject">The GameObject to record from</param>
		/// <param name="defaultName">Default name for new animation clips</param>
		public static void RecordAnimation(SerializedProperty animClipProp, GameObject targetObject, string defaultName = "NewAnimation")
		{
			if (targetObject == null)
			{
				EditorUtility.DisplayDialog("Error", "Target object is null.", "OK");
				return;
			}

			// Create or get animation clip
			AnimationClip clip = animClipProp.objectReferenceValue as AnimationClip;
			if (clip == null)
			{
				string path = EditorUtility.SaveFilePanelInProject("Save Animation", defaultName, "anim", "Path to save animation");
				if (string.IsNullOrEmpty(path)) return;
				
				clip = new AnimationClip();
				AssetDatabase.CreateAsset(clip, path);
				animClipProp.objectReferenceValue = clip;
				animClipProp.serializedObject.ApplyModifiedProperties();
			}

			// Use VRCFury recorder via reflection
			CallVRCFuryRecorder(clip, targetObject);
		}


		private static string ConvertToAnimFormat(string humanTraitName)
		{
			if (string.IsNullOrEmpty(humanTraitName)) return null;
			
			string result = humanTraitName;
			result = result.Replace("Left Thumb", "LeftHand.Thumb");
			result = result.Replace("Left Index", "LeftHand.Index");
			result = result.Replace("Left Middle", "LeftHand.Middle");
			result = result.Replace("Left Ring", "LeftHand.Ring");
			result = result.Replace("Left Little", "LeftHand.Little");
			
			result = result.Replace("Right Thumb", "RightHand.Thumb");
			result = result.Replace("Right Index", "RightHand.Index");
			result = result.Replace("Right Middle", "RightHand.Middle");
			result = result.Replace("Right Ring", "RightHand.Ring");
			result = result.Replace("Right Little", "RightHand.Little");
			
			result = result.Replace(" 1 ", ".1 ");
			result = result.Replace(" 2 ", ".2 ");
			result = result.Replace(" 3 ", ".3 ");
			result = result.Replace(" Spread", ".Spread");
			
			return result;
		}

		private static void CallVRCFuryRecorder(AnimationClip clip, GameObject targetObject)
		{
			// Use reflection to call VRCFury's internal RecorderUtils.Record method
			try
			{
				// Try to find the types from loaded assemblies
				System.Type recorderUtilsType = null;
				System.Type vfGameObjectType = null;
				foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
				{
					if (recorderUtilsType == null)
						recorderUtilsType = assembly.GetType("VF.Utils.RecorderUtils");
					if (vfGameObjectType == null)
						vfGameObjectType = assembly.GetType("VF.Builder.VFGameObject");
					if (recorderUtilsType != null && vfGameObjectType != null) break;
				}
				
				if (recorderUtilsType == null)
				{
					EditorUtility.DisplayDialog("Error", "VRCFury RecorderUtils not found. Make sure VRCFury is installed.", "OK");
					return;
				}
				
				if (vfGameObjectType == null)
				{
					EditorUtility.DisplayDialog("Error", "VRCFury VFGameObject type not found. Make sure VRCFury is installed.", "OK");
					return;
				}
				
				var recordMethod = recorderUtilsType.GetMethod("Record", BindingFlags.Public | BindingFlags.Static);
				if (recordMethod == null)
				{
					EditorUtility.DisplayDialog("Error", "VRCFury RecorderUtils.Record method not found.", "OK");
					return;
				}
				
				// Create VFGameObject by calling the implicit conversion operator via reflection
				// The implicit operator is: public static implicit operator VFGameObject(GameObject d)
				// We need to find the op_Implicit method
				var implicitOperators = vfGameObjectType.GetMethods(BindingFlags.Public | BindingFlags.Static)
					.Where(m => m.Name == "op_Implicit" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(GameObject))
					.ToArray();
				
				object vfGameObject = null;
				if (implicitOperators.Length > 0)
				{
					// Call the implicit operator directly
					vfGameObject = implicitOperators[0].Invoke(null, new object[] { targetObject });
				}
				else
				{
					// Fallback: try Cast method
					var castMethod = vfGameObjectType.GetMethod("Cast", new System.Type[] { typeof(GameObject) });
					if (castMethod != null)
					{
						vfGameObject = castMethod.Invoke(null, new object[] { targetObject });
					}
					else
					{
						EditorUtility.DisplayDialog("Error", "Could not convert GameObject to VFGameObject. VRCFury version may be incompatible.", "OK");
						return;
					}
				}
				
				if (vfGameObject == null)
				{
					EditorUtility.DisplayDialog("Error", "Failed to convert GameObject to VFGameObject.", "OK");
					return;
				}
				
				// Call VRCFury recorder - let it handle everything naturally
				// The method signature is: Record(AnimationClip clip, VFGameObject baseObj, bool rewriteClip = true)
				recordMethod.Invoke(null, new object[] { clip, vfGameObject, true });
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[AnimationClipRecorder] Failed to call VRCFury recorder: {ex.Message}");
				EditorUtility.DisplayDialog("Error", $"Failed to start VRCFury recorder: {ex.Message}", "OK");
			}
		}
	}
}

