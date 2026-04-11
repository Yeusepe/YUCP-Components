using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.Tests
{
    public class PackageManagerWindowHostedVerificationTests
    {
        private PackageManagerWindow _window;

        [SetUp]
        public void SetUp()
        {
            _window = ScriptableObject.CreateInstance<PackageManagerWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        [Test]
        public void ResolveHostedVerifiedPackageId_UsesLastHostedVerifiedPackageWhenManifestIsMissing()
        {
            SetPrivateField("_lastHostedVerifiedPackageId", "pkg-hosted-verified");
            GetVerifiedPackageIds().Add("pkg-hosted-verified");

            var metadata = new PackageMetadata
            {
                licensePackages = new List<LicensePackageRequirement>
                {
                    new LicensePackageRequirement
                    {
                        packageId = "pkg-hosted-verified",
                        packageName = "Hosted Verified Package",
                    },
                },
            };

            string resolvedPackageId =
                InvokePrivateMethod<string>("ResolveHostedVerifiedPackageId", metadata);

            Assert.That(resolvedPackageId, Is.EqualTo("pkg-hosted-verified"));
        }

        [Test]
        public void IsHostedImportVerified_TreatsHostedVerificationAsVerifiedWithoutSigningManifest()
        {
            GetVerifiedPackageIds().Add("pkg-hosted-verified");

            var metadata = new PackageMetadata
            {
                licensePackages = new List<LicensePackageRequirement>
                {
                    new LicensePackageRequirement
                    {
                        packageId = "pkg-hosted-verified",
                        packageName = "Hosted Verified Package",
                    },
                },
            };

            bool isVerified =
                InvokePrivateMethod<bool>("IsHostedImportVerified", "pkg-hosted-verified", metadata);

            Assert.That(isVerified, Is.True);
        }

        private HashSet<string> GetVerifiedPackageIds()
        {
            FieldInfo field = typeof(PackageManagerWindow).GetField(
                "_verifiedLicensePackageIds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing _verifiedLicensePackageIds field");
            return field.GetValue(_window) as HashSet<string>;
        }

        private void SetPrivateField(string fieldName, object value)
        {
            FieldInfo field = typeof(PackageManagerWindow).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'");
            field.SetValue(_window, value);
        }

        private T InvokePrivateMethod<T>(string methodName, params object[] args)
        {
            MethodInfo method = typeof(PackageManagerWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method '{methodName}'");
            return (T)method.Invoke(_window, args);
        }
    }
}
