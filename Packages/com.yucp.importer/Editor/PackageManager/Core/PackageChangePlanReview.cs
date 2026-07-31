using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal sealed class PackageReviewRequest
    {
        internal string ApproveLabel = "Confirm changes";
        internal string CancelLabel = "Cancel";
        internal IReadOnlyList<string> DirtyAssets = Array.Empty<string>();
        internal string Heading = string.Empty;
        internal PackageChangePlan Plan;
        internal string Summary = string.Empty;
    }

    internal interface IPackageChangePlanReviewHost
    {
        bool CanReview { get; }

        Task<bool> ReviewAsync(PackageReviewRequest request);
    }

    internal static class PackageChangePlanReview
    {
        private static IPackageChangePlanReviewHost _host;
        private static Func<PackageReviewRequest, bool> _fallback;

        internal static IDisposable Register(IPackageChangePlanReviewHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            return new Registration(host);
        }

        internal static void SetFallback(Func<PackageReviewRequest, bool> fallback)
        {
            _fallback = fallback;
        }

        internal static Task<bool> RequestChangePlanAsync(
            PackageChangePlan plan,
            IEnumerable<string> dirtyAssets,
            string targetLabel)
        {
            return RequestAsync(new PackageReviewRequest
            {
                DirtyAssets = (dirtyAssets ?? Enumerable.Empty<string>()).ToList(),
                Heading = "Exact project changes",
                Plan = plan,
                Summary = targetLabel ?? string.Empty,
            });
        }

        internal static Task<bool> RequestApprovalAsync(
            string heading,
            string summary,
            string approveLabel,
            string cancelLabel)
        {
            return RequestAsync(new PackageReviewRequest
            {
                ApproveLabel = approveLabel,
                CancelLabel = cancelLabel,
                Heading = heading ?? string.Empty,
                Summary = summary ?? string.Empty,
            });
        }

        internal static async Task<bool> RequestAsync(PackageReviewRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            IPackageChangePlanReviewHost host = _host;
            if (host != null && host.CanReview)
            {
                return await host.ReviewAsync(request);
            }
            Func<PackageReviewRequest, bool> fallback = _fallback;
            if (fallback == null)
            {
                throw new InvalidOperationException(
                    "No package review surface is available.");
            }
            return fallback(request);
        }

        private sealed class Registration : IDisposable
        {
            private readonly IPackageChangePlanReviewHost _registered;

            internal Registration(IPackageChangePlanReviewHost registered)
            {
                _registered = registered;
            }

            public void Dispose()
            {
                if (ReferenceEquals(_host, _registered))
                {
                    _host = null;
                }
            }
        }
    }
}
