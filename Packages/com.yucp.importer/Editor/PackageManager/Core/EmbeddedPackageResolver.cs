using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class EmbeddedPackageResolver
    {
        private static readonly TimeSpan ResolveTimeout =
            TimeSpan.FromMinutes(5);

        internal static bool RequiresResolution(
            IEnumerable<VerifiedStagingFile> targetFiles,
            IEnumerable<VerifiedStagingFile> previousFiles)
        {
            Dictionary<string, string> target = Descriptors(targetFiles);
            Dictionary<string, string> previous = Descriptors(previousFiles);
            return target.Count != previous.Count ||
                target.Any(pair =>
                    !previous.TryGetValue(pair.Key, out string priorSha256) ||
                    !string.Equals(
                        pair.Value,
                        priorSha256,
                        StringComparison.Ordinal));
        }

        internal static bool IsEmbeddedPackageDescriptorPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            string[] segments = path.Split('/');
            return segments.Length == 3 &&
                string.Equals(
                    segments[0],
                    "Packages",
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(segments[1]) &&
                string.Equals(
                    segments[2],
                    "package.json",
                    StringComparison.Ordinal);
        }

        // Unity 2022.3 Package Manager Client.Resolve reference:
        // https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PackageManager.Client.Resolve.html
        // Unity 2022.3 registeredPackages completion event reference:
        // https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PackageManager.Events-registeredPackages.html
        internal static Task ResolveAsync()
        {
            var completion = new TaskCompletionSource<bool>();
            DateTime deadline = DateTime.UtcNow + ResolveTimeout;
            ListRequest request = null;
            void OnRegisteredPackages(PackageRegistrationEventArgs _)
            {
                if (request == null)
                {
                    request = Client.List(false, true);
                }
            }
            Events.registeredPackages += OnRegisteredPackages;
            void Poll()
            {
                if ((request == null || !request.IsCompleted) &&
                    DateTime.UtcNow < deadline)
                {
                    return;
                }
                EditorApplication.update -= Poll;
                Events.registeredPackages -= OnRegisteredPackages;
                if (request == null || !request.IsCompleted)
                {
                    completion.TrySetException(
                        new TimeoutException(
                            "Unity Package Manager resolution timed out."));
                    return;
                }
                if (request.Status != StatusCode.Success)
                {
                    completion.TrySetException(
                        new InvalidOperationException(
                            "Unity Package Manager resolution failed: " +
                            (request.Error?.message ?? "unknown error")));
                    return;
                }
                completion.TrySetResult(true);
            }
            EditorApplication.update += Poll;
            try
            {
                Client.Resolve();
                Poll();
            }
            catch (Exception exception)
            {
                EditorApplication.update -= Poll;
                Events.registeredPackages -= OnRegisteredPackages;
                completion.TrySetException(exception);
            }
            return completion.Task;
        }

        private static Dictionary<string, string> Descriptors(
            IEnumerable<VerifiedStagingFile> files)
        {
            return (files ?? Enumerable.Empty<VerifiedStagingFile>())
                .Where(file =>
                    file != null &&
                    IsEmbeddedPackageDescriptorPath(file.normalizedPath))
                .ToDictionary(
                    file => file.normalizedPath,
                    file => file.sha256,
                    StringComparer.Ordinal);
        }
    }
}
