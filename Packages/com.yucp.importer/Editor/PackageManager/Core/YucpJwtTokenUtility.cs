using System;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using YUCP.Importer.Editor.PackageVerifier;
using YUCP.Importer.Editor.PackageVerifier.Core;
using YUCP.Importer.Editor.PackageVerifier.Crypto;
using YUCP.Importer.Editor.PackageVerifier.Settings;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class YucpJwtTokenUtility
    {
        internal static bool TryValidateLicenseToken(string jwt, string expectedPackageId, out LicenseTokenClaims claims)
        {
            claims = null;
            if (!TryVerifyToken(jwt, out var payloadJson, out _))
                return false;

            claims = JsonUtility.FromJson<LicenseTokenClaims>(payloadJson);
            if (claims == null)
                return false;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (claims.exp <= now || claims.iat > now + 300)
                return false;
            if (claims.aud != "yucp-license-gate")
                return false;
            if (claims.iss != LicenseServerResolver.GetExpectedIssuer())
                return false;
            if (claims.package_id != expectedPackageId)
                return false;

            string fingerprint = MachineFingerprintService.GetFingerprint();
            return claims.machine_fingerprint == fingerprint;
        }

        private static bool TryVerifyToken(string jwt, out string payloadJson, out JwtHeader header)
        {
            payloadJson = null;
            header = null;

            if (string.IsNullOrWhiteSpace(jwt))
                return false;

            string[] parts = jwt.Split('.');
            if (parts.Length != 3)
                return false;

            try
            {
                string headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
                payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                header = JsonUtility.FromJson<JwtHeader>(headerJson);
                if (header == null || header.alg != "EdDSA" || string.IsNullOrEmpty(header.kid))
                    return false;

                byte[] publicKey = ResolvePublicKey(header.kid);
                if (publicKey == null || publicKey.Length != 32)
                    return false;

                byte[] signingInput = Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]);
                byte[] signature = Base64UrlDecode(parts[2]);
                return Ed25519Wrapper.Verify(signingInput, signature, publicKey);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP License] Token verification failed: {ex.Message}");
                return false;
            }
        }

        private static byte[] ResolvePublicKey(string keyId)
        {
            byte[] publicKey = TrustedAuthority.GetPublicKey(keyId);
            return publicKey != null && publicKey.Length == 32 ? publicKey : null;
        }

        private static bool IsKnownKeyId(string keyId)
        {
            if (string.IsNullOrEmpty(keyId))
                return false;

            return TrustedAuthoritiesSettings.GetCachedKeys().ContainsKey(keyId)
                || TrustedAuthority.IsTrustedKey(keyId);
        }

        private static bool TryFetchAuthorityKeys(string serverUrl)
        {
            string normalizedUrl = TrustedAuthoritiesSettings.NormalizeUrl(serverUrl) ?? serverUrl;
            string fetchUrl = AuthorityKeyFetcher.GetAuthorityDocumentUrl(normalizedUrl);
            using var request = UnityWebRequest.Get(fetchUrl);
            request.timeout = 15;
            request.SetRequestHeader("Accept-Encoding", "identity");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                Thread.Sleep(20);
            }

            if (request.result != UnityWebRequest.Result.Success)
                return false;

            var result = AuthorityKeyFetcher.ParseAuthorityResponse(request.downloadHandler.text);
            if (!result.success || result.keys == null || result.keys.Count == 0)
                return false;

            var pinnedKeys = TrustedAuthority.FilterToPinnedKeys(result.keys);
            if (pinnedKeys.Count == 0)
                return false;

            TrustedAuthoritiesSettings.CacheKeys(normalizedUrl, pinnedKeys, result.fetchTime == default ? DateTime.UtcNow : result.fetchTime);
            TrustedAuthority.ReloadAllKeys();
            return true;
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string padded = input.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }

        [Serializable]
        private class JwtHeader
        {
            public string alg;
            public string kid;
        }

        [Serializable]
        internal class LicenseTokenClaims
        {
            public string iss;
            public string aud;
            public string sub;
            public string jti;
            public string package_id;
            public string machine_fingerprint;
            public string provider;
            public long iat;
            public long exp;
        }

    }
}
