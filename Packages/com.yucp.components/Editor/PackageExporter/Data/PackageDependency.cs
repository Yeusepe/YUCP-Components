using System;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Defines how a package dependency should be handled during export.
    /// </summary>
    public enum DependencyExportMode
    {
        /// <summary>
        /// Include the package files directly in the exported package (bundled)
        /// </summary>
        Bundle,
        
        /// <summary>
        /// Add as a dependency in package.json (auto-downloads when users install your package)
        /// </summary>
        Dependency
    }
    
    /// <summary>
    /// Configuration for how to handle a single package dependency during export.
    /// </summary>
    [Serializable]
    public class PackageDependency
    {
        public bool enabled = true;
        public string packageName = "";
        public string packageVersion = "";
        public string displayName = "";
        public DependencyExportMode exportMode = DependencyExportMode.Dependency;
        public bool isVpmDependency = false; // VRChat packages use vpmDependencies instead
        
        public PackageDependency()
        {
        }
        
        public PackageDependency(string name, string version, string displayName, bool isVpm)
        {
            this.packageName = name;
            this.packageVersion = version;
            this.displayName = displayName;
            this.isVpmDependency = isVpm;
            this.enabled = true;
            this.exportMode = DependencyExportMode.Dependency;
        }
    }
}
