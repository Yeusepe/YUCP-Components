using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using YUCP.Importer.Editor.PackageVerifier.Crypto;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class Ed25519WrapperSecurityTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), $"yucp-ed25519-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Test]
        public void TryLoadVerifiedChaosNaClAssembly_RejectsUnexpectedHash()
        {
            string dllCopy = CopyChaosNaClDll();

            bool success = InvokeTryLoadVerifiedChaosNaClAssembly(
                dllCopy,
                "0000000000000000000000000000000000000000000000000000000000000000",
                out Assembly assembly,
                out string error);

            Assert.That(success, Is.False);
            Assert.That(assembly, Is.Null);
            Assert.That(error, Does.Contain("failed integrity verification"));
        }

        [Test]
        public void TryLoadVerifiedChaosNaClAssembly_LoadsPinnedPlugin()
        {
            string dllCopy = CopyChaosNaClDll();
            const string expectedHash = "f442b14191f55536e7b72ec83a056f5ed1c55aaa2f44a0f95f00a4a24a286311";

            bool success = InvokeTryLoadVerifiedChaosNaClAssembly(
                dllCopy,
                expectedHash,
                out Assembly assembly,
                out string error);

            Assert.That(success, Is.True);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(assembly, Is.Not.Null);
            Assert.That(assembly.GetName().Name, Is.EqualTo("Chaos.NaCl"));
        }

        private static bool InvokeTryLoadVerifiedChaosNaClAssembly(
            string assemblyPath,
            string expectedSha256,
            out Assembly assembly,
            out string error)
        {
            MethodInfo method = typeof(Ed25519Wrapper).GetMethod(
                "TryLoadVerifiedChaosNaClAssembly",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            object[] args = { assemblyPath, expectedSha256, null, null };
            bool success = (bool)method.Invoke(null, args);
            assembly = args[2] as Assembly;
            error = args[3] as string;
            return success;
        }

        private string CopyChaosNaClDll()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourcePath = Path.Combine(projectRoot, "Plugins", "Chaos.NaCl.dll");
            Assert.That(File.Exists(sourcePath), Is.True, "Expected the pinned Chaos.NaCl plugin to exist in the project root.");

            string destinationPath = Path.Combine(_tempRoot, "Chaos.NaCl.dll");
            File.Copy(sourcePath, destinationPath);
            return destinationPath;
        }
    }
}
