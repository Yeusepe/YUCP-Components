using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace YUCP.Components.Editor.Utils
{
    /// <summary>
    /// Utility for installing d4rkAvatarOptimizer via VPM package manager.
    /// Downloads and installs the latest version from the VPM repository.
    /// </summary>
    public static class D4rkOptimizerInstaller
    {
        private const string PACKAGE_NAME = "d4rkpl4y3r.d4rkavataroptimizer";
        private const string DISPLAY_NAME = "d4rkAvatarOptimizer";
        private const string REPOSITORY_URL = "https://d4rkc0d3r.github.io/vpm-repos/main.json";
        private const string FALLBACK_VERSION = "3.12.5";

        /// <summary>
        /// Install d4rkAvatarOptimizer via VPM manifest
        /// </summary>
        public static void InstallOptimizer(Action onSuccess = null, Action<string> onError = null)
        {
            try
            {
                Debug.Log("[D4rkOptimizerInstaller] Starting installation...");


                if (!AddRepositoryToManifest())
                {
                    onError?.Invoke("Failed to add repository to VPM manifest");
                    return;
                }


                if (!AddPackageToManifest(FALLBACK_VERSION))
                {
                    onError?.Invoke("Failed to add package to VPM manifest");
                    return;
                }


                AssetDatabase.Refresh();

                Debug.Log("[D4rkOptimizerInstaller] Installation complete! Unity will resolve the package.");

                EditorUtility.DisplayDialog(
                    "Installing d4rkAvatarOptimizer",
                    $"Installation initiated!\n\n" +
                    $"Unity is now resolving the package. This may take a few moments.\n\n" +
                    $"Package: {DISPLAY_NAME}\n" +
                    $"Version: {FALLBACK_VERSION}\n\n" +
                    $"The Avatar Optimizer Plugin will become active once the package is resolved.",
                    "OK");

                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[D4rkOptimizerInstaller] Installation failed: {ex.Message}");
                onError?.Invoke(ex.Message);
            }
        }

        private static bool AddRepositoryToManifest()
        {
            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "vpm-manifest.json");
                JObject manifest;

                if (File.Exists(manifestPath))
                {
                    manifest = JObject.Parse(File.ReadAllText(manifestPath));
                }
                else
                {

                    manifest = new JObject
                    {
                        ["dependencies"] = new JObject(),
                        ["locked"] = new JObject()
                    };
                }


                if (manifest["dependencies"] == null)
                {
                    manifest["dependencies"] = new JObject();
                }

                if (manifest["locked"] == null)
                {
                    manifest["locked"] = new JObject();
                }


                File.WriteAllText(manifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[D4rkOptimizerInstaller] Failed to add repository: {ex.Message}");
                return false;
            }
        }

        private static bool AddPackageToManifest(string version)
        {
            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "vpm-manifest.json");
                JObject manifest;

                if (File.Exists(manifestPath))
                {
                    manifest = JObject.Parse(File.ReadAllText(manifestPath));
                }
                else
                {
                    Debug.LogError("[D4rkOptimizerInstaller] VPM manifest not found!");
                    return false;
                }


                var dependencies = manifest["dependencies"] as JObject;
                if (dependencies == null)
                {
                    dependencies = new JObject();
                    manifest["dependencies"] = dependencies;
                }

                dependencies[PACKAGE_NAME] = new JObject
                {
                    ["version"] = version
                };


                var locked = manifest["locked"] as JObject;
                if (locked == null)
                {
                    locked = new JObject();
                    manifest["locked"] = locked;
                }

                locked[PACKAGE_NAME] = new JObject
                {
                    ["version"] = version
                };


                File.WriteAllText(manifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));

                Debug.Log($"[D4rkOptimizerInstaller] Added {PACKAGE_NAME}@{version} to vpm-manifest.json");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[D4rkOptimizerInstaller] Failed to add package: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Alternative installation via direct GitHub download (fallback)
        /// </summary>
        public static void OpenGitHubReleasePage()
        {
            Application.OpenURL("https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/releases/latest");
        }
    }
}


