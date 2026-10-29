using SiraUtil.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberPlaylistsLib.Types;
using MorePlaylists.Utilities;

namespace MorePlaylists.Entries
{
    internal abstract class BasicEntry : IBasicEntry
    {
        public abstract string FolderName { get; }
        public IPlaylist? LocalPlaylist { get; set; }
        public bool DownloadBlocked { get; set; }
        public bool ExhaustedPages => true;
        
        #region Basic Entry

        private readonly SemaphoreSlim cacheSemaphore = new(1, 1);
        public IPlaylist? RemotePlaylist { get; private set; }

        public async Task CachePlaylist(IHttpService siraHttpService, CancellationToken cancellationToken)
        {
            if (RemotePlaylist != null)
            {
                return;
            }

            await cacheSemaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (RemotePlaylist != null) return;
                var handler = BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.DefaultHandler;
                if (handler == null)
                {
                    throw new InvalidOperationException("No playlist deserializer is registered.");
                }

                var downloaded = await RemoteRequest.GetPlaylist(siraHttpService, PlaylistURL, cancellationToken, handler.Deserialize);
                cancellationToken.ThrowIfCancellationRequested();
                RemotePlaylist = downloaded;
                if (RemotePlaylist == null)
                {
                    throw new InvalidDataException("The playlist response could not be deserialized: " + PlaylistURL);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Plugin.Log?.Error("An exception occurred while acquiring " + PlaylistURL +
                                  $"\nException: {exception}");
                throw;
            }
            finally
            {
                cacheSemaphore.Release();
            }
        }

        
        public async Task<IPlaylist?> DownloadPlaylist(IHttpService siraHttpService, CancellationToken cancellationToken = default)
        {
            if (RemotePlaylist != null)
            {
                return RemotePlaylist;
            }
            
            if (RemotePlaylist == null)
            {
                await CachePlaylist(siraHttpService, cancellationToken);
            }
            return RemotePlaylist ?? throw new InvalidOperationException("The playlist download did not produce a playlist.");
        }
        
        #endregion

        #region Abstract

        public abstract string Title { get; protected set; }
        public abstract string Author { get; protected set; }
        public abstract string Description { get; protected set; }
        public abstract string PlaylistURL { get; protected set; }
        public abstract string SpriteURL { get; protected set; }
        public abstract Task<List<Song>?> GetSongs(IHttpService siraHttpService, CancellationToken cancellationToken, bool firstPage = false);

        #endregion
    }
}
