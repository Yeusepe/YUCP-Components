using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PackageGuardian.Core.Repository;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    internal sealed class SnapshotJob
    {
        private readonly Repository _repository;
        private readonly SnapshotRequest _request;
        private readonly Action<SnapshotProgress> _progress;

        public SnapshotJob(Repository repository, SnapshotRequest request, Action<SnapshotProgress> progress = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _progress = progress;
        }

        public string Execute(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var options = new SnapshotOptions
            {
                Committer = _request.Author,
                IncludeRoots = _request.TrackedRoots,
                CancellationToken = token,
                Progress = (processed, total, path) =>
                {
                    _progress?.Invoke(new SnapshotProgress(processed, total, path));
                }
            };

            return _repository.CreateSnapshot(_request.Message, _request.Author, options);
        }
    }

    internal sealed class SnapshotRequest
    {
        public SnapshotRequest(string message, string author, IEnumerable<string> trackedRoots, bool validateFirst)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Author = author ?? throw new ArgumentNullException(nameof(author));
            TrackedRoots = trackedRoots?.ToArray() ?? Array.Empty<string>();
            ValidateFirst = validateFirst;
        }

        public string Message { get; }
        public string Author { get; }
        public IReadOnlyList<string> TrackedRoots { get; }
        public bool ValidateFirst { get; }
    }

    internal readonly struct SnapshotProgress
    {
        public SnapshotProgress(int processed, int total, string path)
        {
            Processed = processed;
            Total = total;
            Path = path;
        }

        public int Processed { get; }
        public int Total { get; }
        public string Path { get; }
    }
}



