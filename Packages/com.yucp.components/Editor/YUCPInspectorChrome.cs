using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// What every YUCP inspector shows above its own sections: the component header and the
    /// package-wide notices (beta, missing VRCFury) that used to be IMGUI boxes above the header.
    /// </summary>
    [InitializeOnLoad]
    internal static class YUCPInspectorChrome
    {
        static YUCPInspectorChrome() => YUCPInspector.Created += (ui, editor) =>
        {
            ui.Root.Insert(0, YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay(ui.Title));
            var notices = Notices(editor.target.GetType()).ToList();
            ui.Status(() => notices);
        };

        private static IEnumerable<YUCPInspector.Message> Notices(Type type)
        {
            if (Attribute.GetCustomAttribute(type, typeof(BetaWarningAttribute)) is BetaWarningAttribute beta)
                yield return YUCPInspector.Message.Warning(beta.Message);
            if (type.FullName.StartsWith("YUCP.Components") && Type.GetType("com.vrcfury.api.FuryComponents, com.vrcfury.api") == null)
                yield return YUCPInspector.Message.Error("Install VRCFury (vrcfury.com/download). This component needs it to build.");
        }

        /// <summary>True when the editor is built with <see cref="YUCPInspector"/>, so the IMGUI header drawers leave the notices to it.</summary>
        // ponytail: every YUCP UI Toolkit editor uses YUCPInspector; one that overrides CreateInspectorGUI without it would lose the notices.
        internal static bool DrawsNotices(UnityEditor.Editor editor) =>
            editor.GetType().GetMethod(nameof(UnityEditor.Editor.CreateInspectorGUI)).DeclaringType != typeof(UnityEditor.Editor);
    }
}
