using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SiraUtil.Web;
using PlaylistManager.Utilities;

namespace MorePlaylists.Utilities
{
    internal static class RemoteRequest
    {
        internal sealed class HttpFailure : IOException
        {
            internal int Status { get; }
            internal HttpFailure(int status, string url) : base($"HTTP {status}: {url}") => Status = status;
        }

        // Some response readers do not accept a token. Bound the caller's wait and
        // observe a late fault (or dispose a late stream) when cancellation wins.
        internal static async Task<T> Await<T>(Task<T> task, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                ObserveAbandoned(task);
                token.ThrowIfCancellationRequested();
            }
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task) != task)
                {
                    ObserveAbandoned(task);
                    token.ThrowIfCancellationRequested();
                }
            }
            if (token.IsCancellationRequested)
            {
                ObserveAbandoned(task);
                token.ThrowIfCancellationRequested();
            }
            return await task;
        }

        private static void ObserveAbandoned<T>(Task<T> task) =>
            _ = task.ContinueWith(completed =>
            {
                if (completed.IsFaulted) _ = completed.Exception;
                else if (completed.Status == TaskStatus.RanToCompletion && completed.Result is Stream stream)
                    stream.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        internal static async Task<T> Run<T>(Func<CancellationToken, Task<T>> request, CancellationToken token,
            string operation, int attempts = 3, int timeoutSeconds = 20)
        {
            for (var attempt = 1; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    return await Await(request(timeout.Token), timeout.Token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception failure)
                {
                    Exception error = failure is OperationCanceledException
                        ? new TimeoutException($"Timed out after {timeoutSeconds} seconds: {operation}", failure)
                        : failure;
                    if (attempt >= attempts || !IsTransient(error))
                        throw new IOException($"{operation} failed after {attempt} attempt(s).", error);
                    Plugin.Log?.Warn($"Retrying {operation} ({attempt}/{attempts}): {error.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), token);
                }
            }
        }

        private static bool IsTransient(Exception error)
        {
            if (error is HttpFailure http)
                return http.Status == 408 || http.Status == 429 || http.Status >= 500;
            return error is IOException || error is WebException || error is TimeoutException ||
                   error.GetType().Name == "HttpRequestException";
        }

        internal static async Task<T?> BeatSaverPage<T>(Func<CancellationToken, Task<T?>> request,
            CancellationToken token, string operation) where T : class
        {
            try { return await Run(request, token, operation, attempts: 1, timeoutSeconds: 100); }
            catch (IOException error) when (error.InnerException is PlaylistHttpException { StatusCode: 404 })
            {
                // BeatSaver uses 404 for an unknown user and for some exhausted pages.
                return null;
            }
        }

        internal static Task<string> GetText(IHttpService service, string url, CancellationToken token) =>
            Run(async requestToken =>
            {
                var response = await Await(service.GetAsync(url, cancellationToken: requestToken), requestToken);
                if (!response.Successful) throw new HttpFailure((int)response.Code, url);
                return await Await(response.ReadAsStringAsync(), requestToken);
            }, token, "Fetch " + url);

        internal static Task<T> GetPlaylist<T>(IHttpService service, string url, CancellationToken token, Func<Stream, T> deserialize) =>
            Run(async requestToken =>
            {
                var response = await Await(service.GetAsync(url, cancellationToken: requestToken), requestToken);
                if (!response.Successful) throw new HttpFailure((int)response.Code, url);
                using var stream = await Await(response.ReadAsStreamAsync(), requestToken);
                requestToken.ThrowIfCancellationRequested();
                return deserialize(stream);
            }, token, "Fetch playlist " + url);
    }
}
