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
			[Tooltip("Type of condition to check")]
			public ConditionType conditionType = ConditionType.GameObjectExists;

			[Tooltip("Invert the result of this condition")]
			public bool invert = false;

			[Header("Mesh Exists Settings")]
			[Tooltip("Name or path to the mesh GameObject")]
			public string meshNameOrPath = "";

			[Tooltip("Type of mesh renderer to check for")]
			public MeshType meshType = MeshType.Either;

			[Header("Bone Exists Settings")]
			[Tooltip("How to resolve the bone target")]
			public BoneTargetType boneTargetType = BoneTargetType.HumanoidBone;

			[Tooltip("Humanoid bone to check (when using HumanoidBone target type)")]
			public HumanBodyBones humanoidBone = HumanBodyBones.Hips;

			[Tooltip("Custom path to bone/transform (when using CustomPath target type)")]
			public string bonePath = "";

			[Header("Component Exists Settings")]
			[Tooltip("Full type name of component to check for (e.g., 'UnityEngine.Rigidbody')")]
			public string componentTypeName = "";

			[Header("GameObject Exists Settings")]
			[Tooltip("Name or path to the GameObject")]
			public string gameObjectNameOrPath = "";

			[Header("Tag Matches Settings")]
			[Tooltip("Name or path to the GameObject to check tag")]
			public string tagCheckGameObjectPath = "";

			[Tooltip("Tag name to match")]
			public string tagName = "Untagged";

			[Header("Layer Matches Settings")]
			[Tooltip("Name or path to the GameObject to check layer")]
			public string layerCheckGameObjectPath = "";

			[Tooltip("Layer name to match")]
			public string layerName = "Default";
		}

	[Serializable]
	public class ConditionGroup
	{
		[Tooltip("Logical operator for combining conditions in this group")]
		public LogicalOperator groupOperator = LogicalOperator.And;

		[Tooltip("Invert the entire group result")]
		public bool invertGroup = false;

		[Tooltip("List of conditions in this group")]
		public List<Condition> conditions = new List<Condition>();

		[Tooltip("Nested condition groups (for complex logic). Maximum depth: 8 levels to avoid Unity serialization depth limit.")]
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
		[Tooltip("Top-level condition groups. If any group evaluates to true, deletion will occur (unless deleteOnTrue is false).")]
		public List<ConditionGroup> conditionGroups = new List<ConditionGroup>();

		[Header("Objects to Delete")]
		[Tooltip("GameObjects to delete when conditions are met")]
		public List<GameObject> objectsToDelete = new List<GameObject>();

		[Header("Settings")]
		[Tooltip("If true, delete objects when conditions evaluate to true. If false, delete when conditions evaluate to false.")]
		public bool deleteOnTrue = true;

		[Header("Debug")]
		[Tooltip("Enable detailed logging during avatar build")]
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

