using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(ConditionalDeletionData))]
	public class ConditionalDeletionDataEditor : UnityEditor.Editor
	{
		private const int MAX_NESTING_DEPTH = 8;

		private bool foldConditionGroups = true;
		private bool foldObjects = true;
		private bool foldSettings = true;

		private void OnEnable()
		{
			var data = target as ConditionalDeletionData;
			if (data != null)
			{
				int id = data.GetInstanceID();
				foldConditionGroups = SessionState.GetBool($"CondDel_Conditions_{id}", true);
				foldObjects = SessionState.GetBool($"CondDel_Objects_{id}", true);
				foldSettings = SessionState.GetBool($"CondDel_Settings_{id}", true);
			}
		}

		private void OnDisable()
		{
			var data = target as ConditionalDeletionData;
			if (data != null)
			{
				int id = data.GetInstanceID();
				SessionState.SetBool($"CondDel_Conditions_{id}", foldConditionGroups);
				SessionState.SetBool($"CondDel_Objects_{id}", foldObjects);
				SessionState.SetBool($"CondDel_Settings_{id}", foldSettings);
			}
		}

		public override VisualElement CreateInspectorGUI()
		{
			serializedObject.Update();
			
			var root = new VisualElement();
			YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
			root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Conditional Deletion"));
			
			root.Add(YUCPUIToolkitHelper.CreateHelpBox("Define conditions that check for objects on your avatar. If ANY condition group evaluates to true, the specified objects will be deleted during build.", YUCPUIToolkitHelper.MessageType.Info));

			var conditionCard = YUCPUIToolkitHelper.CreateCard("Condition Groups", "Define conditions that trigger deletion");
			var conditionContent = YUCPUIToolkitHelper.GetCardContent(conditionCard);
			
			var conditionGroupsProp = serializedObject.FindProperty("conditionGroups");
			var conditionGroupsField = new PropertyField(conditionGroupsProp, "Condition Groups");
			conditionGroupsField.AddToClassList("yucp-field-input");
			
			var depthWarning = new Label($"(Depth limit: {MAX_NESTING_DEPTH})");
			depthWarning.style.fontSize = 10;
			depthWarning.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
			depthWarning.style.marginTop = 3;
			depthWarning.style.marginBottom = 5;
			
			conditionContent.Add(conditionGroupsField);
			conditionContent.Add(depthWarning);
			root.Add(conditionCard);

			var objectsCard = YUCPUIToolkitHelper.CreateCard("Objects to Delete", "Specify which objects should be deleted");
			var objectsContent = YUCPUIToolkitHelper.GetCardContent(objectsCard);
			
			objectsContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Drag GameObjects here that should be deleted when conditions are met.", YUCPUIToolkitHelper.MessageType.None));
			
			var objectsToDeleteProp = serializedObject.FindProperty("objectsToDelete");
			var objectsToDeleteField = new PropertyField(objectsToDeleteProp, "Objects to Delete");
			objectsToDeleteField.AddToClassList("yucp-field-input");
			objectsContent.Add(objectsToDeleteField);
			root.Add(objectsCard);

			var settingsCard = YUCPUIToolkitHelper.CreateCard("Settings", "Configure deletion behavior");
			var settingsContent = YUCPUIToolkitHelper.GetCardContent(settingsCard);
			settingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("deleteOnTrue"), "Delete On True"));
			settingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Mode"));
			root.Add(settingsCard);

			root.schedule.Execute(() => serializedObject.ApplyModifiedProperties()).Every(100);

			return root;
		}


	}
}
