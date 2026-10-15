using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
	[SupportBanner]
	[AddComponentMenu("YUCP/Conditional Deletion")]
	[HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
	[DisallowMultipleComponent]
	public class ConditionalDeletionData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour, ISerializationCallbackReceiver
	{
		public enum ConditionType
		{
			MeshExists,
			BoneExists,
			ComponentExists,
			GameObjectExists,
			TagMatches,
			LayerMatches
		}

		public enum LogicalOperator
		{
			And,
			Or,
			Not,
			Nand,
			Xor
		}

		public enum BoneTargetType
		{
			HumanoidBone,
			CustomPath
		}

		public enum MeshType
		{
			SkinnedMeshRenderer,
			MeshRenderer,
			Either
		}

		[Serializable]
		public class Condition
		{
			[Tooltip("What this condition checks for on the avatar.")]
			public ConditionType conditionType = ConditionType.GameObjectExists;

			[Tooltip("The condition is true when the check fails.")]
			public bool invert = false;

			[Header("Mesh Exists Settings")]
			[Tooltip("Name or path of the mesh object.")]
			public string meshNameOrPath = "";

			[Tooltip("The kind of renderer the mesh must have.")]
			public MeshType meshType = MeshType.Either;

			[Header("Bone Exists Settings")]
			[Tooltip("Find the bone by humanoid bone or by path.")]
			public BoneTargetType boneTargetType = BoneTargetType.HumanoidBone;

			[Tooltip("The humanoid bone to look for.")]
			public HumanBodyBones humanoidBone = HumanBodyBones.Hips;

			[Tooltip("Path of the bone or object to look for.")]
			public string bonePath = "";

			[Header("Component Exists Settings")]
			[Tooltip("Full type name of the component, such as UnityEngine.Rigidbody.")]
			public string componentTypeName = "";

			[Header("GameObject Exists Settings")]
			[Tooltip("Name or path of the object.")]
			public string gameObjectNameOrPath = "";

			[Header("Tag Matches Settings")]
			[Tooltip("Name or path of the object whose tag is checked.")]
			public string tagCheckGameObjectPath = "";

			[Tooltip("The tag the object must have.")]
			public string tagName = "Untagged";

			[Header("Layer Matches Settings")]
			[Tooltip("Name or path of the object whose layer is checked.")]
			public string layerCheckGameObjectPath = "";

			[Tooltip("The layer the object must be on.")]
			public string layerName = "Default";
		}

	[Serializable]
	public class ConditionGroup
	{
		[Tooltip("How the conditions and nested groups in this group combine.")]
		public LogicalOperator groupOperator = LogicalOperator.And;

		[Tooltip("The group is true when its combined result is false.")]
		public bool invertGroup = false;

		[Tooltip("The checks in this group.")]
		public List<Condition> conditions = new List<Condition>();

		[Tooltip("Groups inside this group, for more complex logic. Up to 8 levels deep.")]
		public List<ConditionGroup> nestedGroups = new List<ConditionGroup>();
		
		public void TrimExcessiveDepth(int currentDepth = 0, int maxDepth = 8)
		{
			if (currentDepth >= maxDepth)
			{
				nestedGroups.Clear();
				return;
			}
			
			foreach (var nested in nestedGroups)
			{
				nested?.TrimExcessiveDepth(currentDepth + 1, maxDepth);
			}
		}
	}

		[Header("Condition Groups")]
		[Tooltip("The objects are deleted depending on whether any of these groups is true.")]
		public List<ConditionGroup> conditionGroups = new List<ConditionGroup>();

		[Header("Objects to Delete")]
		[Tooltip("Objects removed from the build when the condition is met.")]
		public List<GameObject> objectsToDelete = new List<GameObject>();

		[Header("Settings")]
		[Tooltip("Any Group True deletes the objects when at least one group is true. No Group True deletes them when every group is false.")]
		public bool deleteOnTrue = true;

		[Header("Debug")]
		[Tooltip("Logs each condition's result to the Console when the avatar builds.")]
		public bool debugMode = false;

		public int PreprocessOrder => 0;
		public bool OnPreprocess() => true;
		
		public void OnBeforeSerialize()
		{
			foreach (var group in conditionGroups)
			{
				group?.TrimExcessiveDepth(0, 8);
			}
		}
		
		public void OnAfterDeserialize()
		{
			foreach (var group in conditionGroups)
			{
				group?.TrimExcessiveDepth(0, 8);
			}
		}
	}
}

