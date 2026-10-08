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


        private const string ZipUrl = "https://github.com/Yeusepe/YUCP-Components/archive/60549e5c697ce1fa5c6cefad7f02f842ddc4eb84.zip";
        private const string ZipRootFolderName = "YUCP-Components-60549e5c697ce1fa5c6cefad7f02f842ddc4eb84";

        static PluginsDownloader()
        {

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



                if (Directory.Exists(pluginsPath))
                {

                    bool hasContent = Directory.GetFiles(pluginsPath, "*", SearchOption.AllDirectories).Length > 0 ||
                                      Directory.GetDirectories(pluginsPath, "*", SearchOption.AllDirectories).Length > 0;

                    if (hasContent)
                    {
                        return;
                    }
                }


                DownloadAndInstallPlugins(pluginsPath);
            }
            catch
            {

            }
        }

        private static void DownloadAndInstallPlugins(string destinationPluginsPath)
        {

            string tempRoot = Path.Combine(Path.GetTempPath(), "YUCP");
            string zipPath = Path.Combine(tempRoot, "repo.zip");

            try
            {
                Directory.CreateDirectory(tempRoot);

                using (var client = new WebClient())
                {
                    client.DownloadFile(ZipUrl, zipPath);
                }


                string pluginsPrefix = ZipRootFolderName + "/Plugins/";

                using (var fileStream = File.OpenRead(zipPath))
                using (var zipInputStream = new ZipInputStream(fileStream))
                {
                    ZipEntry entry;
                    while ((entry = zipInputStream.GetNextEntry()) != null)
                    {
                        if (entry.IsFile && entry.Name.StartsWith(pluginsPrefix, StringComparison.OrdinalIgnoreCase))
                        {

                            string relativePath = entry.Name.Substring(pluginsPrefix.Length);
                            string targetPath = Path.Combine(destinationPluginsPath, relativePath);


                            string targetDir = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }


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

                }
            }
        }

    }
}

































