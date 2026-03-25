using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    [InitializeOnLoad]
    internal static class EditorMainThreadDispatcher
    {
        private static readonly int MainThreadId;
        private static readonly SynchronizationContext MainSynchronizationContext;

        static EditorMainThreadDispatcher()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            MainSynchronizationContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        internal static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == MainThreadId;

        internal static void Invoke(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (IsMainThread)
            {
                action();
                return;
            }

            ExceptionDispatchInfo capturedException = null;
            using var done = new ManualResetEventSlim(false);
            MainSynchronizationContext.Post(_ =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    capturedException = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    done.Set();
                }
            }, null);

            done.Wait();
            capturedException?.Throw();
        }

        internal static T Invoke<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (IsMainThread)
                return action();

            T result = default(T);
            ExceptionDispatchInfo capturedException = null;
            using var done = new ManualResetEventSlim(false);
            MainSynchronizationContext.Post(_ =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    capturedException = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    done.Set();
                }
            }, null);

            done.Wait();
            capturedException?.Throw();
            return result;
        }
    }
}
