using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using com.vrcfury.api;

namespace YUCP.Components.Editor
{
    public static class VRCFuryHelper
    {
        public static VRCExpressionsMenu GetMenuFromLocation(VRCAvatarDescriptor descriptor, string location)
        {
            if (descriptor == null || descriptor.expressionsMenu == null)
            {
                return null;
            }

            VRCExpressionsMenu menu = descriptor.expressionsMenu;
            if (string.IsNullOrEmpty(location))
            {
                return menu;
            }

            string trimmed = location.Trim();
            if (trimmed.StartsWith("/"))
            {
                trimmed = trimmed.Substring(1);
            }
            if (trimmed.EndsWith("/"))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            if (string.IsNullOrEmpty(trimmed))
            {
                return menu;
            }

            string[] menus = trimmed.Split('/');

            for (int i = 0; i < menus.Length; i++)
            {
                string nextMenu = menus[i];
                if (string.IsNullOrEmpty(nextMenu))
                {
                    continue;
                }

                if (menu.controls == null)
                {
                    menu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
                }

                VRCExpressionsMenu.Control nextMenuControl = menu.controls
                    .Where(x => x.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    .FirstOrDefault(x => x.name == nextMenu);
                
                if (nextMenuControl == null || nextMenuControl.subMenu == null)
                {
                    // Create the missing submenu
                    VRCExpressionsMenu newSubMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    newSubMenu.name = nextMenu;
                    newSubMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
                    
                    // If the parent menu is an asset, save the submenu as a sub-asset
                    if (AssetDatabase.Contains(menu))
                    {
                        AssetDatabase.AddObjectToAsset(newSubMenu, menu);
                        AssetDatabase.SaveAssets();
                    }
                    
                    // Create the submenu control
                    VRCExpressionsMenu.Control subMenuControl = new VRCExpressionsMenu.Control()
                    {
                        name = nextMenu,
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = newSubMenu
                    };
                    
                    menu.controls.Add(subMenuControl);
                    EditorUtility.SetDirty(menu);
                    EditorUtility.SetDirty(newSubMenu);
                    
                    menu = newSubMenu;
                }
                else
                {
                    menu = nextMenuControl.subMenu;
                }
            }

            if (menu.controls == null)
            {
                menu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            }
            return menu;
        }

        public static void AddMenuToggle(VRCExpressionsMenu menu, string name, string parameterName)
        {
            if (menu == null || menu.controls == null)
            {
                Debug.LogWarning("[YUCP VRCFuryHelper] Cannot add menu toggle: menu is null or has no controls.");
                return;
            }

            if (menu.controls.Any(c => c.parameter != null && c.parameter.name == parameterName))
            {
                return;
            }

            menu.controls.Add(new VRCExpressionsMenu.Control()
            {
                name = name,
                style = VRCExpressionsMenu.Control.Style.Style1,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter()
                {
                    name = parameterName
                }
            });

            EditorUtility.SetDirty(menu);
        }
        public static void AddControllerToVRCFury(VRCAvatarDescriptor descriptor, AnimatorController controller, VRCAvatarDescriptor.AnimLayerType layerType = VRCAvatarDescriptor.AnimLayerType.FX)
        {
            
            CreateNewFullController(descriptor, controller, layerType);
        }

        public static void AddParamsToVRCFury(VRCAvatarDescriptor descriptor, VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters parameters)
        {
            
            var fullController = FuryComponents.CreateFullController(descriptor.gameObject);
            fullController.AddParams(parameters);
            EditorUtility.SetDirty(descriptor.gameObject);
        }

        private static void CreateNewFullController(VRCAvatarDescriptor descriptor, AnimatorController controller, VRCAvatarDescriptor.AnimLayerType layerType)
        {
            var fullController = FuryComponents.CreateFullController(descriptor.gameObject);
            fullController.AddController(controller, layerType);
            EditorUtility.SetDirty(descriptor.gameObject);
        }

        public static void AddGlobalParamToVRCFury(VRCAvatarDescriptor descriptor, string parameterName)
        {
            // Always use the public API for consistency and robustness
            var fullController = FuryComponents.CreateFullController(descriptor.gameObject);
            fullController.AddGlobalParam(parameterName);
            EditorUtility.SetDirty(descriptor.gameObject);
        }

    }
}

