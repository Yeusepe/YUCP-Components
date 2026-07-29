using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal interface INativePackagePublisherVerifier
    {
        void Verify(
            string executablePath,
            string expectedSubject,
            string expectedCertificateSha256,
            string trustMode);
    }

    internal sealed class NativePackageRuntimeTrust
    {
        internal readonly string executableSha256;
        internal readonly string metadataUrl;
        internal readonly string publisherCertificateSha256;
        internal readonly string publisherSubject;
        internal readonly string publisherTrustMode;
        internal readonly INativePackagePublisherVerifier publisherVerifier;
        internal readonly string targetsUrl;
        internal readonly string trustedRootSha256;

        internal NativePackageRuntimeTrust(
            string executableSha256,
            string trustedRootSha256,
            string metadataUrl,
            string targetsUrl,
            string publisherSubject,
            string publisherCertificateSha256,
            string publisherTrustMode,
            INativePackagePublisherVerifier publisherVerifier)
        {
            if (!IsSha256(executableSha256) ||
                !IsSha256(trustedRootSha256) ||
                !IsTrustedRepositoryUrl(metadataUrl) ||
                !IsTrustedRepositoryUrl(targetsUrl) ||
                string.IsNullOrWhiteSpace(publisherSubject) ||
                !IsSha256(publisherCertificateSha256) ||
                !IsPublisherTrustConfigurationValid(
                    metadataUrl,
                    targetsUrl,
                    publisherSubject,
                    publisherTrustMode) ||
                publisherVerifier == null)
            {
                throw new InvalidDataException(
                    "The package runtime release trust is not configured.");
            }
            this.executableSha256 = executableSha256;
            this.trustedRootSha256 = trustedRootSha256;
            this.metadataUrl = metadataUrl;
            this.targetsUrl = targetsUrl;
            this.publisherSubject = publisherSubject;
            this.publisherCertificateSha256 =
                publisherCertificateSha256;
            this.publisherTrustMode = publisherTrustMode;
            this.publisherVerifier = publisherVerifier;
        }

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!(character >= '0' && character <= '9') &&
                    !(character >= 'a' && character <= 'f'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsTrustedRepositoryUrl(string value)
        {
            Uri url;
            if (string.IsNullOrWhiteSpace(value) ||
                value != value.Trim() ||
                value.EndsWith("/", StringComparison.Ordinal) ||
                !Uri.TryCreate(value, UriKind.Absolute, out url) ||
                !string.IsNullOrEmpty(url.UserInfo) ||
                !string.IsNullOrEmpty(url.Query) ||
                !string.IsNullOrEmpty(url.Fragment) ||
                !string.Equals(
                    url.AbsoluteUri,
                    value,
                    StringComparison.Ordinal))
            {
                return false;
            }
            return string.Equals(
                       url.Scheme,
                       Uri.UriSchemeHttps,
                       StringComparison.Ordinal) ||
                   (string.Equals(
                        url.Scheme,
                        Uri.UriSchemeHttp,
                        StringComparison.Ordinal) &&
                   url.IsLoopback);
        }

        private static bool IsPublisherTrustConfigurationValid(
            string metadataUrl,
            string targetsUrl,
            string publisherSubject,
            string trustMode)
        {
            if (string.Equals(
                    trustMode,
                    WindowsAuthenticodePublisherVerifier.SystemTrustMode,
                    StringComparison.Ordinal))
            {
                return true;
            }
            if (string.Equals(
                    trustMode,
                    WindowsAuthenticodePublisherVerifier
                        .PinnedProductionTrustMode,
                    StringComparison.Ordinal))
            {
                return string.Equals(
                           publisherSubject,
                           WindowsAuthenticodePublisherVerifier
                               .PinnedProductionSubject,
                           StringComparison.Ordinal) &&
                       IsHttpsRepositoryUrl(metadataUrl) &&
                       IsHttpsRepositoryUrl(targetsUrl);
            }
            return string.Equals(
                       trustMode,
                       WindowsAuthenticodePublisherVerifier
                           .PinnedDevelopmentTrustMode,
                       StringComparison.Ordinal) &&
                   publisherSubject.StartsWith(
                       "CN=YUCP Local Development ",
                       StringComparison.Ordinal) &&
                   IsLoopbackHttpRepositoryUrl(metadataUrl) &&
                   IsLoopbackHttpRepositoryUrl(targetsUrl);
        }

        private static bool IsLoopbackHttpRepositoryUrl(string value)
        {
            Uri url;
            return Uri.TryCreate(value, UriKind.Absolute, out url) &&
                   string.Equals(
                       url.Scheme,
                       Uri.UriSchemeHttp,
                       StringComparison.Ordinal) &&
                   url.IsLoopback;
        }

        private static bool IsHttpsRepositoryUrl(string value)
        {
            Uri url;
            return Uri.TryCreate(value, UriKind.Absolute, out url) &&
                   string.Equals(
                       url.Scheme,
                       Uri.UriSchemeHttps,
                       StringComparison.Ordinal);
        }
    }

    internal static class NativePackageRuntimeReleaseTrust
    {
        internal const string ExecutableSha256 =
            "df117742e6003ed3ca4ab7e9d18178ca9a9a8b09b495df311bc3fbd0262581ab";
        internal const string MetadataUrl =
            "https://verify.creators.yucp.club/api/v2/package-installer/tuf/metadata";
        internal const string PublisherCertificateSha256 =
            "9b6dd710c802f64177ac1a4033fa692dc2b2a9f1c13471edebbe5e121e5cf5e3";
        internal const string PublisherSubject =
            "CN=YUCP Package Runtime";
        internal const string PublisherTrustMode =
            "pinned-production";
        internal const string TargetsUrl =
            "https://verify.creators.yucp.club/api/v2/package-installer/tuf/targets";
        internal const string TrustedRootSha256 =
            "89c01b7ae6b44904fc09155abfbdde8abde407b5cdb3272a493e8aab7574a589";

        internal static NativePackageRuntimeTrust Load()
        {
            return new NativePackageRuntimeTrust(
                ExecutableSha256,
                TrustedRootSha256,
                MetadataUrl,
                TargetsUrl,
                PublisherSubject,
                PublisherCertificateSha256,
                PublisherTrustMode,
                new WindowsAuthenticodePublisherVerifier());
        }
    }

    internal sealed class WindowsAuthenticodePublisherVerifier
        : INativePackagePublisherVerifier
    {
        internal const string PinnedDevelopmentTrustMode =
            "pinned-development";
        internal const string PinnedProductionTrustMode =
            "pinned-production";
        internal const string PinnedProductionSubject =
            "CN=YUCP Package Runtime";
        internal const string SystemTrustMode = "system";
        private const uint UnionChoiceFile = 1;
        private const uint UiChoiceNone = 2;
        private const uint RevokeWholeChain = 1;
        private const uint DisableMd2Md4 = 0x2000;
        private const uint RevocationCheckChain = 0x40;
        private const int CertEUntrustedRoot =
            unchecked((int)0x800B0109);
        private static readonly Guid GenericVerifyV2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public void Verify(
            string executablePath,
            string expectedSubject,
            string expectedCertificateSha256,
            string trustMode)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                throw new PlatformNotSupportedException(
                    "Windows publisher verification is unavailable.");
            }
            VerifyAuthenticode(executablePath, trustMode);
            VerifyPublisherIdentity(
                executablePath,
                expectedSubject,
                expectedCertificateSha256,
                trustMode);
        }

        private static void VerifyAuthenticode(
            string path,
            string trustMode)
        {
            IntPtr pathPointer = IntPtr.Zero;
            IntPtr filePointer = IntPtr.Zero;
            try
            {
                pathPointer = Marshal.StringToCoTaskMemUni(path);
                var file = new WinTrustFileInfo
                {
                    structureSize = (uint)Marshal.SizeOf(
                        typeof(WinTrustFileInfo)),
                    filePath = pathPointer,
                };
                filePointer = Marshal.AllocCoTaskMem(
                    Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(file, filePointer, false);
                var trust = new WinTrustData
                {
                    structureSize = (uint)Marshal.SizeOf(
                        typeof(WinTrustData)),
                    uiChoice = UiChoiceNone,
                    revocationChecks =
                        IsPinnedTrustMode(trustMode)
                            ? 0u
                            : RevokeWholeChain,
                    unionChoice = UnionChoiceFile,
                    fileInformation = filePointer,
                    providerFlags = ProviderFlagsForTests(
                        trustMode),
                };
                Guid action = GenericVerifyV2;
                int result = WinVerifyTrust(
                    new IntPtr(-1),
                    ref action,
                    ref trust);
                if (!IsAuthenticodeResultAcceptedForTests(
                        result,
                        trustMode))
                {
                    throw new CryptographicException(
                        "The package runtime publisher signature is invalid.");
                }
            }
            finally
            {
                if (filePointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(filePointer);
                }
                if (pathPointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pathPointer);
                }
            }
        }

        private static void VerifyPublisherIdentity(
            string path,
            string expectedSubject,
            string expectedCertificateSha256,
            string trustMode)
        {
            // https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate.createfromsignedfile
            using (X509Certificate embedded =
                X509Certificate.CreateFromSignedFile(path))
            using (var certificate = new X509Certificate2(embedded))
            using (SHA256 sha256 = SHA256.Create())
            {
                string actualCertificateSha256 =
                    BitConverter.ToString(
                            sha256.ComputeHash(certificate.RawData))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                bool invalidDevelopmentCertificate =
                    string.Equals(
                        trustMode,
                        PinnedDevelopmentTrustMode,
                        StringComparison.Ordinal) &&
                    (!StructuralComparisons
                         .StructuralEqualityComparer.Equals(
                             certificate.SubjectName.RawData,
                             certificate.IssuerName.RawData) ||
                     certificate.NotBefore.ToUniversalTime() >
                         DateTime.UtcNow.AddMinutes(5) ||
                     certificate.NotAfter.ToUniversalTime() >
                         DateTime.UtcNow.AddDays(2));
                if (!string.Equals(
                        actualCertificateSha256,
                        expectedCertificateSha256,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        certificate.Subject,
                        expectedSubject,
                        StringComparison.Ordinal) ||
                    invalidDevelopmentCertificate)
                {
                    throw new CryptographicException(
                        "The package runtime publisher identity is invalid.");
                }
            }
        }

        internal static bool IsAuthenticodeResultAcceptedForTests(
            int result,
            string trustMode)
        {
            return result == 0 ||
                   (result == CertEUntrustedRoot &&
                    IsPinnedTrustMode(trustMode));
        }

        internal static uint ProviderFlagsForTests(string trustMode)
        {
            return IsPinnedTrustMode(trustMode)
                ? DisableMd2Md4
                : DisableMd2Md4 | RevocationCheckChain;
        }

        private static bool IsPinnedTrustMode(string trustMode)
        {
            return string.Equals(
                       trustMode,
                       PinnedDevelopmentTrustMode,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       trustMode,
                       PinnedProductionTrustMode,
                       StringComparison.Ordinal);
        }

        // https://learn.microsoft.com/windows/win32/api/wintrust/nf-wintrust-winverifytrust
        [DllImport(
            "wintrust.dll",
            ExactSpelling = true,
            PreserveSig = true)]
        private static extern int WinVerifyTrust(
            IntPtr window,
            ref Guid actionId,
            ref WinTrustData trustData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            internal uint structureSize;
            internal IntPtr filePath;
            internal IntPtr fileHandle;
            internal IntPtr knownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            internal uint structureSize;
            internal IntPtr policyCallbackData;
            internal IntPtr sipClientData;
            internal uint uiChoice;
            internal uint revocationChecks;
            internal uint unionChoice;
            internal IntPtr fileInformation;
            internal uint stateAction;
            internal IntPtr stateData;
            internal IntPtr urlReference;
            internal uint providerFlags;
            internal uint uiContext;
            internal IntPtr signatureSettings;
        }
    }
}
