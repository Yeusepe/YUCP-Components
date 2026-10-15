using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase; // for IEditorOnly & IPreprocessCallbackBehaviour

namespace YUCP.Components
{
	[SupportBanner]
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
			[Tooltip("The toggle's name in the avatar menu.")] public string displayName = "Custom";
			[Tooltip("A global bool parameter for this target, so other systems can select it. Empty generates one.")] public string globalBoolParam = "";
			[Tooltip("How the target is found: a humanoid bone, an object, or a path from the avatar root.")] public TargetType targetType = TargetType.Transform;
			[Tooltip("The humanoid bone to link to.")] public HumanBodyBones humanoidBone = HumanBodyBones.Hips;
			[Tooltip("The object to link to.")] public Transform transform;
			[Tooltip("The target's path from the avatar root, e.g. Armature/Hips/Spine.")] public string armaturePath = "";
			[Tooltip("A child of the target to link to instead, as a path. Empty uses the target.")] public string offsetPath = "";
			[Tooltip("This toggle starts on.")] public bool defaultOn = false;
			[Tooltip("Keeps the object where it is relative to the target. Off snaps it onto the target.")]
			public bool keepTransforms = true;
			[Tooltip("This toggle turns on when every other toggle in the group is off.")]
			public bool exclusiveOffState = false;
			[Tooltip("An animation that plays while this target is selected.")]
			public AnimationClip animationClip;
		}

		[Header("Symmetric Part (Built-ins)")]
		[Tooltip("The body part the object moves between, on the left and right sides.")] public BodyPart part = BodyPart.Hand;
		[Tooltip("A child of the bone to link to instead, as a path. Empty uses the bone.")] public string offset = "";
		[Tooltip("Adds a toggle that links the object to the left side.")] public bool includeLeft = true;
		[Tooltip("Adds a toggle that links the object to the right side.")] public bool includeRight = true;
		[Tooltip("A global bool parameter for this side, so other systems can select it. Empty generates one.")] public string leftParam = "";
		[Tooltip("A global bool parameter for this side, so other systems can select it. Empty generates one.")] public string rightParam = "";
		[Tooltip("This toggle starts on.")] public bool leftDefaultOn = false;
		[Tooltip("This toggle starts on.")] public bool rightDefaultOn = false;
		[Tooltip("This toggle turns on when every other toggle in the group is off.")] public bool leftExclusiveOffState = false;
		[Tooltip("This toggle turns on when every other toggle in the group is off.")] public bool rightExclusiveOffState = false;
		[Tooltip("An animation that plays while the object is on the left.")] public AnimationClip leftAnimation;
		[Tooltip("An animation that plays while the object is on the right.")] public AnimationClip rightAnimation;

		[Header("Custom Options (Unlimited)")]
		public List<CustomTarget> customTargets = new List<CustomTarget>();

		[Header("Toggles & Menu")]
		[Tooltip("The menu folder for the generated toggles, e.g. Accessories/Watch. Empty puts them at the top level.")] public string menuPath = "";
		[Tooltip("Remembers the selected target between worlds and avatar reloads.")]
		public bool saved = true;
		[Tooltip("Toggles that share a tag can't be on together. Empty uses a tag unique to this object.")]
		public string exclusiveTag = "";

		[Header("Constraint Mode")]
		[Tooltip("Parent follows the target's position and rotation. Position and Rotation follow only one.")]
		public ConstraintMode constraintMode = ConstraintMode.Parent;

		[Header("Debug")]
		[Tooltip("Logs extra detail while the avatar builds.")] public bool debugMode = false;

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


