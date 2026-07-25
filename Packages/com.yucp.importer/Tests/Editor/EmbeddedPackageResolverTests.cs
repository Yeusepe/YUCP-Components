using System.Collections.Generic;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class EmbeddedPackageResolverTests
    {
        [Test]
        public void AddedEmbeddedPackageDescriptorRequiresResolution()
        {
            Assert.IsTrue(EmbeddedPackageResolver.RequiresResolution(
                new[]
                {
                    Record(
                        "Packages/com.example.product/package.json",
                        new string('1', 64)),
                },
                new List<VerifiedStagingFile>()));
        }

        [Test]
        public void ChangedEmbeddedPackageDescriptorRequiresResolution()
        {
            Assert.IsTrue(EmbeddedPackageResolver.RequiresResolution(
                new[]
                {
                    Record(
                        "Packages/com.example.product/package.json",
                        new string('2', 64)),
                },
                new[]
                {
                    Record(
                        "Packages/com.example.product/package.json",
                        new string('1', 64)),
                }));
        }

        [Test]
        public void RemovedEmbeddedPackageDescriptorRequiresResolution()
        {
            Assert.IsTrue(EmbeddedPackageResolver.RequiresResolution(
                new List<VerifiedStagingFile>(),
                new[]
                {
                    Record(
                        "Packages/com.example.product/package.json",
                        new string('1', 64)),
                }));
        }

        [Test]
        public void UnchangedEmbeddedPackageDescriptorDoesNotRequireResolution()
        {
            Assert.IsFalse(EmbeddedPackageResolver.RequiresResolution(
                new[]
                {
                    Record(
                        "Packages/com.example.product/package.json",
                        new string('1', 64)),
                    Record(
                        "Assets/Product/file.cs",
                        new string('2', 64)),
                },
                new[]
                {
                    Record(
                        "Packages/com.example.product/package.json",
                        new string('1', 64)),
                    Record(
                        "Assets/Product/file.cs",
                        new string('3', 64)),
                }));
        }

        [TestCase("Packages/package.json")]
        [TestCase("Packages/com.example.product/Editor/package.json")]
        [TestCase("Assets/Packages/com.example.product/package.json")]
        public void NonRootPackageDescriptorsDoNotRequireResolution(string path)
        {
            Assert.IsFalse(EmbeddedPackageResolver.RequiresResolution(
                new[] { Record(path, new string('1', 64)) },
                new List<VerifiedStagingFile>()));
        }

        private static VerifiedStagingFile Record(
            string path,
            string sha256)
        {
            return new VerifiedStagingFile
            {
                bytes = 1,
                normalizedPath = path,
                sha256 = sha256,
            };
        }
    }
}
