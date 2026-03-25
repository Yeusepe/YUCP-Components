using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Short-lived local HTTP relay that holds the browser connection open after OAuth sign-in
    /// and issues a 302 redirect to the Gumroad verification URL once the intent is created.
    ///
    /// Flow:
    ///   1. <see cref="Start"/> allocates a port and returns the relay URL.
    ///   2. The OAuth success page redirects the browser to that URL.
    ///   3. The relay waits (up to 30 s) for <see cref="SetVerifyUrl"/> to be called.
    ///   4. When Unity creates the verification intent it calls <see cref="SetVerifyUrl"/>;
    ///      the relay immediately 302-redirects the browser to the Gumroad verification page.
    /// </summary>
    internal static class PendingVerifyRelay
    {
        private static HttpListener _listener;
        private static TaskCompletionSource<string> _urlTcs;

        internal static string Start()
        {
            Cancel();
            int port = FindFreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _urlTcs = new TaskCompletionSource<string>();
            _ = ServeLoopAsync(_listener, _urlTcs);
            return $"http://127.0.0.1:{port}/";
        }

        internal static void SetVerifyUrl(string url)
        {
            _urlTcs?.TrySetResult(url);
        }

        internal static void Cancel()
        {
            _urlTcs?.TrySetCanceled();
            _urlTcs = null;
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        private static async Task ServeLoopAsync(HttpListener listener, TaskCompletionSource<string> tcs)
        {
            try
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await listener.GetContextAsync(); }
                    catch { break; }
                    _ = HandleAsync(ctx, tcs, listener);
                }
            }
            catch { }
        }

        private static async Task HandleAsync(
            HttpListenerContext ctx,
            TaskCompletionSource<string> tcs,
            HttpListener listener)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Task<string> urlTask = tcs.Task;
                Task timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);
                Task done = await Task.WhenAny(urlTask, timeoutTask);
                cts.Cancel();

                if (done == urlTask && urlTask.Status == TaskStatus.RanToCompletion)
                {
                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers["Location"] = urlTask.Result;
                    ctx.Response.ContentLength64 = 0;
                }
                else
                {
                    byte[] body = Encoding.UTF8.GetBytes(
                        "<html><body><p>Verification setup timed out. Return to Unity and try again.</p></body></html>");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = body.Length;
                    await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Relay] Request error: {ex.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.ContentLength64 = 0; } catch { }
            }
            finally
            {
                try { ctx.Response.Close(); } catch { }
                // Single-use: stop accepting after first request
                try { listener.Stop(); } catch { }
            }
        }

        private static int FindFreePort()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }
    }
}
