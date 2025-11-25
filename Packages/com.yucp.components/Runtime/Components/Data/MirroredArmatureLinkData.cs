using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase; // for IEditorOnly & IPreprocessCallbackBehaviour

namespace YUCP.Components
{
	[AddComponentMenu("YUCP/Mirrored Armature Link")]
	[HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
	[DisallowMultipleComponent]
	public class MirroredArmatureLinkData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
	{
		public enum BodyPart
		{
			UpperLeg, LowerLeg, Foot, Toes,
			Shoulder, UpperArm, LowerArm, Hand,
			ThumbProximal, ThumbIntermediate, ThumbDistal,
			IndexProximal, IndexIntermediate, IndexDistal,
			MiddleProximal, MiddleIntermediate, MiddleDistal,
			RingProximal, RingIntermediate, RingDistal,
			LittleProximal, LittleIntermediate, LittleDistal,
			Eye
		}

		public enum TargetType
		{
			HumanoidBone,
			Transform,
			ArmaturePath
		}

		public enum ConstraintMode
		{
			Parent,
			PositionOnly,
			RotationOnly
		}

		[Serializable]
		public class CustomTarget
		{
			[Tooltip("Display name for the toggle/menu item")] public string displayName = "Custom";
			[Tooltip("Global bool parameter name to expose for this option")] public string globalBoolParam = "";
			[Tooltip("How to resolve the target transform")] public TargetType targetType = TargetType.Transform;
			[Tooltip("Humanoid bone when TargetType is HumanoidBone")] public HumanBodyBones humanoidBone = HumanBodyBones.Hips;
			[Tooltip("Transform reference when TargetType is Transform")] public Transform transform;
			[Tooltip("Path under the avatar's armature root when TargetType is ArmaturePath")] public string armaturePath = "";
			[Tooltip("Optional child path under the resolved target")] public string offsetPath = "";
			[Tooltip("Toggle starts ON by default")] public bool defaultOn = false;
			[Tooltip("If true, maintain the current world placement when switching to this target (stores offsets).")]
			public bool keepTransforms = true;
			[Tooltip("If enabled, this toggle uses Exclusive Off State within the exclusive group")]
			public bool exclusiveOffState = false;
			[Tooltip("Optional animation clip to play when this toggle is activated")]
			public AnimationClip animationClip;
		}

		[Header("Symmetric Part (Built-ins)")]
		[Tooltip("Which symmetric body part to reference for Left/Right options")] public BodyPart part = BodyPart.Hand;
		[Tooltip("Optional offset string (child path) under the resolved bone")] public string offset = "";
		[Tooltip("Include a Left option referencing the left-side humanoid bone")] public bool includeLeft = true;
		[Tooltip("Include a Right option referencing the right-side humanoid bone")] public bool includeRight = true;
		[Tooltip("Optional global bool parameter for the Left option")] public string leftParam = "";
		[Tooltip("Optional global bool parameter for the Right option")] public string rightParam = "";
		[Tooltip("Toggle starts ON by default (Left)")] public bool leftDefaultOn = false;
		[Tooltip("Toggle starts ON by default (Right)")] public bool rightDefaultOn = false;
		[Tooltip("If enabled, the Left toggle uses Exclusive Off State within the exclusive group")] public bool leftExclusiveOffState = false;
		[Tooltip("If enabled, the Right toggle uses Exclusive Off State within the exclusive group")] public bool rightExclusiveOffState = false;
		[Tooltip("Optional animation clip to play when Left toggle is activated")] public AnimationClip leftAnimation;
		[Tooltip("Optional animation clip to play when Right toggle is activated")] public AnimationClip rightAnimation;

		[Header("Custom Options (Unlimited)")]
		public List<CustomTarget> customTargets = new List<CustomTarget>();

		[Header("Toggles & Menu")]
		[Tooltip("Menu path used for the generated toggles (eg: Tools/Thing)")] public string menuPath = "";
		[Tooltip("If enabled, VRCFury toggles will be saved")]
		public bool saved = true;
		[Tooltip("Shared exclusive tag applied to all generated toggles (auto if empty)")]
		public string exclusiveTag = "";

		[Header("Constraint Mode")]
		[Tooltip("Choose how the object should be constrained when toggling between targets")]
		public ConstraintMode constraintMode = ConstraintMode.Parent;

		[Header("Debug")]
		[Tooltip("Enable additional build-time logging")] public bool debugMode = false;

		public int PreprocessOrder => 0;
		public bool OnPreprocess() => true;

		public static bool TryMapBodyPartToSides(BodyPart part, out HumanBodyBones left, out HumanBodyBones right)
		{
			left = HumanBodyBones.Hips;
			right = HumanBodyBones.Hips;
			switch (part)
			{
				case BodyPart.UpperLeg: left = HumanBodyBones.LeftUpperLeg; right = HumanBodyBones.RightUpperLeg; return true;
				case BodyPart.LowerLeg: left = HumanBodyBones.LeftLowerLeg; right = HumanBodyBones.RightLowerLeg; return true;
				case BodyPart.Foot: left = HumanBodyBones.LeftFoot; right = HumanBodyBones.RightFoot; return true;
				case BodyPart.Toes: left = HumanBodyBones.LeftToes; right = HumanBodyBones.RightToes; return true;
				case BodyPart.Shoulder: left = HumanBodyBones.LeftShoulder; right = HumanBodyBones.RightShoulder; return true;
				case BodyPart.UpperArm: left = HumanBodyBones.LeftUpperArm; right = HumanBodyBones.RightUpperArm; return true;
				case BodyPart.LowerArm: left = HumanBodyBones.LeftLowerArm; right = HumanBodyBones.RightLowerArm; return true;
				case BodyPart.Hand: left = HumanBodyBones.LeftHand; right = HumanBodyBones.RightHand; return true;
				case BodyPart.ThumbProximal: left = HumanBodyBones.LeftThumbProximal; right = HumanBodyBones.RightThumbProximal; return true;
				case BodyPart.ThumbIntermediate: left = HumanBodyBones.LeftThumbIntermediate; right = HumanBodyBones.RightThumbIntermediate; return true;
				case BodyPart.ThumbDistal: left = HumanBodyBones.LeftThumbDistal; right = HumanBodyBones.RightThumbDistal; return true;
				case BodyPart.IndexProximal: left = HumanBodyBones.LeftIndexProximal; right = HumanBodyBones.RightIndexProximal; return true;
				case BodyPart.IndexIntermediate: left = HumanBodyBones.LeftIndexIntermediate; right = HumanBodyBones.RightIndexIntermediate; return true;
				case BodyPart.IndexDistal: left = HumanBodyBones.LeftIndexDistal; right = HumanBodyBones.RightIndexDistal; return true;
				case BodyPart.MiddleProximal: left = HumanBodyBones.LeftMiddleProximal; right = HumanBodyBones.RightMiddleProximal; return true;
				case BodyPart.MiddleIntermediate: left = HumanBodyBones.LeftMiddleIntermediate; right = HumanBodyBones.RightMiddleIntermediate; return true;
				case BodyPart.MiddleDistal: left = HumanBodyBones.LeftMiddleDistal; right = HumanBodyBones.RightMiddleDistal; return true;
				case BodyPart.RingProximal: left = HumanBodyBones.LeftRingProximal; right = HumanBodyBones.RightRingProximal; return true;
				case BodyPart.RingIntermediate: left = HumanBodyBones.LeftRingIntermediate; right = HumanBodyBones.RightRingIntermediate; return true;
				case BodyPart.RingDistal: left = HumanBodyBones.LeftRingDistal; right = HumanBodyBones.RightRingDistal; return true;
				case BodyPart.LittleProximal: left = HumanBodyBones.LeftLittleProximal; right = HumanBodyBones.RightLittleProximal; return true;
				case BodyPart.LittleIntermediate: left = HumanBodyBones.LeftLittleIntermediate; right = HumanBodyBones.RightLittleIntermediate; return true;
				case BodyPart.LittleDistal: left = HumanBodyBones.LeftLittleDistal; right = HumanBodyBones.RightLittleDistal; return true;
				case BodyPart.Eye: left = HumanBodyBones.LeftEye; right = HumanBodyBones.RightEye; return true;
			}
			return false;
		}
	}
}


