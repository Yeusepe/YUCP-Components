using System.Collections.Generic;

namespace PackageGuardian.Core.Ignore
{
    /// <summary>
    /// Default ignore patterns for Unity projects.
    /// </summary>
    public static class DefaultIgnores
    {
        /// <summary>
        /// Hard-coded Unity ignore patterns that are applied.
        /// </summary>
        public static readonly IReadOnlyList<string> Patterns = new List<string>
        {
            // Unity generated
            "Library/",
            "Temp/",
            "Obj/",
            "Logs/",
            "Build/",
            "Builds/",
            "UserSettings/",
            
            // IDE
            ".vs/",
            ".idea/",
            ".vscode/",
            "*.csproj",
            "*.sln",
            "*.suo",
            "*.user",
            "*.userprefs",
            
            // Version control
            ".git/",
            ".svn/",
            ".hg/",
            
            // Package caches
            "Packages/*/package-cache/",
            "Packages/packages-lock.json",
            
            // Package Guardian itself
            ".pg/",
            ".pgignore",
            
            // OS
            ".DS_Store",
            "Thumbs.db",
            "desktop.ini"
        }.AsReadOnly();

        /// <summary>
        /// Check if a path matches any default ignore pattern (quick check).
        /// </summary>
        public static bool IsIgnoredByDefault(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;
            
            string normalizedPath = path.Replace('\\', '/');
            
            // Quick string checks for common patterns
            if (normalizedPath.StartsWith("Library/") ||
                normalizedPath.StartsWith("Temp/") ||
                normalizedPath.StartsWith("Obj/") ||
                normalizedPath.StartsWith("Logs/") ||
                normalizedPath.StartsWith("Build/") ||
                normalizedPath.StartsWith("Builds/") ||
                normalizedPath.StartsWith("UserSettings/") ||
                normalizedPath.StartsWith(".vs/") ||
                normalizedPath.StartsWith(".idea/") ||
                normalizedPath.StartsWith(".vscode/") ||
                normalizedPath.StartsWith(".git/") ||
                normalizedPath.StartsWith(".svn/") ||
                normalizedPath.StartsWith(".hg/") ||
                normalizedPath.StartsWith(".pg/") ||
                normalizedPath.Contains("/Library/") ||
                normalizedPath.Contains("/Temp/") ||
                normalizedPath.Contains("/Obj/") ||
                normalizedPath.Contains("/Build/") ||
                normalizedPath.Contains("/Builds/"))
            {
                return true;
            }
            
            // Check file extensions
            if (normalizedPath.EndsWith(".csproj") ||
                normalizedPath.EndsWith(".sln") ||
                normalizedPath.EndsWith(".suo") ||
                normalizedPath.EndsWith(".user") ||
                normalizedPath.EndsWith(".userprefs") ||
                normalizedPath.EndsWith(".DS_Store") ||
                normalizedPath.EndsWith("Thumbs.db") ||
                normalizedPath.EndsWith("desktop.ini"))
            {
                return true;
            }
            
            // Check package cache
            if (normalizedPath.Contains("/package-cache/") ||
                normalizedPath.EndsWith("packages-lock.json"))
            {
                return true;
            }
            
            return false;
        }
    }
}

