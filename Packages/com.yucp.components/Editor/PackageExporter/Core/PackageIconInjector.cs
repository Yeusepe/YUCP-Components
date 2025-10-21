using System;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using UnityEngine;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Injects custom icons into Unity packages (.unitypackage files).
    /// Based on the Unitypackage-icon-creator tool but refactored for programmatic use.
    /// </summary>
    public static class PackageIconInjector
    {
        /// <summary>
        /// Add a PNG icon to an existing Unity package
        /// </summary>
        public static bool AddIconToPackage(string packagePath, string iconPath, string outputPath)
        {
            if (!File.Exists(packagePath))
            {
                Debug.LogError($"[PackageIconInjector] Package file not found: {packagePath}");
                return false;
            }
            
            if (!File.Exists(iconPath))
            {
                Debug.LogError($"[PackageIconInjector] Icon file not found: {iconPath}");
                return false;
            }
            
            if (Path.GetExtension(iconPath).ToLower() != ".png")
            {
                Debug.LogError($"[PackageIconInjector] Icon must be a PNG file: {iconPath}");
                return false;
            }
            
            string tempDir = GetTemporaryDirectory();
            
            try
            {
                Debug.Log($"[PackageIconInjector] Extracting package to: {tempDir}");
                
                // Extract the package
                ExtractTarGz(packagePath, tempDir);
                
                Debug.Log($"[PackageIconInjector] Adding icon: {iconPath}");
                
                // Create new package with icon
                CreateTarGzWithIcon(outputPath, tempDir, iconPath);
                
                Debug.Log($"[PackageIconInjector] Package with icon created: {outputPath}");
                
                // Cleanup
                Directory.Delete(tempDir, true);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageIconInjector] Failed to add icon: {ex.Message}");
                
                // Cleanup on error
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                
                return false;
            }
        }
        
        /// <summary>
        /// Create a temporary directory for package manipulation
        /// </summary>
        private static string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "YUCP_PackageIcon_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }
        
        /// <summary>
        /// Extract a .unitypackage (tar.gz format) to a directory
        /// </summary>
        private static void ExtractTarGz(string tarGzPath, string destFolder)
        {
            using (Stream inStream = File.OpenRead(tarGzPath))
            using (Stream gzipStream = new GZipInputStream(inStream))
            {
                var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
                tarArchive.ExtractContents(destFolder);
                tarArchive.Close();
            }
        }
        
        /// <summary>
        /// Create a .unitypackage (tar.gz format) with an embedded icon
        /// </summary>
        private static void CreateTarGzWithIcon(string outputPath, string sourceDirectory, string iconFilePath)
        {
            using (Stream outStream = File.Create(outputPath))
            using (Stream gzipStream = new GZipOutputStream(outStream))
            {
                var tarArchive = TarArchive.CreateOutputTarArchive(gzipStream);
                
                // Set root path (case sensitive, must use forward slashes, must not end with slash)
                tarArchive.RootPath = sourceDirectory.Replace('\\', '/');
                if (tarArchive.RootPath.EndsWith("/"))
                {
                    tarArchive.RootPath = tarArchive.RootPath.Remove(tarArchive.RootPath.Length - 1);
                }
                
                // Add the icon file first
                var iconFileName = ".icon.png";
                var iconTarEntry = TarEntry.CreateEntryFromFile(iconFilePath);
                iconTarEntry.Name = iconFileName;
                iconTarEntry.TarHeader.TypeFlag = TarHeader.LF_NORMAL;
                tarArchive.WriteEntry(iconTarEntry, false);
                
                Debug.Log($"[PackageIconInjector] Added icon to package: {iconFileName}");
                
                // Add all files from source directory
                var filenames = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
                foreach (var filename in filenames)
                {
                    var relativePath = filename.Substring(sourceDirectory.Length);
                    if (relativePath.StartsWith("\\") || relativePath.StartsWith("/"))
                    {
                        relativePath = relativePath.Substring(1);
                    }
                    relativePath = relativePath.Replace('\\', '/');
                    
                    var tarEntry = TarEntry.CreateEntryFromFile(filename);
                    tarEntry.Name = relativePath;
                    tarArchive.WriteEntry(tarEntry, true);
                }
                
                tarArchive.Close();
            }
        }
    }
}
