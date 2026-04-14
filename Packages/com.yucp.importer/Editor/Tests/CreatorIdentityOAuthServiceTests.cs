using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class CreatorIdentityOAuthServiceTests
    {
        private const string PackageId = "pkg-signout-cache-test";
        private const string ProtectedAssetId = "0123456789abcdef0123456789abcdef";

        [TearDown]
        public void TearDown()
        {
            TryDeleteFile(GetLicenseCachePath());
            TryDeleteFile(GetUnlockCachePath());
            SessionState.EraseString(GetLicenseSessionKey());
            SessionState.EraseString(GetUnlockSessionKey());
            SetVerificationOpenUrlOverride(null);
        }

        [Test]
        public void SignOut_ClearsLicenseAndProtectedUnlockCaches_ForInstalledPackages()
        {
            string licenseSessionKey = GetLicenseSessionKey();
            string unlockSessionKey = GetUnlockSessionKey();
            string licenseCachePath = GetLicenseCachePath();
            string unlockCachePath = GetUnlockCachePath();

            Directory.CreateDirectory(Path.GetDirectoryName(licenseCachePath));
            Directory.CreateDirectory(Path.GetDirectoryName(unlockCachePath));
            File.WriteAllBytes(licenseCachePath, new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(unlockCachePath, new byte[] { 5, 6, 7, 8 });
            SessionState.SetString(licenseSessionKey, "cached-license-token");
            SessionState.SetString(unlockSessionKey, "cached-unlock-token");

            InvokeSignOut(new List<InstalledPackageInfo>
            {
                new InstalledPackageInfo
                {
                    packageId = PackageId,
                    protectedPayload = new ProtectedPayloadDescriptor
                    {
                        protectedAssetId = ProtectedAssetId,
                    },
                },
            });

            Assert.That(string.IsNullOrEmpty(SessionState.GetString(licenseSessionKey, null)), Is.True);
            Assert.That(string.IsNullOrEmpty(SessionState.GetString(unlockSessionKey, null)), Is.True);
            Assert.That(File.Exists(licenseCachePath), Is.False);
            Assert.That(File.Exists(unlockCachePath), Is.False);
        }

        [UnityTest]
        public System.Collections.IEnumerator LicenseTokenCache_StoreToken_FromBackgroundThread_MarshalsSessionStateAccess()
        {
            string licenseSessionKey = GetLicenseSessionKey();
            string licenseCachePath = GetLicenseCachePath();
            const string jwt = "background-thread-license-token";

            Task backgroundWrite = Task.Run(() => InvokeLicenseStoreToken(jwt));
            while (!backgroundWrite.IsCompleted)
            {
                yield return null;
            }

            Assert.That(backgroundWrite.Exception, Is.Null);
            Assert.That(SessionState.GetString(licenseSessionKey, null), Is.EqualTo(jwt));
            Assert.That(File.Exists(licenseCachePath), Is.True);
        }

        [UnityTest]
        public System.Collections.IEnumerator VerificationIntentService_OpenVerificationUrl_FromBackgroundThread_MarshalsOpenUrl()
        {
            const string verificationUrl = "https://example.invalid/verify";
            string observedUrl = null;
            SetVerificationOpenUrlOverride(url => observedUrl = url);

            Task backgroundOpen = Task.Run(() => InvokeVerificationOpenUrl(verificationUrl));
            while (!backgroundOpen.IsCompleted)
            {
                yield return null;
            }

            Assert.That(backgroundOpen.Exception, Is.Null);
            Assert.That(observedUrl, Is.EqualTo(verificationUrl));
        }

        private static void InvokeSignOut(IReadOnlyList<InstalledPackageInfo> installedPackages)
        {
            MethodInfo signOut = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.CreatorIdentityOAuthService")
                .GetMethod("SignOut", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IReadOnlyList<InstalledPackageInfo>) }, null);

            Assert.That(signOut, Is.Not.Null);
            signOut.Invoke(null, new object[] { installedPackages });
        }

        private static string GetLicenseSessionKey()
        {
            MethodInfo getSessionKey = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.LicenseTokenCache")
                .GetMethod("GetSessionKey", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(getSessionKey, Is.Not.Null);
            return getSessionKey.Invoke(null, new object[] { PackageId }) as string;
        }

        private static string GetUnlockSessionKey()
        {
            MethodInfo getSessionKey = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.ProtectedAssetUnlockService")
                .GetMethod("GetSessionKey", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(getSessionKey, Is.Not.Null);
            return getSessionKey.Invoke(null, new object[] { PackageId, ProtectedAssetId }) as string;
        }

        private static string GetLicenseCachePath()
        {
            MethodInfo cacheFilePath = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.LicenseTokenCache")
                .GetMethod("CacheFilePath", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(cacheFilePath, Is.Not.Null);
            return cacheFilePath.Invoke(null, new object[] { PackageId }) as string;
        }

        private static void InvokeLicenseStoreToken(string jwt)
        {
            MethodInfo storeToken = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.LicenseTokenCache")
                .GetMethod("StoreToken", BindingFlags.Public | BindingFlags.Static);

            Assert.That(storeToken, Is.Not.Null);
            storeToken.Invoke(null, new object[] { PackageId, jwt });
        }

        private static void InvokeVerificationOpenUrl(string verificationUrl)
        {
            MethodInfo openVerificationUrl = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.VerificationIntentService")
                .GetMethod("OpenVerificationUrl", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(openVerificationUrl, Is.Not.Null);
            openVerificationUrl.Invoke(null, new object[] { verificationUrl, null });
        }

        private static void SetVerificationOpenUrlOverride(Action<string> openUrlOverride)
        {
            VerificationIntentServiceTestHooks.OpenUrlHandler = openUrlOverride;
        }

        private static string GetUnlockCachePath()
        {
            MethodInfo cacheFilePath = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.ProtectedAssetUnlockService")
                .GetMethod("CacheFilePath", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(cacheFilePath, Is.Not.Null);
            return cacheFilePath.Invoke(null, new object[] { PackageId, ProtectedAssetId }) as string;
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            File.Delete(path);
        }

        private static System.Type GetEditorType(string fullName)
        {
            System.Type editorType = typeof(InstalledPackageInfo).Assembly.GetType(fullName, false);
            Assert.That(editorType, Is.Not.Null, $"Expected to load type '{fullName}'.");
            return editorType;
        }
    }
}
