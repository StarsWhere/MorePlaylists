using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberPlaylistsLib.Types;
using HMUI;
using MorePlaylists.Entries;
using PlaylistManager.UI;
using PlaylistManager.Utilities;
using SiraUtil.Web;
using Zenject;
using PlaylistLibUtils = MorePlaylists.Utilities.PlaylistLibUtils;

namespace MorePlaylists.UI
{
    internal class MorePlaylistsDownloaderViewController : ViewController, IDisposable
    {
        [Inject]
        private readonly IHttpService siraHttpService = null!;

        [Inject]
        private readonly PlaylistDownloader playlistDownloader = null!;

        [Inject]
        private readonly PlaylistDownloaderViewController playlistDownloaderViewController = null!;

        private sealed class DownloadOperation : IDisposable
        {
            private int disposed;

            public DownloadOperation(CancellationTokenSource cancellationSource, bool downloadSongs)
            {
                CancellationSource = cancellationSource;
                DownloadSongs = downloadSongs;
            }

            public CancellationTokenSource CancellationSource { get; }
            public bool DownloadSongs { get; }
            public Task? Task { get; set; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    CancellationSource.Dispose();
                }
            }
        }

        private readonly object operationLock = new();
        private readonly Dictionary<IEntry, DownloadOperation> activeDownloads = new();
        private readonly CancellationTokenSource lifetimeCancellation = new();
        private int disposed;

        public event Action<IEntry>? PlaylistDownloaded;
        public event Action<IEntry, Exception>? PlaylistDownloadFailed;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            playlistDownloaderViewController.SetParent(transform);
        }

        public void DownloadPlaylist(IEntry entry, bool downloadSongs)
        {
            DownloadOperation operation;
            lock (operationLock)
            {
                if (Volatile.Read(ref disposed) != 0 || entry.DownloadBlocked || activeDownloads.ContainsKey(entry))
                {
                    return;
                }

                var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
                cancellationSource.CancelAfter(TimeSpan.FromSeconds(90));
                operation = new DownloadOperation(cancellationSource, downloadSongs);
                activeDownloads.Add(entry, operation);
                entry.DownloadBlocked = true;
            }

            operation.Task = StartDownload(entry, operation);
        }

        private async Task StartDownload(IEntry entry, DownloadOperation operation)
        {
            IPlaylist? playlist = null;
            Exception? failure = null;
            try
            {
                playlist = await entry.DownloadPlaylist(siraHttpService, operation.CancellationSource.Token);
                operation.CancellationSource.Token.ThrowIfCancellationRequested();
                if (playlist == null)
                {
                    throw new InvalidOperationException("The playlist download returned no playlist data.");
                }
            }
            catch (OperationCanceledException exception)
            {
                if (Volatile.Read(ref disposed) != 0 || lifetimeCancellation.IsCancellationRequested)
                {
                    CleanupOperation(entry, operation);
                    return;
                }

                failure = new TimeoutException("Playlist download timed out after 90 seconds.", exception);
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref disposed) != 0 || lifetimeCancellation.IsCancellationRequested)
                {
                    CleanupOperation(entry, operation);
                    return;
                }

                failure = exception;
                Plugin.Log?.Error($"Unable to download playlist '{entry.Title}': {exception}");
            }

            if (Volatile.Read(ref disposed) != 0 || lifetimeCancellation.IsCancellationRequested)
            {
                CleanupOperation(entry, operation);
                return;
            }

            try
            {
                await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => CompleteDownload(entry, operation, playlist, failure));
            }
            catch (Exception exception)
            {
                Plugin.Log?.Error($"Unable to finish playlist download '{entry.Title}': {exception}");
                CleanupOperation(entry, operation);
            }
        }

        private void CompleteDownload(IEntry playlistEntry, DownloadOperation operation, IPlaylist? playlist, Exception? failure)
        {
            if (Volatile.Read(ref disposed) != 0 || lifetimeCancellation.IsCancellationRequested)
            {
                CleanupOperation(playlistEntry, operation);
                return;
            }
            if (!TakeOperation(playlistEntry, operation))
            {
                return;
            }

            if (failure == null && playlist != null)
            {
                try
                {
                    PlaylistLibUtils.SavePlaylist(playlistEntry, playlist);
                    if (operation.DownloadSongs)
                    {
                        playlistDownloader.QueuePlaylist(new PlaylistManager.Types.DownloadQueueEntry(playlist, BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.GetManagerForPlaylist(playlist)));
                    }

                    InvokePlaylistDownloaded(playlistEntry);
                    return;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    Plugin.Log?.Error($"Unable to save playlist '{playlistEntry.Title}': {exception}");
                }
            }

            playlistEntry.DownloadBlocked = false;
            InvokePlaylistDownloadFailed(playlistEntry, failure ?? new InvalidOperationException("The playlist download did not produce a playlist."));
        }

        private bool TakeOperation(IEntry entry, DownloadOperation operation)
        {
            lock (operationLock)
            {
                if (!activeDownloads.TryGetValue(entry, out var activeOperation) || !ReferenceEquals(activeOperation, operation))
                {
                    return false;
                }

                activeDownloads.Remove(entry);
            }

            operation.Dispose();
            return true;
        }

        private void CleanupOperation(IEntry entry, DownloadOperation operation)
        {
            if (TakeOperation(entry, operation)) entry.DownloadBlocked = false;
        }

        private void InvokePlaylistDownloaded(IEntry entry)
        {
            try
            {
                PlaylistDownloaded?.Invoke(entry);
            }
            catch (Exception exception)
            {
                Plugin.Log?.Error($"A playlist download success handler failed for '{entry.Title}': {exception}");
            }
        }

        private void InvokePlaylistDownloadFailed(IEntry entry, Exception exception)
        {
            try
            {
                PlaylistDownloadFailed?.Invoke(entry, exception);
            }
            catch (Exception handlerException)
            {
                Plugin.Log?.Error($"A playlist download failure handler failed for '{entry.Title}': {handlerException}");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            lifetimeCancellation.Cancel();
            lock (operationLock)
            {
                foreach (var operation in activeDownloads.Values)
                {
                    operation.CancellationSource.Cancel();
                }
            }
        }

        protected override void OnDestroy()
        {
            Dispose();
            base.OnDestroy();
        }
    }
}
