using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace YUCP.AutoFinalize
{
    [InitializeOnLoad]
    public static class YucpAutoFinalize
    {
        private const string KeyPending = "YUCP_Finalize_Pending";
        private const string KeyBaseline = "YUCP_Finalize_BaselineUtcTicks";

        static YucpAutoFinalize()
        {
            if (SessionState.GetBool(KeyPending, false))
                EditorApplication.update += Tick;
        }

        public static void Arm()
        {
            var asmDir = Path.Combine("Library", "ScriptAssemblies");
            var latest = Directory.Exists(asmDir)
                ? Directory.GetFiles(asmDir, "*.dll")
                         .Select(File.GetLastWriteTimeUtc)
                         .DefaultIfEmpty(DateTime.MinValue).Max()
                : DateTime.MinValue;

            SessionState.SetString(KeyBaseline, latest.Ticks.ToString());
            SessionState.SetBool(KeyPending, true);

            Debug.Log("[YucpAutoFinalize] Armed - waiting for compilation to complete...");

            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
            EditorUtility.RequestScriptReload();
        }

        private static void Tick()
        {
            var asmDir = Path.Combine("Library", "ScriptAssemblies");
            if (!Directory.Exists(asmDir)) return;

            long baselineTicks = long.TryParse(SessionState.GetString(KeyBaseline, "0"), out var t) ? t : 0;
            long latestTicks = Directory.GetFiles(asmDir, "*.dll")
                                        .Select(File.GetLastWriteTimeUtc)
                                        .DefaultIfEmpty(DateTime.MinValue).Max().Ticks;

            if (latestTicks > baselineTicks)
            {
                Debug.Log("[YucpAutoFinalize] New assemblies detected - refreshing editor views...");

                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                TryClearConsole();

                SessionState.EraseString(KeyBaseline);
                SessionState.EraseBool(KeyPending);
                EditorApplication.update -= Tick;

                Debug.Log("[YucpAutoFinalize] Finalization complete!");
            }
        }

        private static void TryClearConsole()
        {
            try
            {
                var editorAsm = typeof(UnityEditor.SceneView).Assembly;
                var logEntries = editorAsm.GetType("UnityEditor.LogEntries");
                var clear = logEntries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
                clear?.Invoke(null, null);
                Debug.Log("[YucpAutoFinalize] Console cleared successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YucpAutoFinalize] Could not clear console: {ex.Message}");
            }
        }
    }
}

