using System;
using System.IO;
using System.Net;
using ICSharpCode.SharpZipLib.Zip;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.Utils
{
    /// <summary>
    /// Ensures that the project-level <root>/Plugins folder exists.
    /// If it does not, downloads the Plugins folder content from the
    /// YUCP-Components GitHub repository and installs it into the
    /// Unity project root (next to the Assets folder).
    /// </summary>
    [InitializeOnLoad]
    public static class PluginsDownloader
    {
        // Commit and URL taken from
        // https://github.com/Yeusepe/YUCP-Components/tree/60549e5c697ce1fa5c6cefad7f02f842ddc4eb84/Plugins
        private const string ZipUrl = "https://github.com/Yeusepe/YUCP-Components/archive/60549e5c697ce1fa5c6cefad7f02f842ddc4eb84.zip";
        private const string ZipRootFolderName = "YUCP-Components-60549e5c697ce1fa5c6cefad7f02f842ddc4eb84";

        static PluginsDownloader()
        {
            // Run once after the editor domain reloads
            EditorApplication.update += RunOnceOnLoad;
        }

        private static void RunOnceOnLoad()
        {
            EditorApplication.update -= RunOnceOnLoad;
            TryEnsurePluginsFolder();
        }

        /// <summary>
        /// Checks if <project-root>/Plugins exists, and if not, downloads
        /// and installs the Plugins folder from the GitHub snapshot.
        /// </summary>
        private static void TryEnsurePluginsFolder()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string pluginsPath = Path.Combine(projectRoot, "Plugins");

                // Respect existing installations: only act if the folder is missing.
                // Check both directory existence and that it's not empty (has at least one file or subdirectory)
                if (Directory.Exists(pluginsPath))
                {
                    // Additional check: ensure it's not an empty directory
                    bool hasContent = Directory.GetFiles(pluginsPath, "*", SearchOption.AllDirectories).Length > 0 ||
                                      Directory.GetDirectories(pluginsPath, "*", SearchOption.AllDirectories).Length > 0;
                    
                    if (hasContent)
                    {
                        return;
                    }
                }

                // Automatically download and install without prompting
                DownloadAndInstallPlugins(pluginsPath);
            }
            catch
            {
                // Silently fail - user can manually install if needed
            }
        }

        private static void DownloadAndInstallPlugins(string destinationPluginsPath)
        {
            // Use shorter temp path to avoid Windows 260 character path limit
            string tempRoot = Path.Combine(Path.GetTempPath(), "YUCP");
            string zipPath = Path.Combine(tempRoot, "repo.zip");

            try
            {
                Directory.CreateDirectory(tempRoot);

                using (var client = new WebClient())
                {
                    client.DownloadFile(ZipUrl, zipPath);
                }

                // Extract only Plugins folder entries directly to destination to avoid long paths
                string pluginsPrefix = ZipRootFolderName + "/Plugins/";
                
                using (var fileStream = File.OpenRead(zipPath))
                using (var zipInputStream = new ZipInputStream(fileStream))
                {
                    ZipEntry entry;
                    while ((entry = zipInputStream.GetNextEntry()) != null)
                    {
                        if (entry.IsFile && entry.Name.StartsWith(pluginsPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            // Get relative path within Plugins folder
                            string relativePath = entry.Name.Substring(pluginsPrefix.Length);
                            string targetPath = Path.Combine(destinationPluginsPath, relativePath);
                            
                            // Create directory if needed
                            string targetDir = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }
                            
                            // Extract file
                            using (var outputStream = File.Create(targetPath))
                            {
                                zipInputStream.CopyTo(outputStream);
                            }
                        }
                    }
                }

                AssetDatabase.Refresh();
            }
            catch
            {
                // Silently fail - user can manually install if needed
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, true);
                    }
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = filePath.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string targetFilePath = Path.Combine(destinationDir, relativePath);

                string targetDir = Path.GetDirectoryName(targetFilePath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(filePath, targetFilePath, overwrite: true);
            }
        }
    }
}
























