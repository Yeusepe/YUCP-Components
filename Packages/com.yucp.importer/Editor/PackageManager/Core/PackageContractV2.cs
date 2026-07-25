using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Chaos.NaCl;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class PackageContractV2
    {
        internal const int Version = 2;
        internal const int CoseAlgorithmEdDsa = -8;
        internal const int PurposeHeader = 1001;
        internal const string InstallSessionPurpose = "install-session-v2";
        internal const string MaterializationReceiptPurpose = "materialization-receipt-v2";
        private static readonly Regex HashPurposePattern = new Regex(
            "^yucp:[a-z0-9-]+:v[0-9]+$",
            RegexOptions.CultureInvariant);

        internal static byte[] HashFields(string purpose, params byte[][] fields)
        {
            if (string.IsNullOrEmpty(purpose) || !HashPurposePattern.IsMatch(purpose))
                throw new ArgumentException(
                    "Package hash purpose must be a versioned ASCII YUCP purpose.",
                    nameof(purpose));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            byte[] purposeBytes = Encoding.ASCII.GetBytes(purpose);
            using (SHA256 sha256 = SHA256.Create())
            {
                sha256.TransformBlock(
                    purposeBytes,
                    0,
                    purposeBytes.Length,
                    purposeBytes,
                    0);
                foreach (byte[] field in fields)
                {
                    if (field == null)
                        throw new ArgumentException(
                            "Package hash fields must not contain null.",
                            nameof(fields));
                    byte[] length = EncodeUnsignedBigEndian((ulong)field.LongLength);
                    sha256.TransformBlock(length, 0, length.Length, length, 0);
                    sha256.TransformBlock(field, 0, field.Length, field, 0);
                }
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return sha256.Hash;
            }
        }

        private static byte[] EncodeUnsignedBigEndian(ulong value)
        {
            var bytes = new byte[8];
            for (int index = bytes.Length - 1; index >= 0; index--)
            {
                bytes[index] = (byte)(value & 0xff);
                value >>= 8;
            }
            return bytes;
        }

        internal static byte[] VerifySignedPayload(
            byte[] coseSign1,
            string expectedPurpose,
            byte[] expectedKeyId,
            byte[] publicKey)
        {
            if (coseSign1 == null) throw new ArgumentNullException(nameof(coseSign1));
            if (string.IsNullOrWhiteSpace(expectedPurpose))
                throw new ArgumentException("Expected purpose is missing.", nameof(expectedPurpose));
            if (expectedKeyId == null || expectedKeyId.Length == 0 || expectedKeyId.Length > 64)
                throw new ArgumentException("Expected key ID must contain 1 through 64 bytes.", nameof(expectedKeyId));
            if (publicKey == null || publicKey.Length != 32)
                throw new ArgumentException("Ed25519 public key must contain 32 bytes.", nameof(publicKey));

            try
            {
                return VerifySignedPayloadCanonical(
                    coseSign1,
                    expectedPurpose,
                    expectedKeyId,
                    publicKey);
            }
            catch (CborContentException exception)
            {
                throw new FormatException("COSE_Sign1 contains invalid CBOR.", exception);
            }
        }

        private static byte[] VerifySignedPayloadCanonical(
            byte[] coseSign1,
            string expectedPurpose,
            byte[] expectedKeyId,
            byte[] publicKey)
        {
            var reader = NewCanonicalReader(coseSign1);
            RequireLength(reader.ReadStartArray(), 4, "COSE_Sign1 array");
            byte[] protectedHeaders = reader.ReadByteString();
            RequireLength(reader.ReadStartMap(), 0, "COSE_Sign1 unprotected map");
            reader.ReadEndMap();
            byte[] payload = reader.ReadByteString();
            byte[] signature = reader.ReadByteString();
            if (signature.Length != 64)
                throw new FormatException("COSE_Sign1 signature must contain 64 bytes.");
            reader.ReadEndArray();
            RequireFinished(reader, "COSE_Sign1");

            VerifyProtectedHeaders(
                protectedHeaders,
                expectedPurpose,
                expectedKeyId);
            AssertCanonicalPayload(payload);

            byte[] toBeSigned = BuildSignatureStructure(protectedHeaders, payload);
            if (!Ed25519.Verify(signature, toBeSigned, publicKey))
                throw new FormatException("COSE_Sign1 signature is invalid.");
            return payload;
        }

        internal static void AssertCanonicalPayload(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            try
            {
                var reader = NewCanonicalReader(payload);
                reader.SkipValue();
                RequireFinished(reader, "Package contract payload");
            }
            catch (CborContentException exception)
            {
                throw new FormatException(
                    "Package contract payload contains invalid CBOR.",
                    exception);
            }
        }

        private static void VerifyProtectedHeaders(
            byte[] protectedHeaders,
            string expectedPurpose,
            byte[] expectedKeyId)
        {
            var reader = NewCanonicalReader(protectedHeaders);
            RequireLength(reader.ReadStartMap(), 4, "COSE protected header map");

            RequireLabel(reader, 1);
            if (reader.ReadInt32() != CoseAlgorithmEdDsa)
                throw new FormatException("COSE_Sign1 algorithm is not EdDSA.");

            RequireLabel(reader, 2);
            RequireLength(reader.ReadStartArray(), 1, "COSE critical header array");
            if (reader.ReadInt32() != PurposeHeader)
                throw new FormatException("COSE purpose header is not critical.");
            reader.ReadEndArray();

            RequireLabel(reader, 4);
            byte[] keyId = reader.ReadByteString();
            if (!FixedTimeEquals(keyId, expectedKeyId))
                throw new FormatException("COSE_Sign1 key ID is not trusted.");

            RequireLabel(reader, PurposeHeader);
            if (!string.Equals(reader.ReadTextString(), expectedPurpose, StringComparison.Ordinal))
                throw new FormatException("COSE_Sign1 purpose does not match the expected contract.");

            reader.ReadEndMap();
            RequireFinished(reader, "COSE protected headers");
        }

        private static byte[] BuildSignatureStructure(byte[] protectedHeaders, byte[] payload)
        {
            var writer = new CborWriter(CborConformanceMode.Canonical);
            writer.WriteStartArray(4);
            writer.WriteTextString("Signature1");
            writer.WriteByteString(protectedHeaders);
            writer.WriteByteString(Array.Empty<byte>());
            writer.WriteByteString(payload);
            writer.WriteEndArray();
            return writer.Encode();
        }

        private static CborReader NewCanonicalReader(byte[] bytes)
        {
            return new CborReader(
                bytes,
                CborConformanceMode.Canonical,
                allowMultipleRootLevelValues: false);
        }

        internal static void RequireLength(int? actual, int expected, string name)
        {
            if (!actual.HasValue || actual.Value != expected)
                throw new FormatException($"{name} must contain {expected} items.");
        }

        internal static void RequireLabel(CborReader reader, int expected)
        {
            int actual = reader.ReadInt32();
            if (actual != expected)
                throw new FormatException($"Package contract label {expected} is missing or misplaced.");
        }

        internal static void RequireFinished(CborReader reader, string name)
        {
            if (reader.BytesRemaining != 0)
                throw new FormatException($"{name} contains trailing bytes.");
        }

        internal static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }

    internal sealed class InstallSessionBootstrapV2
    {
        internal string Kind;
        internal string Url;
        internal byte[] Sha256;
    }

    internal sealed class InstallSessionV2
    {
        internal string AliasId;
        internal string[] AllowedApiOrigins;
        internal string[] AllowedArtifactOrigins;
        internal string Audience;
        internal byte[] BindingRoot;
        internal InstallSessionBootstrapV2[] Bootstrap;
        internal string BuyerId;
        internal string CreatorId;
        internal byte[] DeviceKeyThumbprint;
        internal long ExpiresAt;
        internal long IssuedAt;
        internal string Issuer;
        internal string KeyId;
        internal long MaxLifetimeSeconds;
        internal long NotBefore;
        internal string ProductId;
        internal byte[] ReleaseRoot;
        internal string SessionId;
        internal string TokenType;
        internal string Version;
    }

    internal sealed class InstallSessionValidationContext
    {
        internal string AliasId;
        internal string[] AllowedApiOrigins;
        internal string[] AllowedArtifactOrigins;
        internal string Audience;
        internal byte[] BindingRoot;
        internal byte[] DeviceKeyThumbprint;
        internal string Issuer;
        internal long Now;
        internal byte[] ReleaseRoot;
    }

    internal sealed class VerifiedInstallSessionV2
    {
        private readonly byte[] _coseSign1;
        private readonly byte[] _expectedKeyId;
        private readonly byte[] _publicKey;

        internal InstallSessionV2 Session { get; }

        internal VerifiedInstallSessionV2(
            byte[] coseSign1,
            byte[] expectedKeyId,
            byte[] publicKey,
            InstallSessionV2 session)
        {
            _coseSign1 = (byte[])coseSign1.Clone();
            _expectedKeyId = (byte[])expectedKeyId.Clone();
            _publicKey = (byte[])publicKey.Clone();
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal InstallSessionV2 ValidateBeforeProjectMutation(
            InstallSessionValidationContext currentContext)
        {
            return InstallSessionV2Verifier.VerifyAndValidate(
                _coseSign1,
                _expectedKeyId,
                _publicKey,
                currentContext);
        }
    }

    internal static class InstallSessionV2Verifier
    {
        internal const string TokenType = "YUCP-InstallSession";
        internal const long MaximumLifetimeSeconds = 15 * 60;

        internal static InstallSessionV2 VerifyAndValidate(
            byte[] coseSign1,
            byte[] expectedKeyId,
            byte[] publicKey,
            InstallSessionValidationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            byte[] payload = PackageContractV2.VerifySignedPayload(
                coseSign1,
                PackageContractV2.InstallSessionPurpose,
                expectedKeyId,
                publicKey);
            InstallSessionV2 session = Parse(payload);
            if (!string.Equals(
                    session.KeyId,
                    Encoding.UTF8.GetString(expectedKeyId),
                    StringComparison.Ordinal))
                throw new FormatException("InstallSessionV2 key ID does not match its COSE header.");
            Validate(session, context);
            return session;
        }

        internal static VerifiedInstallSessionV2 Resolve(
            byte[] coseSign1,
            byte[] expectedKeyId,
            byte[] publicKey,
            InstallSessionValidationContext context)
        {
            InstallSessionV2 session = VerifyAndValidate(
                coseSign1,
                expectedKeyId,
                publicKey,
                context);
            return new VerifiedInstallSessionV2(
                coseSign1,
                expectedKeyId,
                publicKey,
                session);
        }

        private static InstallSessionV2 Parse(byte[] payload)
        {
            try
            {
                return ParseCanonical(payload);
            }
            catch (CborContentException exception)
            {
                throw new FormatException(
                    "InstallSessionV2 contains invalid CBOR.",
                    exception);
            }
        }

        private static InstallSessionV2 ParseCanonical(byte[] payload)
        {
            var reader = new CborReader(
                payload,
                CborConformanceMode.Canonical,
                allowMultipleRootLevelValues: false);
            PackageContractV2.RequireLength(reader.ReadStartMap(), 21, "InstallSessionV2 map");

            PackageContractV2.RequireLabel(reader, 0);
            if (reader.ReadInt32() != PackageContractV2.Version)
                throw new FormatException("InstallSessionV2 schema version is invalid.");

            var session = new InstallSessionV2();
            PackageContractV2.RequireLabel(reader, 1);
            session.TokenType = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 2);
            session.Issuer = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 3);
            session.Audience = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 4);
            session.KeyId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 5);
            session.CreatorId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 6);
            session.BuyerId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 7);
            session.ProductId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 8);
            session.Version = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 9);
            session.AliasId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 10);
            session.ReleaseRoot = ReadDigest(reader, "release root");
            PackageContractV2.RequireLabel(reader, 11);
            session.BindingRoot = ReadDigest(reader, "binding root");
            PackageContractV2.RequireLabel(reader, 12);
            session.DeviceKeyThumbprint = ReadDigest(reader, "device key thumbprint");
            PackageContractV2.RequireLabel(reader, 13);
            session.AllowedApiOrigins = ReadTextArray(reader, "allowed API origins");
            PackageContractV2.RequireLabel(reader, 14);
            session.AllowedArtifactOrigins = ReadTextArray(reader, "allowed artifact origins");
            PackageContractV2.RequireLabel(reader, 15);
            session.Bootstrap = ReadBootstrap(reader);
            PackageContractV2.RequireLabel(reader, 16);
            session.IssuedAt = ReadNonnegativeInteger(reader, "issued-at");
            PackageContractV2.RequireLabel(reader, 17);
            session.NotBefore = ReadNonnegativeInteger(reader, "not-before");
            PackageContractV2.RequireLabel(reader, 18);
            session.ExpiresAt = ReadNonnegativeInteger(reader, "expires-at");
            PackageContractV2.RequireLabel(reader, 19);
            session.SessionId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 20);
            session.MaxLifetimeSeconds = ReadNonnegativeInteger(reader, "maximum lifetime");
            reader.ReadEndMap();
            PackageContractV2.RequireFinished(reader, "InstallSessionV2");
            return session;
        }

        private static InstallSessionBootstrapV2[] ReadBootstrap(CborReader reader)
        {
            int? count = reader.ReadStartArray();
            if (!count.HasValue || count.Value < 1 || count.Value > 16)
                throw new FormatException("InstallSessionV2 bootstrap count must be between 1 and 16.");
            var entries = new InstallSessionBootstrapV2[count.Value];
            for (int index = 0; index < entries.Length; index++)
            {
                PackageContractV2.RequireLength(
                    reader.ReadStartMap(),
                    3,
                    $"InstallSessionV2 bootstrap {index}");
                PackageContractV2.RequireLabel(reader, 0);
                string kind = reader.ReadTextString();
                PackageContractV2.RequireLabel(reader, 1);
                string url = reader.ReadTextString();
                PackageContractV2.RequireLabel(reader, 2);
                byte[] sha256 = ReadDigest(reader, $"bootstrap {index} digest");
                reader.ReadEndMap();
                entries[index] = new InstallSessionBootstrapV2
                {
                    Kind = kind,
                    Url = url,
                    Sha256 = sha256,
                };
            }
            reader.ReadEndArray();
            return entries;
        }

        private static string[] ReadTextArray(CborReader reader, string name)
        {
            int? count = reader.ReadStartArray();
            if (!count.HasValue || count.Value < 1)
                throw new FormatException($"InstallSessionV2 {name} must not be empty.");
            var values = new string[count.Value];
            for (int index = 0; index < values.Length; index++)
                values[index] = reader.ReadTextString();
            reader.ReadEndArray();
            return values;
        }

        private static byte[] ReadDigest(CborReader reader, string name)
        {
            byte[] digest = reader.ReadByteString();
            if (digest.Length != 32)
                throw new FormatException($"InstallSessionV2 {name} must contain 32 bytes.");
            return digest;
        }

        private static long ReadNonnegativeInteger(CborReader reader, string name)
        {
            ulong value = reader.ReadUInt64();
            if (value > long.MaxValue)
                throw new FormatException($"InstallSessionV2 {name} exceeds the supported range.");
            return (long)value;
        }

        private static void Validate(
            InstallSessionV2 session,
            InstallSessionValidationContext context)
        {
            if (!string.Equals(session.TokenType, TokenType, StringComparison.Ordinal))
                throw new FormatException("InstallSessionV2 token type is invalid.");
            RequireText(session.AliasId, "alias ID");
            RequireText(session.Audience, "audience");
            RequireText(session.BuyerId, "buyer ID");
            RequireText(session.CreatorId, "creator ID");
            RequireText(session.Issuer, "issuer");
            RequireText(session.KeyId, "key ID");
            RequireText(session.ProductId, "product ID");
            RequireText(session.SessionId, "session ID");
            RequireText(session.Version, "version");

            string[] apiOrigins = NormalizeOrigins(session.AllowedApiOrigins, "API origins");
            string[] artifactOrigins = NormalizeOrigins(
                session.AllowedArtifactOrigins,
                "artifact origins");
            var allowedOrigins = new HashSet<string>(
                apiOrigins.Concat(artifactOrigins),
                StringComparer.Ordinal);
            foreach (InstallSessionBootstrapV2 bootstrap in session.Bootstrap)
            {
                RequireText(bootstrap.Kind, "bootstrap kind");
                if (!Uri.TryCreate(bootstrap.Url, UriKind.Absolute, out Uri url) ||
                    !allowedOrigins.Contains(url.GetLeftPart(UriPartial.Authority)) ||
                    !string.IsNullOrEmpty(url.UserInfo) ||
                    !string.IsNullOrEmpty(url.Fragment))
                    throw new FormatException("InstallSessionV2 bootstrap URL is outside allowed origins.");
            }

            if (session.NotBefore < session.IssuedAt || session.ExpiresAt <= session.NotBefore)
                throw new FormatException("InstallSessionV2 time claims are invalid.");
            if (session.MaxLifetimeSeconds < 1 ||
                session.MaxLifetimeSeconds > MaximumLifetimeSeconds ||
                session.ExpiresAt - session.IssuedAt > session.MaxLifetimeSeconds)
                throw new FormatException("InstallSessionV2 lifetime exceeds its bounded policy.");
            if (context.Now < session.NotBefore || context.Now >= session.ExpiresAt)
                throw new FormatException("InstallSessionV2 is not active.");

            if (!string.Equals(session.AliasId, context.AliasId, StringComparison.Ordinal) ||
                !string.Equals(session.Audience, context.Audience, StringComparison.Ordinal) ||
                !string.Equals(session.Issuer, context.Issuer, StringComparison.Ordinal) ||
                !PackageContractV2.FixedTimeEquals(session.ReleaseRoot, context.ReleaseRoot) ||
                !PackageContractV2.FixedTimeEquals(session.BindingRoot, context.BindingRoot) ||
                !PackageContractV2.FixedTimeEquals(
                    session.DeviceKeyThumbprint,
                    context.DeviceKeyThumbprint))
                throw new FormatException("InstallSessionV2 is not bound to the requested install.");

            if (!apiOrigins.SequenceEqual(
                    NormalizeOrigins(context.AllowedApiOrigins, "expected API origins"),
                    StringComparer.Ordinal) ||
                !artifactOrigins.SequenceEqual(
                    NormalizeOrigins(context.AllowedArtifactOrigins, "expected artifact origins"),
                    StringComparer.Ordinal))
                throw new FormatException("InstallSessionV2 origin binding is invalid.");
        }

        private static string[] NormalizeOrigins(string[] values, string name)
        {
            if (values == null || values.Length == 0)
                throw new FormatException($"InstallSessionV2 {name} must not be empty.");
            var normalized = new string[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                if (!Uri.TryCreate(values[index], UriKind.Absolute, out Uri uri) ||
                    !string.IsNullOrEmpty(uri.UserInfo) ||
                    !string.IsNullOrEmpty(uri.Query) ||
                    !string.IsNullOrEmpty(uri.Fragment) ||
                    uri.AbsolutePath != "/" ||
                    !IsAllowedOriginScheme(uri))
                    throw new FormatException($"InstallSessionV2 {name} contains an invalid origin.");
                normalized[index] = uri.GetLeftPart(UriPartial.Authority);
            }
            if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
                throw new FormatException($"InstallSessionV2 {name} contains duplicate origins.");
            return normalized;
        }

        private static bool IsAllowedOriginScheme(Uri uri)
        {
            if (uri.Scheme == Uri.UriSchemeHttps)
                return true;
            if (uri.Scheme != Uri.UriSchemeHttp)
                return false;
            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) ||
                   string.Equals(uri.Host, "::1", StringComparison.Ordinal);
        }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
                throw new FormatException($"InstallSessionV2 {name} must contain 1 through 512 characters.");
        }
    }

    internal sealed class MaterializedFileV2
    {
        internal string AttributionId;
        internal string NormalizedPath;
        internal long OutputBytes;
        internal byte[] OutputSha256;
    }

    internal sealed class ExactRenditionVersionV2
    {
        internal string BucketName;
        internal string FileIdentifier;
        internal long ObjectBytes;
        internal string ObjectKey;
        internal byte[] ObjectSha256;
        internal string ProviderVersion;
        internal string StorageRole;
    }

    internal sealed class MaterializationReceiptV2
    {
        internal string BuyerSubjectPseudonym;
        internal string CapabilityId;
        internal string CodecBuild;
        internal string[] CreatedPaths;
        internal string CreatorId;
        internal long ExpiresAt;
        internal string GrantId;
        internal string HelperBuild;
        internal long IssuedAt;
        internal string JobId;
        internal long KeyEpoch;
        internal long LeaseGeneration;
        internal string MaterializationAlgorithm;
        internal string MaterializerId;
        internal MaterializedFileV2[] OutputFiles;
        internal byte[] OutputTreeRoot;
        internal string PluginVersion;
        internal string ProductId;
        internal byte[] ProtectedSourceRoot;
        internal string PseudonymMethod;
        internal string ReceiptId;
        internal byte[] ReleaseRoot;
        internal ExactRenditionVersionV2 Rendition;
        internal string RuntimeBuild;
        internal string TraceId;
    }

    internal sealed class MaterializationReceiptValidationContext
    {
        internal long Now;
        internal string ProductId;
        internal byte[] ReleaseRoot;
        internal long RenditionBytes;
        internal byte[] RenditionSha256;
    }

    internal static class MaterializationReceiptV2Verifier
    {
        internal static MaterializationReceiptV2 VerifyAndValidate(
            byte[] coseSign1,
            byte[] expectedKeyId,
            byte[] publicKey,
            MaterializationReceiptValidationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            byte[] payload = PackageContractV2.VerifySignedPayload(
                coseSign1,
                PackageContractV2.MaterializationReceiptPurpose,
                expectedKeyId,
                publicKey);
            MaterializationReceiptV2 receipt = Parse(payload);
            Validate(receipt, context);
            return receipt;
        }

        private static MaterializationReceiptV2 Parse(byte[] payload)
        {
            try
            {
                return ParseCanonical(payload);
            }
            catch (CborContentException exception)
            {
                throw new FormatException(
                    "MaterializationReceiptV2 contains invalid CBOR.",
                    exception);
            }
        }

        private static MaterializationReceiptV2 ParseCanonical(byte[] payload)
        {
            var reader = new CborReader(
                payload,
                CborConformanceMode.Canonical,
                allowMultipleRootLevelValues: false);
            PackageContractV2.RequireLength(
                reader.ReadStartMap(),
                26,
                "MaterializationReceiptV2 map");

            PackageContractV2.RequireLabel(reader, 0);
            if (reader.ReadInt32() != PackageContractV2.Version)
                throw new FormatException("MaterializationReceiptV2 schema version is invalid.");

            var receipt = new MaterializationReceiptV2();
            PackageContractV2.RequireLabel(reader, 1);
            receipt.ReceiptId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 2);
            receipt.CapabilityId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 3);
            receipt.CreatorId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 4);
            receipt.BuyerSubjectPseudonym = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 5);
            receipt.PseudonymMethod = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 6);
            receipt.ProductId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 7);
            receipt.ReleaseRoot = ReadDigest(reader, "release root");
            PackageContractV2.RequireLabel(reader, 8);
            receipt.ProtectedSourceRoot = ReadDigest(reader, "protected source root");
            PackageContractV2.RequireLabel(reader, 9);
            receipt.OutputTreeRoot = ReadDigest(reader, "output tree root");
            PackageContractV2.RequireLabel(reader, 10);
            receipt.OutputFiles = ReadOutputFiles(reader);
            PackageContractV2.RequireLabel(reader, 11);
            receipt.GrantId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 12);
            receipt.JobId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 13);
            receipt.LeaseGeneration = ReadNonnegativeInteger(reader, "lease generation");
            PackageContractV2.RequireLabel(reader, 14);
            receipt.Rendition = ReadRendition(reader);
            PackageContractV2.RequireLabel(reader, 15);
            receipt.MaterializationAlgorithm = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 16);
            receipt.PluginVersion = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 17);
            receipt.CodecBuild = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 18);
            receipt.KeyEpoch = ReadNonnegativeInteger(reader, "key epoch");
            PackageContractV2.RequireLabel(reader, 19);
            receipt.HelperBuild = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 20);
            receipt.RuntimeBuild = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 21);
            receipt.CreatedPaths = ReadTextArray(reader, "created paths");
            PackageContractV2.RequireLabel(reader, 22);
            receipt.IssuedAt = ReadNonnegativeInteger(reader, "issued-at");
            PackageContractV2.RequireLabel(reader, 23);
            receipt.ExpiresAt = ReadNonnegativeInteger(reader, "expires-at");
            PackageContractV2.RequireLabel(reader, 24);
            receipt.MaterializerId = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 25);
            receipt.TraceId = reader.ReadTextString();
            reader.ReadEndMap();
            PackageContractV2.RequireFinished(reader, "MaterializationReceiptV2");
            return receipt;
        }

        private static MaterializedFileV2[] ReadOutputFiles(CborReader reader)
        {
            int? count = reader.ReadStartArray();
            if (!count.HasValue || count.Value < 1 || count.Value > 512)
                throw new FormatException(
                    "MaterializationReceiptV2 output file count must be between 1 and 512.");

            var files = new MaterializedFileV2[count.Value];
            for (int index = 0; index < files.Length; index++)
            {
                PackageContractV2.RequireLength(
                    reader.ReadStartMap(),
                    4,
                    $"MaterializationReceiptV2 output file {index}");
                PackageContractV2.RequireLabel(reader, 0);
                string normalizedPath = reader.ReadTextString();
                PackageContractV2.RequireLabel(reader, 1);
                byte[] outputSha256 = ReadDigest(reader, $"output file {index} digest");
                PackageContractV2.RequireLabel(reader, 2);
                long outputBytes = ReadNonnegativeInteger(
                    reader,
                    $"output file {index} byte count");
                PackageContractV2.RequireLabel(reader, 3);
                string attributionId = reader.ReadTextString();
                reader.ReadEndMap();
                files[index] = new MaterializedFileV2
                {
                    AttributionId = attributionId,
                    NormalizedPath = normalizedPath,
                    OutputBytes = outputBytes,
                    OutputSha256 = outputSha256,
                };
            }
            reader.ReadEndArray();
            return files;
        }

        private static ExactRenditionVersionV2 ReadRendition(CborReader reader)
        {
            PackageContractV2.RequireLength(
                reader.ReadStartMap(),
                7,
                "MaterializationReceiptV2 rendition");
            PackageContractV2.RequireLabel(reader, 0);
            string storageRole = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 1);
            string bucketName = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 2);
            string objectKey = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 3);
            string providerVersion = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 4);
            string fileIdentifier = reader.ReadTextString();
            PackageContractV2.RequireLabel(reader, 5);
            byte[] objectSha256 = ReadDigest(reader, "rendition digest");
            PackageContractV2.RequireLabel(reader, 6);
            long objectBytes = ReadNonnegativeInteger(reader, "rendition byte count");
            reader.ReadEndMap();
            return new ExactRenditionVersionV2
            {
                BucketName = bucketName,
                FileIdentifier = fileIdentifier,
                ObjectBytes = objectBytes,
                ObjectKey = objectKey,
                ObjectSha256 = objectSha256,
                ProviderVersion = providerVersion,
                StorageRole = storageRole,
            };
        }

        private static string[] ReadTextArray(CborReader reader, string name)
        {
            int? count = reader.ReadStartArray();
            if (!count.HasValue || count.Value < 1 || count.Value > 512)
                throw new FormatException(
                    $"MaterializationReceiptV2 {name} count must be between 1 and 512.");
            var values = new string[count.Value];
            for (int index = 0; index < values.Length; index++)
                values[index] = reader.ReadTextString();
            reader.ReadEndArray();
            return values;
        }

        private static byte[] ReadDigest(CborReader reader, string name)
        {
            byte[] digest = reader.ReadByteString();
            if (digest.Length != 32)
                throw new FormatException(
                    $"MaterializationReceiptV2 {name} must contain 32 bytes.");
            return digest;
        }

        private static long ReadNonnegativeInteger(CborReader reader, string name)
        {
            ulong value = reader.ReadUInt64();
            if (value > long.MaxValue)
                throw new FormatException(
                    $"MaterializationReceiptV2 {name} exceeds the supported range.");
            return (long)value;
        }

        private static void Validate(
            MaterializationReceiptV2 receipt,
            MaterializationReceiptValidationContext context)
        {
            RequireText(receipt.ReceiptId, "receipt ID");
            RequireText(receipt.CapabilityId, "capability ID");
            RequireText(receipt.CreatorId, "creator ID");
            RequireText(receipt.BuyerSubjectPseudonym, "buyer pseudonym");
            RequireText(receipt.PseudonymMethod, "pseudonym method");
            RequireText(receipt.ProductId, "product ID");
            RequireText(receipt.GrantId, "grant ID");
            RequireText(receipt.JobId, "job ID");
            RequireText(receipt.MaterializationAlgorithm, "materialization algorithm");
            RequireText(receipt.PluginVersion, "plugin version");
            RequireText(receipt.CodecBuild, "codec build");
            RequireText(receipt.HelperBuild, "helper build");
            RequireText(receipt.RuntimeBuild, "runtime build");
            RequireText(receipt.MaterializerId, "materializer ID");
            RequireText(receipt.TraceId, "trace ID");

            if (receipt.IssuedAt < 0 ||
                receipt.ExpiresAt <= receipt.IssuedAt ||
                context.Now < receipt.IssuedAt ||
                context.Now >= receipt.ExpiresAt)
                throw new FormatException("MaterializationReceiptV2 is not active.");
            if (!string.Equals(receipt.ProductId, context.ProductId, StringComparison.Ordinal) ||
                !PackageContractV2.FixedTimeEquals(receipt.ReleaseRoot, context.ReleaseRoot))
                throw new FormatException(
                    "MaterializationReceiptV2 does not match the requested release.");
            if (receipt.Rendition == null ||
                !string.Equals(receipt.Rendition.StorageRole, "renditions", StringComparison.Ordinal) ||
                receipt.Rendition.ObjectBytes != context.RenditionBytes ||
                !PackageContractV2.FixedTimeEquals(
                    receipt.Rendition.ObjectSha256,
                    context.RenditionSha256))
                throw new FormatException(
                    "MaterializationReceiptV2 does not match the downloaded rendition.");

            ValidateOutputFiles(receipt);
        }

        private static void ValidateOutputFiles(MaterializationReceiptV2 receipt)
        {
            if (receipt.OutputFiles == null ||
                receipt.CreatedPaths == null ||
                receipt.OutputFiles.Length != receipt.CreatedPaths.Length)
                throw new FormatException(
                    "MaterializationReceiptV2 created paths do not match its output files.");

            for (int index = 0; index < receipt.OutputFiles.Length; index++)
            {
                MaterializedFileV2 file = receipt.OutputFiles[index];
                RequireMaterializedPath(file.NormalizedPath);
                RequireText(file.AttributionId, "attribution ID");
                if (!string.Equals(
                        receipt.CreatedPaths[index],
                        file.NormalizedPath,
                        StringComparison.Ordinal))
                    throw new FormatException(
                        "MaterializationReceiptV2 created paths do not match its output files.");
                if (index > 0 &&
                    CompareUtf8(
                        receipt.OutputFiles[index - 1].NormalizedPath,
                        file.NormalizedPath) >= 0)
                    throw new FormatException(
                        "MaterializationReceiptV2 output files are not strictly ordered.");
            }

            byte[] computedRoot = PackageContractV2.HashFields(
                "yucp:output-tree:v2",
                receipt.OutputFiles
                    .SelectMany(file => new[]
                    {
                        Encoding.UTF8.GetBytes(file.NormalizedPath),
                        file.OutputSha256,
                        EncodeUnsignedBigEndian((ulong)file.OutputBytes),
                    })
                    .ToArray());
            if (!PackageContractV2.FixedTimeEquals(receipt.OutputTreeRoot, computedRoot))
                throw new FormatException(
                    "MaterializationReceiptV2 output tree root is invalid.");
        }

        private static int CompareUtf8(string left, string right)
        {
            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);
            int commonLength = Math.Min(leftBytes.Length, rightBytes.Length);
            for (int index = 0; index < commonLength; index++)
            {
                int comparison = leftBytes[index].CompareTo(rightBytes[index]);
                if (comparison != 0)
                    return comparison;
            }
            return leftBytes.Length.CompareTo(rightBytes.Length);
        }

        private static byte[] EncodeUnsignedBigEndian(ulong value)
        {
            var bytes = new byte[8];
            for (int index = bytes.Length - 1; index >= 0; index--)
            {
                bytes[index] = (byte)(value & 0xff);
                value >>= 8;
            }
            return bytes;
        }

        private static void RequireMaterializedPath(string value)
        {
            RequireText(value, "output path");
            if (value.Length > 1024 ||
                value.StartsWith("/", StringComparison.Ordinal) ||
                value.Contains("\\") ||
                value.Split('/').Any(segment =>
                    segment.Length == 0 ||
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal)))
                throw new FormatException(
                    "MaterializationReceiptV2 output path is not normalized.");
        }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
                throw new FormatException(
                    $"MaterializationReceiptV2 {name} must contain 1 through 512 characters.");
        }
    }
}
