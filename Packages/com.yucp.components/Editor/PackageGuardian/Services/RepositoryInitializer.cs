using System;
using System.IO;
using UnityEngine;
using PackageGuardian.Core.Repository;
using PackageGuardian.Core.Storage;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    /// <summary>
    /// Initializes Package Guardian repository in Unity project.
    /// </summary>
    public static class RepositoryInitializer
    {
        /// <summary>
        /// Check if repository is initialized.
        /// </summary>
        public static bool IsInitialized(string projectRoot)
        {
            string pgDir = Path.Combine(projectRoot, ".pg");
            return Directory.Exists(pgDir);
        }
        
        /// <summary>
        /// Initialize a new repository in the Unity project.
        /// </summary>
        public static void Initialize(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentNullException(nameof(projectRoot));
            
            if (!Directory.Exists(projectRoot))
                throw new DirectoryNotFoundException($"Project root not found: {projectRoot}");
            
            string pgDir = Path.Combine(projectRoot, ".pg");
            
            if (Directory.Exists(pgDir))
            {
                Debug.Log("[Package Guardian] Repository already initialized");
                return;
            }
            
            Debug.Log("[Package Guardian] Initializing repository...");
            
            // Create directory structure
            Directory.CreateDirectory(pgDir);
            Directory.CreateDirectory(Path.Combine(pgDir, "objects"));
            Directory.CreateDirectory(Path.Combine(pgDir, "refs", "heads"));
            Directory.CreateDirectory(Path.Combine(pgDir, "refs", "stash", "auto"));
            Directory.CreateDirectory(Path.Combine(pgDir, "refs", "tags"));
            
            // Create HEAD pointing to main branch
            string headPath = Path.Combine(pgDir, "HEAD");
            File.WriteAllText(headPath, "ref: refs/heads/main\n");
            
            // Create empty index
            string indexPath = Path.Combine(pgDir, "index.json");
            File.WriteAllText(indexPath, "[]\n");
            
            // Create config file
            string configPath = Path.Combine(pgDir, "config.json");
            File.WriteAllText(configPath, "{\n  \"version\": \"1.0\"\n}\n");
            
            // Create default .pgignore if it doesn't exist
            string pgignorePath = Path.Combine(projectRoot, ".pgignore");
            if (!File.Exists(pgignorePath))
            {
                CreateDefaultPgignore(pgignorePath);
            }
            
            Debug.Log("[Package Guardian] Repository initialized successfully");
            
            // Update .gitignore to exclude .pg directory
            UpdateGitignore(projectRoot);
        }
        
        private static void CreateDefaultPgignore(string pgignorePath)
        {
            var defaultPatterns = new[]
            {
                "# Package Guardian ignore patterns",
                "# Additional patterns beyond the built-in Unity ignores",
                "",
                "# Custom patterns can be added here",
                "# Examples:",
                "# *.tmp",
                "# *.cache",
                ""
            };
            
            File.WriteAllLines(pgignorePath, defaultPatterns);
        }
        
        private static void UpdateGitignore(string projectRoot)
        {
            string gitignorePath = Path.Combine(projectRoot, ".gitignore");
            
            if (!File.Exists(gitignorePath))
                return; // No .gitignore, skip
            
            string gitignoreContent = File.ReadAllText(gitignorePath);
            
            // Check if .pg is already ignored
            if (gitignoreContent.Contains(".pg/") || gitignoreContent.Contains("/.pg/"))
            {
                Debug.Log("[Package Guardian] .gitignore already contains .pg/ exclusion");
                return;
            }
            
            // Add .pg/ to gitignore
            gitignoreContent += "\n# Package Guardian repository\n.pg/\n";
            File.WriteAllText(gitignorePath, gitignoreContent);
            
            Debug.Log("[Package Guardian] Added .pg/ to .gitignore");
        }
    }
}

