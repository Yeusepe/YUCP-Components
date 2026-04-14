using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class CouplingRuntimeServiceTests
    {
        [Test]
        public void CreateRuntimeHostProcessStartInfo_UsesSystemRundll32()
        {
            string runtimePath = @"C:\runtime\coupling.dll";
            string manifestPath = @"C:\runtime\manifest.dat";

            ProcessStartInfo startInfo = InvokeCreateRuntimeHostProcessStartInfo(runtimePath, manifestPath);

            Assert.That(startInfo.FileName, Is.EqualTo(System.IO.Path.Combine(Environment.SystemDirectory, "rundll32.exe")));
            Assert.That(startInfo.Arguments, Is.EqualTo($"\"{runtimePath}\",EntryPoint \"{manifestPath}\""));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.CreateNoWindow, Is.True);
        }

        private static ProcessStartInfo InvokeCreateRuntimeHostProcessStartInfo(string runtimePath, string manifestPath)
        {
            Type serviceType = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core.CouplingRuntimeService",
                throwOnError: false);
            Assert.That(serviceType, Is.Not.Null);

            MethodInfo method = serviceType.GetMethod("CreateRuntimeHostProcessStartInfo", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { runtimePath, manifestPath }) as ProcessStartInfo;
        }
    }
}
