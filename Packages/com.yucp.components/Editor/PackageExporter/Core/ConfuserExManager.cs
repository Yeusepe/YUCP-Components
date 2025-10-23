using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
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
        /// Uses synchronous download with progress feedback
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
                // Create directory
                progressCallback?.Invoke(0.1f, "Creating tools directory...");
                if (!Directory.Exists(ConfuserExDirectory))
                {
                    Directory.CreateDirectory(ConfuserExDirectory);
                }
                
                // Download with progress feedback
                progressCallback?.Invoke(0.2f, "Downloading ConfuserEx CLI...");
                string tempZipPath = Path.Combine(Path.GetTempPath(), "ConfuserEx-CLI.zip");
                
                // Delete any existing temp file (from previous failed downloads)
                if (File.Exists(tempZipPath))
                {
                    try
                    {
                        File.Delete(tempZipPath);
                        Debug.Log("[ConfuserEx] Deleted existing temp file from previous download");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ConfuserEx] Could not delete existing temp file: {ex.Message}");
                        // Generate unique temp filename to avoid sharing violation
                        tempZipPath = Path.Combine(Path.GetTempPath(), $"ConfuserEx-CLI_{Guid.NewGuid().ToString("N").Substring(0, 8)}.zip");
                        Debug.Log($"[ConfuserEx] Using alternative temp path: {tempZipPath}");
                    }
                }
                
                using (var client = new WebClient())
                {
                    // Track download progress
                    client.DownloadProgressChanged += (sender, e) =>
                    {
                        float progress = 0.2f + (e.ProgressPercentage / 100f * 0.5f);
                        progressCallback?.Invoke(progress, $"Downloading ConfuserEx: {e.ProgressPercentage}%");
                    };
                    
                    // Synchronous download - will block but with progress updates
                    client.DownloadFile(new Uri(CONFUSEREX_DOWNLOAD_URL), tempZipPath);
                }
                
                // Extract
                progressCallback?.Invoke(0.7f, "Extracting ConfuserEx...");
                ExtractZipFile(tempZipPath, ConfuserExDirectory);
                
                // Cleanup
                progressCallback?.Invoke(0.9f, "Cleaning up...");
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
                
                // Complete
                progressCallback?.Invoke(1.0f, "ConfuserEx installed successfully!");
                Debug.Log($"[ConfuserEx] Successfully installed to: {ConfuserExDirectory}");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfuserEx] Installation failed: {ex.Message}");
                progressCallback?.Invoke(0f, "Installation failed");
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
        /// Obfuscate assemblies using ConfuserEx with non-blocking progress updates
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
                progressCallback?.Invoke(0.05f, "Starting obfuscation process...");
                
                // Create temp working directory
                progressCallback?.Invoke(0.08f, "Creating temporary workspace...");
                string workingDir = Path.Combine(Path.GetTempPath(), "YUCP_Obfuscation_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDir);
                
                Debug.Log($"[ConfuserEx] Working directory: {workingDir}");
                
                // Copy DLLs to working directory with detailed progress
                progressCallback?.Invoke(0.1f, "Scanning assemblies for obfuscation...");
                var validAssemblies = new List<AssemblyObfuscationSettings>();
                int assemblyCount = 0;
                
                foreach (var assembly in assemblies)
                {
                    if (!assembly.enabled)
                        continue;
                    
                    assemblyCount++;
                    progressCallback?.Invoke(0.1f + (assemblyCount * 0.1f / assemblies.Count), $"Processing assembly {assemblyCount}/{assemblies.Count}: {assembly.assemblyName}");
                    
                    var assemblyInfo = new AssemblyScanner.AssemblyInfo(assembly.assemblyName, assembly.asmdefPath);
                    if (!assemblyInfo.exists)
                    {
                        Debug.LogWarning($"[ConfuserEx] DLL not found for assembly: {assembly.assemblyName}");
                        continue;
                    }
                    
                    string dllFileName = Path.GetFileName(assemblyInfo.dllPath);
                    string destPath = Path.Combine(workingDir, dllFileName);
                    
                    progressCallback?.Invoke(0.1f + (assemblyCount * 0.1f / assemblies.Count), $"Copying {dllFileName} to workspace...");
                    File.Copy(assemblyInfo.dllPath, destPath, true);
                    
                    validAssemblies.Add(assembly);
                    Debug.Log($"[ConfuserEx] Copied DLL: {dllFileName}");
                }
                
                // Copy all dependency DLLs from ScriptAssemblies (ConfuserEx needs them for resolution)
                progressCallback?.Invoke(0.2f, "Copying dependency DLLs for resolution...");
                int copiedDeps = 0;
                
                // 1. Copy from ScriptAssemblies
                string scriptAssembliesPath = Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies");
                if (Directory.Exists(scriptAssembliesPath))
                {
                    string[] allDlls = Directory.GetFiles(scriptAssembliesPath, "*.dll", SearchOption.TopDirectoryOnly);
                    
                    foreach (string dllPath in allDlls)
                    {
                        string dllFileName = Path.GetFileName(dllPath);
                        string destPath = Path.Combine(workingDir, dllFileName);
                        
                        // Skip if already copied (the assembly being obfuscated)
                        if (File.Exists(destPath))
                            continue;
                        
                        try
                        {
                            File.Copy(dllPath, destPath, true);
                            copiedDeps++;
                        }
                        catch
                        {
                            // Ignore copy errors for dependency DLLs (might be locked)
                        }
                    }
                }
                
                // 2. Copy from PackageCache (for Unity packages like Newtonsoft.Json)
                string packageCachePath = Path.Combine(Application.dataPath, "..", "Library", "PackageCache");
                if (Directory.Exists(packageCachePath))
                {
                    string[] packageDlls = Directory.GetFiles(packageCachePath, "*.dll", SearchOption.AllDirectories);
                    
                    foreach (string dllPath in packageDlls)
                    {
                        string dllFileName = Path.GetFileName(dllPath);
                        string destPath = Path.Combine(workingDir, dllFileName);
                        
                        // Skip if already copied
                        if (File.Exists(destPath))
                            continue;
                        
                        try
                        {
                            File.Copy(dllPath, destPath, true);
                            copiedDeps++;
                        }
                        catch
                        {
                            // Ignore copy errors for dependency DLLs
                        }
                    }
                }
                
                // 3. Copy Unity Engine DLLs (UnityEngine.CoreModule, etc.)
                string unityEditorPath = Path.GetDirectoryName(EditorApplication.applicationPath);
                string unityEnginePath = Path.Combine(unityEditorPath, "Data", "Managed", "UnityEngine");
                
                if (Directory.Exists(unityEnginePath))
                {
                    string[] unityDlls = Directory.GetFiles(unityEnginePath, "*.dll", SearchOption.TopDirectoryOnly);
                    
                    foreach (string dllPath in unityDlls)
                    {
                        string dllFileName = Path.GetFileName(dllPath);
                        string destPath = Path.Combine(workingDir, dllFileName);
                        
                        // Skip if already copied
                        if (File.Exists(destPath))
                            continue;
                        
                        try
                        {
                            File.Copy(dllPath, destPath, true);
                            copiedDeps++;
                        }
                        catch
                        {
                            // Ignore copy errors
                        }
                    }
                }
                
                // 4. Also copy from Data/Managed root (for other Unity assemblies)
                string unityManagedPath = Path.Combine(unityEditorPath, "Data", "Managed");
                if (Directory.Exists(unityManagedPath))
                {
                    string[] managedDlls = Directory.GetFiles(unityManagedPath, "*.dll", SearchOption.TopDirectoryOnly);
                    
                    foreach (string dllPath in managedDlls)
                    {
                        string dllFileName = Path.GetFileName(dllPath);
                        string destPath = Path.Combine(workingDir, dllFileName);
                        
                        // Skip if already copied
                        if (File.Exists(destPath))
                            continue;
                        
                        try
                        {
                            File.Copy(dllPath, destPath, true);
                            copiedDeps++;
                        }
                        catch
                        {
                            // Ignore copy errors
                        }
                    }
                }
                
                Debug.Log($"[ConfuserEx] Copied {copiedDeps} dependency DLLs for assembly resolution");
                
                if (validAssemblies.Count == 0)
                {
                    Debug.LogWarning("[ConfuserEx] No valid assemblies to obfuscate");
                    return false;
                }
                
                progressCallback?.Invoke(0.25f, "Generating ConfuserEx configuration file...");
                
                // Generate .crproj file
                string projectFilePath = GenerateProjectFile(validAssemblies, preset, workingDir);
                
                progressCallback?.Invoke(0.3f, $"Starting ConfuserEx obfuscation ({preset} preset)...");
                Debug.Log($"[ConfuserEx] Starting obfuscation of {validAssemblies.Count} assemblies with {preset} preset");
                
                // Run ConfuserEx with non-blocking progress updates
                bool success = RunConfuserExNonBlocking(projectFilePath, workingDir, progressCallback);
                
                if (!success)
                {
                    Debug.LogError("[ConfuserEx] Obfuscation failed");
                    return false;
                }
                
                progressCallback?.Invoke(0.85f, "Obfuscation completed! Copying obfuscated assemblies...");
                
                // Copy obfuscated DLLs back to Library/ScriptAssemblies
                string obfuscatedDir = Path.Combine(workingDir, "Obfuscated");
                if (Directory.Exists(obfuscatedDir))
                {
                    int copyCount = 0;
                    foreach (var assembly in validAssemblies)
                    {
                        copyCount++;
                        progressCallback?.Invoke(0.85f + (copyCount * 0.1f / validAssemblies.Count), $"Installing obfuscated assembly {copyCount}/{validAssemblies.Count}: {assembly.assemblyName}");
                        
                        var assemblyInfo = new AssemblyScanner.AssemblyInfo(assembly.assemblyName, assembly.asmdefPath);
                        string obfuscatedDllPath = Path.Combine(obfuscatedDir, Path.GetFileName(assemblyInfo.dllPath));
                        
                        if (File.Exists(obfuscatedDllPath))
                        {
                            // Backup original DLL
                            string backupPath = assemblyInfo.dllPath + ".backup";
                            if (!File.Exists(backupPath))
                            {
                                progressCallback?.Invoke(0.85f + (copyCount * 0.1f / validAssemblies.Count), $"Backing up original {assembly.assemblyName}...");
                                File.Copy(assemblyInfo.dllPath, backupPath, true);
                            }
                            
                            // Replace with obfuscated version
                            progressCallback?.Invoke(0.85f + (copyCount * 0.1f / validAssemblies.Count), $"Installing obfuscated {assembly.assemblyName}...");
                            File.Copy(obfuscatedDllPath, assemblyInfo.dllPath, true);
                            Debug.Log($"[ConfuserEx] Replaced DLL with obfuscated version: {assembly.assemblyName}");
                        }
                    }
                }
                
                progressCallback?.Invoke(0.98f, "Cleaning up temporary files...");
                
                // Clean up working directory
                try
                {
                    Directory.Delete(workingDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
                
                progressCallback?.Invoke(1.0f, "Obfuscation complete! All assemblies protected.");
                Debug.Log($"[ConfuserEx] Successfully obfuscated {validAssemblies.Count} assemblies");
                
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
        /// Execute ConfuserEx CLI with non-blocking progress updates
        /// </summary>
        private static bool RunConfuserExNonBlocking(string projectFilePath, string workingDirectory, Action<float, string> progressCallback)
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
                    // Read output asynchronously with progress updates
                    string output = "";
                    string error = "";
                    
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    
                    // Update progress while waiting for completion
                    int waitCount = 0;
                    while (!process.HasExited)
                    {
                        // Yield control back to Unity to prevent hanging
                        EditorApplication.delayCall += () => { };
                        
                        System.Threading.Thread.Sleep(500); // Wait 500ms
                        waitCount++;
                        
                        // Update progress every 2 seconds (every 4 iterations)
                        if (waitCount % 4 == 0)
                        {
                            float progress = 0.4f + (waitCount / 100f) * 0.4f; // Progress from 0.4 to 0.8 over time
                            progressCallback?.Invoke(progress, $"Obfuscating assemblies... ({waitCount * 0.5f:F0}s elapsed)");
                        }
                        
                        // Safety timeout - if it takes more than 5 minutes, something is wrong
                        if (waitCount > 600) // 5 minutes
                        {
                            Debug.LogError("[ConfuserEx] Process timed out after 5 minutes");
                            try
                            {
                                process.Kill();
                            }
                            catch { }
                            return false;
                        }
                    }
                    
                    // Get the output
                    output = outputTask.Result;
                    error = errorTask.Result;
                    
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
                    
                    progressCallback?.Invoke(0.8f, "Obfuscation completed successfully!");
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
