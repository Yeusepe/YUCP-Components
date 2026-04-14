using NUnit.Framework;
using System;
using System.Collections.Generic;
using YUCP.Importer.Editor.PackageVerifier.Core;
using YUCP.Importer.Editor.PackageVerifier;
using YUCP.Importer.Editor.PackageVerifier.Settings;

namespace YUCP.Importer.Editor.Tests
{
    public class TrustedAuthorityTests
    {
        [SetUp]
        public void SetUp()
        {
            TrustedAuthoritiesSettings.ClearCachedKeys();
            TrustedAuthority.ReloadAllKeys();
        }

        [TearDown]
        public void TearDown()
        {
            TrustedAuthoritiesSettings.ClearCachedKeys();
            TrustedAuthority.ReloadAllKeys();
        }

        [Test]
        public void GetPublicKey_ReturnsCachedTrustedUrlKey()
        {
            const string keyId = TrustedAuthority.PrimaryRootKeyId;
            const string publicKey = TrustedAuthority.PinnedRootPublicKeyBase64;

            TrustedAuthoritiesSettings.CacheKeys(
                TrustedAuthoritiesSettings.DefaultTrustedUrl,
                new List<AuthorityKeyFetcher.AuthorityKey>
                {
                    new AuthorityKeyFetcher.AuthorityKey
                    {
                        keyId = keyId,
                        publicKey = publicKey,
                        displayName = keyId,
                    }
                },
                DateTime.UtcNow);

            TrustedAuthority.ReloadAllKeys();

            byte[] trustedKey = TrustedAuthority.GetPublicKey(keyId);

            Assert.That(trustedKey, Is.Not.Null);
            CollectionAssert.AreEqual(Convert.FromBase64String(publicKey), trustedKey);
        }

        [Test]
        public void GetPublicKey_IgnoresCachedKeyThatDoesNotMatchPinnedRoots()
        {
            const string keyId = "CREATOR-TOOLING-2026";
            const string publicKey = "SQF9r3TkKGwwQ6jGLBOABnq3UeOcHayQS3WbEJeUhnc=";

            TrustedAuthoritiesSettings.CacheKeys(
                TrustedAuthoritiesSettings.DefaultTrustedUrl,
                new List<AuthorityKeyFetcher.AuthorityKey>
                {
                    new AuthorityKeyFetcher.AuthorityKey
                    {
                        keyId = keyId,
                        publicKey = publicKey,
                        displayName = keyId,
                    }
                },
                DateTime.UtcNow);

            TrustedAuthority.ReloadAllKeys();

            byte[] trustedKey = TrustedAuthority.GetPublicKey(keyId);

            Assert.That(trustedKey, Is.Null);
        }
    }
}
