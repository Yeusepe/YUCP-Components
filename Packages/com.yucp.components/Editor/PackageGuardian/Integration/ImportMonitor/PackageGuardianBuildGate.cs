using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using YUCP.Components.PackageGuardian.Editor.Services;

namespace YUCP.Components.PackageGuardian.Editor.Integration.ImportMonitor
{
    internal sealed class PackageGuardianBuildGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => -8000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (ProtectionLatchService.IsLocked(out var summary))
                throw new BuildFailedException("Package Guardian lock active: " + summary);
        }
    }
}
