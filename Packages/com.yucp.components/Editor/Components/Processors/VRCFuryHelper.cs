using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using com.vrcfury.api;

namespace YUCP.Components.Editor
{
    public static class VRCFuryHelper
    {
        public static void AddControllerToVRCFury(VRCAvatarDescriptor descriptor, AnimatorController controller, VRCAvatarDescriptor.AnimLayerType layerType = VRCAvatarDescriptor.AnimLayerType.FX)
        {
            Component existingVRCFury = FindExistingFullController(descriptor);
            
            if (existingVRCFury != null)
            {
                AddControllerToExistingFullController(existingVRCFury, controller, layerType);
            }
            else
            {
                CreateNewFullController(descriptor, controller, layerType);
            }
        }

        public static void AddParamsToVRCFury(VRCAvatarDescriptor descriptor, VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters parameters)
        {
            Component existingVRCFury = FindExistingFullController(descriptor);
            
            if (existingVRCFury != null)
            {
                AddParamsToExistingFullController(existingVRCFury, parameters);
            }
            else
            {
                var fullController = FuryComponents.CreateFullController(descriptor.gameObject);
                fullController.AddParams(parameters);
                EditorUtility.SetDirty(descriptor.gameObject);
            }
        }

        private static Component FindExistingFullController(VRCAvatarDescriptor descriptor)
        {
            var components = descriptor.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp != null && comp.GetType().Name == "VRCFury")
                {
                    var contentField = comp.GetType().GetField("content", BindingFlags.Public | BindingFlags.Instance);
                    if (contentField != null)
                    {
                        var content = contentField.GetValue(comp);
                        if (content != null && content.GetType().Name == "FullController")
                        {
                            return comp;
                        }
                    }
                }
            }
            return null;
        }

        private static void AddControllerToExistingFullController(Component existingVRCFury, AnimatorController controller, VRCAvatarDescriptor.AnimLayerType layerType)
        {
            var contentField = existingVRCFury.GetType().GetField("content", BindingFlags.Public | BindingFlags.Instance);
            if (contentField == null) return;
            
            var content = contentField.GetValue(existingVRCFury);
            if (content == null) return;
            
            var controllersField = content.GetType().GetField("controllers", BindingFlags.Public | BindingFlags.Instance);
            if (controllersField == null) return;
            
            var controllerEntryType = controllersField.FieldType.GetGenericArguments()[0];
            var entry = System.Activator.CreateInstance(controllerEntryType);
            controllerEntryType.GetField("controller").SetValue(entry, controller);
            controllerEntryType.GetField("type").SetValue(entry, layerType);
            
            var controllersList = controllersField.GetValue(content) as IList;
            controllersList?.Add(entry);
            
            EditorUtility.SetDirty(existingVRCFury);
        }

        private static void CreateNewFullController(VRCAvatarDescriptor descriptor, AnimatorController controller, VRCAvatarDescriptor.AnimLayerType layerType)
        {
            var fullController = FuryComponents.CreateFullController(descriptor.gameObject);
            fullController.AddController(controller, layerType);
            EditorUtility.SetDirty(descriptor.gameObject);
        }

        private static void AddParamsToExistingFullController(Component existingVRCFury, VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters parameters)
        {
            var contentField = existingVRCFury.GetType().GetField("content", BindingFlags.Public | BindingFlags.Instance);
            if (contentField == null) return;
            
            var content = contentField.GetValue(existingVRCFury);
            if (content == null) return;
            
            var addParamsMethod = content.GetType().GetMethod("AddParams", new[] { typeof(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters) });
            if (addParamsMethod != null)
            {
                addParamsMethod.Invoke(content, new object[] { parameters });
                EditorUtility.SetDirty(existingVRCFury);
            }
            else
            {
                Debug.LogWarning("[YUCP VRCFuryHelper] Could not find AddParams method on FullController. Parameters may not be added.");
            }
        }
    }
}

