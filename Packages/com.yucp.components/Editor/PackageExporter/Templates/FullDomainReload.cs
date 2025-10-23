using System;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace YUCP.DirectVpmInstaller
{
    [InitializeOnLoad]
    public static class FullDomainReload
    {
        private static Action _onReloaded;

        static FullDomainReload()
        {
            AssemblyReloadEvents.afterAssemblyReload += AfterAssemblyReload;
        }

        public static void Run(Action onReloaded = null)
        {
            EditorApplication.delayCall += () => Begin(onReloaded);
        }

        private static void Begin(Action onReloaded)
        {
            _onReloaded = onReloaded;

            try 
            { 
                EditorApplication.UnlockReloadAssemblies(); 
            } 
            catch 
            { 
                // Ignore if already unlocked
            }

            try
            {
                // Client.Resolve() returns void and works synchronously for manifest changes
                Client.Resolve();
                
                // Give Package Manager a moment to process the resolve
                EditorApplication.delayCall += ForceRefreshThenCompile;
            }
            catch
            {
                ForceRefreshThenCompile();
            }
        }

        private static void ForceRefreshThenCompile()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);

            EditorUtility.RequestScriptReload();
        }

        private static void AfterAssemblyReload()
        {
            var done = _onReloaded;
            _onReloaded = null;
            
            if (done != null)
            {
                EditorApplication.delayCall += () => done.Invoke();
            }
        }
    }
}


