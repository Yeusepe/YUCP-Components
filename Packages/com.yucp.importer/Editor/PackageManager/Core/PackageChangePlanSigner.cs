using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Authenticates the exact project-local classification reviewed by the
    /// user. The delivery inventory remains bound to server-signed package
    /// contracts; this session key additionally prevents a reviewed plan from
    /// being altered before project mutation.
    /// </summary>
    internal static class PackageChangePlanSigner
    {
        internal const string Algorithm = "hmac-sha256-session-v1";

        private static readonly byte[] SessionKey = CreateSessionKey();
        private static readonly string SessionKeyId =
            Sha256Hex(SessionKey);

        internal static void Sign(PackageChangePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            if (string.IsNullOrWhiteSpace(plan.reviewDigest))
            {
                throw new InvalidOperationException(
                    "The package change plan digest is unavailable.");
            }

            plan.signatureAlgorithm = Algorithm;
            plan.signerKeyId = SessionKeyId;
            plan.signature = Convert.ToBase64String(
                ComputeMac(plan.reviewDigest));
        }

        internal static bool Verify(PackageChangePlan plan)
        {
            if (plan == null ||
                !string.Equals(
                    plan.signatureAlgorithm,
                    Algorithm,
                    StringComparison.Ordinal) ||
                !FixedTimeEquals(
                    plan.signerKeyId,
                    SessionKeyId) ||
                !FixedTimeEquals(
                    plan.reviewDigest,
                    PackageChangePlanBuilder.ComputeReviewDigest(plan)))
            {
                return false;
            }

            byte[] supplied;
            try
            {
                supplied = Convert.FromBase64String(
                    plan.signature ?? string.Empty);
            }
            catch (FormatException)
            {
                return false;
            }
            return FixedTimeEquals(
                supplied,
                ComputeMac(plan.reviewDigest));
        }

        internal static bool VerifyApproval(
            PackageChangePlan plan,
            string approvedDigest,
            string approvedSignature)
        {
            return Verify(plan) &&
                FixedTimeEquals(
                    plan.reviewDigest,
                    approvedDigest ?? string.Empty) &&
                FixedTimeEquals(
                    plan.signature,
                    approvedSignature ?? string.Empty);
        }

        private static byte[] ComputeMac(string reviewDigest)
        {
            string payload =
                "yucp-package-change-plan-v1\n" +
                SessionKeyId + "\n" +
                reviewDigest;
            using (var hmac = new HMACSHA256(SessionKey))
            {
                return hmac.ComputeHash(
                    Encoding.UTF8.GetBytes(payload));
            }
        }

        private static byte[] CreateSessionKey()
        {
            var key = new byte[32];
            using (RandomNumberGenerator random =
                RandomNumberGenerator.Create())
            {
                random.GetBytes(key);
            }
            return key;
        }

        private static string Sha256Hex(byte[] value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return string.Concat(
                    sha256.ComputeHash(value)
                        .Select(item => item.ToString("x2")));
            }
        }

        private static bool FixedTimeEquals(
            string left,
            string right)
        {
            return FixedTimeEquals(
                Encoding.UTF8.GetBytes(left ?? string.Empty),
                Encoding.UTF8.GetBytes(right ?? string.Empty));
        }

        private static bool FixedTimeEquals(
            byte[] left,
            byte[] right)
        {
            int leftLength = left?.Length ?? 0;
            int rightLength = right?.Length ?? 0;
            int length = Math.Max(leftLength, rightLength);
            int difference = leftLength ^ rightLength;
            for (int index = 0; index < length; index++)
            {
                byte leftValue = index < leftLength
                    ? left[index]
                    : (byte)0;
                byte rightValue = index < rightLength
                    ? right[index]
                    : (byte)0;
                difference |= leftValue ^ rightValue;
            }
            return difference == 0;
        }
    }
}
