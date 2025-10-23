using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Test script to identify what's causing Unity to hang during ConfuserEx operations
    /// </summary>
    public static class ConfuserExTest
    {
        private const string CONFUSEREX_DOWNLOAD_URL = "https://github.com/mkaring/ConfuserEx/releases/download/v1.6.0/ConfuserEx-CLI.zip";
        
        [MenuItem("YUCP/Test ConfuserEx Download")]
        public static void TestDownload()
        {
            Debug.Log("[ConfuserExTest] Starting download test...");
            
            EditorApplication.delayCall += () => {
                TestDownloadInternal();
            };
        }
        
        [MenuItem("YUCP/Test ConfuserEx Manager")]
        public static void TestConfuserExManager()
        {
            Debug.Log("[ConfuserExTest] Testing ConfuserExManager.EnsureInstalled...");
            
            bool result = ConfuserExManager.EnsureInstalled((progress, status) =>
            {
                Debug.Log($"[ConfuserExTest] Progress: {progress:P0} - {status}");
            });
            
            Debug.Log($"[ConfuserExTest] EnsureInstalled returned: {result}");
        }
        
        private static void TestDownloadInternal()
        {
            try
            {
                Debug.Log("[ConfuserExTest] Creating temp directory...");
                string tempDir = Path.Combine(Path.GetTempPath(), "ConfuserExTest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                
                string tempZipPath = Path.Combine(tempDir, "ConfuserEx-CLI.zip");
                
                Debug.Log("[ConfuserExTest] Starting download...");
                
                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (sender, e) =>
                    {
                        Debug.Log($"[ConfuserExTest] Download progress: {e.ProgressPercentage}%");
                    };
                    
                    // Test 1: Try the original blocking approach
                    Debug.Log("[ConfuserExTest] Testing original blocking approach...");
                    var startTime = DateTime.Now;
                    
                    try
                    {
                        client.DownloadFileTaskAsync(CONFUSEREX_DOWNLOAD_URL, tempZipPath).Wait();
                        var elapsed = DateTime.Now - startTime;
                        Debug.Log($"[ConfuserExTest] Original approach completed in {elapsed.TotalSeconds:F2}s");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ConfuserExTest] Original approach failed: {ex.Message}");
                    }
                }
                
                // Test 2: Try non-blocking approach
                Debug.Log("[ConfuserExTest] Testing non-blocking approach...");
                TestNonBlockingDownload(tempZipPath);
                
                // Cleanup
                try
                {
                    if (File.Exists(tempZipPath))
                        File.Delete(tempZipPath);
                    Directory.Delete(tempDir);
                }
                catch { }
                
                Debug.Log("[ConfuserExTest] Test completed!");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfuserExTest] Test failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }
        
        private static void TestNonBlockingDownload(string tempZipPath)
        {
            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += (sender, e) =>
                {
                    Debug.Log($"[ConfuserExTest] Non-blocking progress: {e.ProgressPercentage}%");
                };
                
                var downloadTask = client.DownloadFileTaskAsync(CONFUSEREX_DOWNLOAD_URL, tempZipPath);
                var startTime = DateTime.Now;
                
                // Wait with yielding
                while (!downloadTask.IsCompleted)
                {
                    EditorApplication.delayCall += () => { };
                    System.Threading.Thread.Sleep(100);
                    
                    var elapsed = DateTime.Now - startTime;
                    if (elapsed.TotalSeconds > 30) // 30 second timeout
                    {
                        Debug.LogWarning("[ConfuserExTest] Non-blocking approach timed out after 30s");
                        break;
                    }
                }
                
                var totalElapsed = DateTime.Now - startTime;
                Debug.Log($"[ConfuserExTest] Non-blocking approach completed in {totalElapsed.TotalSeconds:F2}s");
                
                if (downloadTask.IsFaulted)
                {
                    Debug.LogError($"[ConfuserExTest] Non-blocking approach failed: {downloadTask.Exception?.GetBaseException()?.Message}");
                }
            }
        }
        
        [MenuItem("YUCP/Test ConfuserEx Process")]
        public static void TestProcess()
        {
            Debug.Log("[ConfuserExTest] Starting process test...");
            
            EditorApplication.delayCall += () => {
                TestProcessInternal();
            };
        }
        
        private static void TestProcessInternal()
        {
            try
            {
                Debug.Log("[ConfuserExTest] Testing process creation...");
                
                // Test 1: Simple process
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c echo Test process",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                Debug.Log("[ConfuserExTest] Creating test process...");
                using (var process = Process.Start(startInfo))
                {
                    Debug.Log("[ConfuserExTest] Waiting for process...");
                    
                    // Test blocking wait
                    var startTime = DateTime.Now;
                    process.WaitForExit();
                    var elapsed = DateTime.Now - startTime;
                    
                    Debug.Log($"[ConfuserExTest] Process completed in {elapsed.TotalSeconds:F2}s");
                    Debug.Log($"[ConfuserExTest] Process output: {process.StandardOutput.ReadToEnd()}");
                }
                
                Debug.Log("[ConfuserExTest] Process test completed!");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfuserExTest] Process test failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }
        
        [MenuItem("YUCP/Test File Operations")]
        public static void TestFileOperations()
        {
            Debug.Log("[ConfuserExTest] Starting file operations test...");
            
            EditorApplication.delayCall += () => {
                TestFileOperationsInternal();
            };
        }
        
        private static void TestFileOperationsInternal()
        {
            try
            {
                Debug.Log("[ConfuserExTest] Testing file operations...");
                
                string testDir = Path.Combine(Path.GetTempPath(), "ConfuserExFileTest");
                Directory.CreateDirectory(testDir);
                
                // Test file copy
                string sourceFile = Path.Combine(testDir, "test.txt");
                string destFile = Path.Combine(testDir, "test_copy.txt");
                
                File.WriteAllText(sourceFile, "Test content");
                
                Debug.Log("[ConfuserExTest] Testing file copy...");
                var startTime = DateTime.Now;
                
                File.Copy(sourceFile, destFile, true);
                
                var elapsed = DateTime.Now - startTime;
                Debug.Log($"[ConfuserExTest] File copy completed in {elapsed.TotalSeconds:F2}s");
                
                // Cleanup
                File.Delete(sourceFile);
                File.Delete(destFile);
                Directory.Delete(testDir);
                
                Debug.Log("[ConfuserExTest] File operations test completed!");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfuserExTest] File operations test failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }
    }
}
