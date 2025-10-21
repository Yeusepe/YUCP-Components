using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
using ICSharpCode.SharpZipLib.Zip;
#endif
using Debug = UnityEngine.Debug;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Manages ConfuserEx CLI download, configuration, and execution.
    /// Handles automatic downloading from GitHub and running obfuscation on assemblies.
    /// </summary>
    public static class ConfuserExManager
    {
        private const string CONFUSEREX_VERSION = "1.6.0";
        private const string CONFUSEREX_DOWNLOAD_URL = "https://github.com/mkaring/ConfuserEx/releases/download/v1.6.0/ConfuserEx-CLI.zip";
        
        private static string ToolsDirectory => Path.Combine(Application.dataPath, "..", "Packages", "com.yucp.components", "Tools");
        private static string ConfuserExDirectory => Path.Combine(ToolsDirectory, "ConfuserEx");
        private static string ConfuserExCliPath => Path.Combine(ConfuserExDirectory, "Confuser.CLI.exe");
        
        /// <summary>
        /// Check if ConfuserEx is installed and ready to use
        /// </summary>
        public static bool IsInstalled()
        {
            return File.Exists(ConfuserExCliPath);
        }
        
        /// <summary>
        /// Download and install ConfuserEx CLI if not already present
        /// </summary>
        public static bool EnsureInstalled(Action<float, string> progressCallback = null)
        {
            if (IsInstalled())
            {
                Debug.Log("[ConfuserEx] Already installed at: " + ConfuserExCliPath);
                return true;
            }
            
            Debug.Log("[ConfuserEx] Not found. Downloading from GitHub...");
            
            try
            {
                progressCallback?.Invoke(0.1f, "Creating tools directory...");
                
                // Create tools directory if it doesn't exist
                if (!Directory.Exists(ConfuserExDirectory))
                {
                    Directory.CreateDirectory(ConfuserExDirectory);
                }
                
                progressCallback?.Invoke(0.2f, "Downloading ConfuserEx CLI...");
                
                // Download the ZIP file
                string tempZipPath = Path.Combine(Path.GetTempPath(), "ConfuserEx-CLI.zip");
                
                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (sender, e) =>
                    {
                        float progress = 0.2f + (e.ProgressPercentage / 100f * 0.5f);
                        progressCallback?.Invoke(progress, $"Downloading ConfuserEx: {e.ProgressPercentage}%");
                    };
                    
                    client.DownloadFileTaskAsync(CONFUSEREX_DOWNLOAD_URL, tempZipPath).Wait();
                }
                
                progressCallback?.Invoke(0.7f, "Extracting ConfuserEx...");
                
                // Extract the ZIP file
                ExtractZipFile(tempZipPath, ConfuserExDirectory);
                
                progressCallback?.Invoke(0.9f, "Cleaning up...");
                
                // Clean up temp file
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
                
                progressCallback?.Invoke(1.0f, "ConfuserEx installed successfully!");
                
                Debug.Log($"[ConfuserEx] Successfully installed to: {ConfuserExDirectory}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfuserEx] Failed to download/install: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Extract ZIP file to target directory
        /// </summary>
        private static void ExtractZipFile(string zipPath, string extractPath)
        {
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
            using (ZipInputStream zipStream = new ZipInputStream(File.OpenRead(zipPath)))
            {
                ZipEntry entry;
                while ((entry = zipStream.GetNextEntry()) != null)
                {
                    string entryPath = Path.Combine(extractPath, entry.Name);
                    string directoryName = Path.GetDirectoryName(entryPath);
                    
                    if (!string.IsNullOrEmpty(directoryName))
                    {
                        Directory.CreateDirectory(directoryName);
                    }
                    
                    if (!entry.IsDirectory && !string.IsNullOrEmpty(entry.Name))
                    {
                        using (FileStream streamWriter = File.Create(entryPath))
                        {
                            byte[] buffer = new byte[4096];
                            int bytesRead;
                            while ((bytesRead = zipStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                streamWriter.Write(buffer, 0, bytesRead);
                            }
                        }
                    }
                }
            }
#else
            Debug.LogError("[ConfuserExManager] ICSharpCode.SharpZipLib not available. Please install the ICSharpCode.SharpZipLib package.");
#endif
        }
        
        /// <summary>
        /// Generate a ConfuserEx project file (.crproj) for the specified assemblies
        /// </summary>
        public static string GenerateProjectFile(
            List<AssemblyObfuscationSettings> assemblies,
            ConfuserExPreset preset,
            string workingDirectory)
        {
            string projectFilePath = Path.Combine(workingDirectory, "obfuscation.crproj");
            
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<project baseDir=\".\" outputDir=\"./Obfuscated\" xmlns=\"http://confuser.codeplex.com\">");
            sb.AppendLine("  <!-- Generated by YUCP Package Exporter -->");
            sb.AppendLine("  ");
            sb.AppendLine("  <rule pattern=\"true\" inherit=\"false\">");
            
            // Add protection rules based on preset
            string protectionRules = ConfuserExPresetGenerator.GenerateProtectionRules(preset);
            sb.AppendLine(protectionRules);
            
            sb.AppendLine("  </rule>");
            sb.AppendLine("  ");
            
            // Add module entries for each assembly
            foreach (var assembly in assemblies)
            {
                if (!assembly.enabled)
                    continue;
                
                var assemblyInfo = new AssemblyScanner.AssemblyInfo(assembly.assemblyName, assembly.asmdefPath);
                if (assemblyInfo.exists)
                {
                    // Use relative path from working directory
                    string relativePath = Path.GetFileName(assemblyInfo.dllPath);
                    sb.AppendLine($"  <module path=\"{relativePath}\" />");
                }
            }
            
            sb.AppendLine("</project>");
            
            File.WriteAllText(projectFilePath, sb.ToString());
            Debug.Log($"[ConfuserEx] Generated project file: {projectFilePath}");
            
            return projectFilePath;
        }
        
        /// <summary>
        /// Obfuscate assemblies using ConfuserEx
        /// </summary>
        public static bool ObfuscateAssemblies(
            List<AssemblyObfuscationSettings> assemblies,
            ConfuserExPreset preset,
            Action<float, string> progressCallback = null)
        {
            if (!IsInstalled())
            {
                Debug.LogError("[ConfuserEx] ConfuserEx CLI is not installed. Cannot obfuscate.");
                return false;
            }
            
            try
            {
                progressCallback?.Invoke(0.1f, "Preparing assemblies for obfuscation...");
                
                // Create temp working directory
                string workingDir = Path.Combine(Path.GetTempPath(), "YUCP_Obfuscation_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDir);
                
                Debug.Log($"[ConfuserEx] Working directory: {workingDir}");
                
                // Copy DLLs to working directory
                var validAssemblies = new List<AssemblyObfuscationSettings>();
                foreach (var assembly in assemblies)
                {
                    if (!assembly.enabled)
                        continue;
                    
                    var assemblyInfo = new AssemblyScanner.AssemblyInfo(assembly.assemblyName, assembly.asmdefPath);
                    if (!assemblyInfo.exists)
                    {
                        Debug.LogWarning($"[ConfuserEx] DLL not found for assembly: {assembly.assemblyName}");
                        continue;
                    }
                    
                    string dllFileName = Path.GetFileName(assemblyInfo.dllPath);
                    string destPath = Path.Combine(workingDir, dllFileName);
                    File.Copy(assemblyInfo.dllPath, destPath, true);
                    
                    validAssemblies.Add(assembly);
                    Debug.Log($"[ConfuserEx] Copied DLL: {dllFileName}");
                }
                
                if (validAssemblies.Count == 0)
                {
                    Debug.LogWarning("[ConfuserEx] No valid assemblies to obfuscate");
                    return false;
                }
                
                progressCallback?.Invoke(0.3f, "Generating ConfuserEx configuration...");
                
                // Generate .crproj file
                string projectFilePath = GenerateProjectFile(validAssemblies, preset, workingDir);
                
                progressCallback?.Invoke(0.4f, $"Running ConfuserEx ({preset} preset)...");
                
                // Run ConfuserEx
                bool success = RunConfuserEx(projectFilePath, workingDir, progressCallback);
                
                if (!success)
                {
                    Debug.LogError("[ConfuserEx] Obfuscation failed");
                    return false;
                }
                
                progressCallback?.Invoke(0.9f, "Copying obfuscated assemblies...");
                
                // Copy obfuscated DLLs back to Library/ScriptAssemblies
                string obfuscatedDir = Path.Combine(workingDir, "Obfuscated");
                if (Directory.Exists(obfuscatedDir))
                {
                    foreach (var assembly in validAssemblies)
                    {
                        var assemblyInfo = new AssemblyScanner.AssemblyInfo(assembly.assemblyName, assembly.asmdefPath);
                        string obfuscatedDllPath = Path.Combine(obfuscatedDir, Path.GetFileName(assemblyInfo.dllPath));
                        
                        if (File.Exists(obfuscatedDllPath))
                        {
                            // Backup original DLL
                            string backupPath = assemblyInfo.dllPath + ".backup";
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(assemblyInfo.dllPath, backupPath, true);
                            }
                            
                            // Replace with obfuscated version
                            File.Copy(obfuscatedDllPath, assemblyInfo.dllPath, true);
                            Debug.Log($"[ConfuserEx] Replaced DLL with obfuscated version: {assembly.assemblyName}");
                        }
                    }
                }
                
                progressCallback?.Invoke(1.0f, "Obfuscation complete!");
                
                // Clean up working directory
                try
                {
                    Directory.Delete(workingDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfuserEx] Obfuscation failed: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }
        
        /// <summary>
        /// Execute ConfuserEx CLI
        /// </summary>
        private static bool RunConfuserEx(string projectFilePath, string workingDirectory, Action<float, string> progressCallback)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ConfuserExCliPath,
                    Arguments = $"\"{projectFilePath}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                Debug.Log($"[ConfuserEx] Executing: {startInfo.FileName} {startInfo.Arguments}");
                
                using (Process process = Process.Start(startInfo))
                {
                    // Read output
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    process.WaitForExit();
                    
                    if (!string.IsNullOrEmpty(output))
                    {
                        Debug.Log($"[ConfuserEx] Output:\n{output}");
                    }
                    
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogWarning($"[ConfuserEx] Errors:\n{error}");
                    }
                    
                    if (process.ExitCode != 0)
                    {
                        Debug.LogError($"[ConfuserEx] Process exited with code {process.ExitCode}");
                        return false;
                    }
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfuserEx] Failed to run ConfuserEx: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Restore original DLLs from backups (in case obfuscation needs to be undone)
        /// </summary>
        public static void RestoreOriginalDlls(List<AssemblyObfuscationSettings> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                if (!assembly.enabled)
                    continue;
                
                var assemblyInfo = new AssemblyScanner.AssemblyInfo(assembly.assemblyName, assembly.asmdefPath);
                string backupPath = assemblyInfo.dllPath + ".backup";
                
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, assemblyInfo.dllPath, true);
                    File.Delete(backupPath);
                    Debug.Log($"[ConfuserEx] Restored original DLL: {assembly.assemblyName}");
                }
            }
        }
        
        /// <summary>
        /// Get the installation status and version info
        /// </summary>
        public static string GetStatusInfo()
        {
            if (IsInstalled())
            {
                return $"ConfuserEx v{CONFUSEREX_VERSION} installed at:\n{ConfuserExCliPath}";
            }
            else
            {
                return "ConfuserEx not installed. Will download automatically when needed.";
            }
        }
    }
}
