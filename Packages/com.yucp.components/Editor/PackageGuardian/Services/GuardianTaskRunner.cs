using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    /// <summary>
    /// Serializes heavy Package Guardian operations onto a single background worker.
    /// Prevents multiple snapshots/stashes from running simultaneously on the main thread.
    /// </summary>
    [InitializeOnLoad]
    public static class GuardianTaskRunner
    {
        private static readonly ConcurrentQueue<IGuardianTask> _queue = new ConcurrentQueue<IGuardianTask>();
        private static readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);
        private static readonly Thread _workerThread;
        private static volatile string _activeTaskName;

        static GuardianTaskRunner()
        {
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "PackageGuardianWorker",
                Priority = System.Threading.ThreadPriority.BelowNormal
            };
            _workerThread.Start();
        }

        /// <summary>
        /// Enqueue work that should run off the main thread, ensuring only one guardian task executes at a time.
        /// </summary>
        public static Task Run(string taskName, Action<CancellationToken> work, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return Run(taskName, ct =>
            {
                work(ct);
                return true;
            }, token);
        }

        /// <summary>
        /// Enqueue work that returns a value.
        /// </summary>
        public static Task<TResult> Run<TResult>(string taskName, Func<CancellationToken, TResult> work, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            var tcs = new TaskCompletionSource<TResult>();
            var guardianTask = new GuardianTask<TResult>(taskName, work, token, tcs);
            _queue.Enqueue(guardianTask);
            _queueSignal.Set();
            return tcs.Task;
        }

        /// <summary>
        /// Indicates whether a background guardian task is currently running.
        /// </summary>
        public static bool IsBusy => _activeTaskName != null;

        /// <summary>
        /// Name of the currently running task, if any.
        /// </summary>
        public static string ActiveTaskName => _activeTaskName;

        private static void WorkerLoop()
        {
            while (true)
            {
                if (!_queue.TryDequeue(out var task))
                {
                    _queueSignal.WaitOne();
                    continue;
                }

            #pragma warning disable CA1031
                try
                {
                    _activeTaskName = task.Name;
                    task.Execute();
                }
                catch (ThreadAbortException)
                {
                    Debug.LogWarning("[Package Guardian] Background worker aborted (domain reload). Pending task may be cancelled.");
                    _activeTaskName = null;
                    Thread.ResetAbort();
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Package Guardian] Background task '{task.Name}' crashed: {ex.Message}");
                }
                finally
                {
                    _activeTaskName = null;
                }
            #pragma warning restore CA1031
            }
        }

        private interface IGuardianTask
        {
            string Name { get; }
            void Execute();
        }

        private sealed class GuardianTask<TResult> : IGuardianTask
        {
            private readonly Func<CancellationToken, TResult> _work;
            private readonly CancellationToken _token;
            private readonly TaskCompletionSource<TResult> _tcs;

            public string Name { get; }

            public GuardianTask(string name, Func<CancellationToken, TResult> work, CancellationToken token, TaskCompletionSource<TResult> tcs)
            {
                Name = name;
                _work = work;
                _token = token;
                _tcs = tcs;
            }

            public void Execute()
            {
                if (_token.IsCancellationRequested)
                {
                    _tcs.TrySetCanceled(_token);
                    return;
                }

                try
                {
                    var result = _work(_token);
                    _tcs.TrySetResult(result);
                }
                catch (ThreadAbortException)
                {
                    Thread.ResetAbort();
                    _tcs.TrySetCanceled();
                    Debug.LogWarning("[Package Guardian] Task cancelled due to thread abort");
                }
                catch (OperationCanceledException oce)
                {
                    _tcs.TrySetCanceled(oce.CancellationToken == default ? _token : oce.CancellationToken);
                }
                catch (Exception ex)
                {
                    _tcs.TrySetException(ex);
                }
            }
        }
    }
}

