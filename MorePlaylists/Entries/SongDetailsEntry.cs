using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberPlaylistsLib.Legacy;
using BeatSaberPlaylistsLib.Types;
using SiraUtil.Web;
using SongDetailsCache;
using MorePlaylists.Utilities;

namespace MorePlaylists.Entries
{
    internal abstract class SongDetailsEntry : BasicEntry
    {
        private List<Song>? songs;
        private readonly SemaphoreSlim songsSemaphore = new(1, 1);
        public override async Task<List<Song>?> GetSongs(IHttpService siraHttpService, CancellationToken cancellationToken, bool firstPage = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (songs != null) return songs;
            await songsSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (songs != null) return songs;
                if (RemotePlaylist == null)
                {
                    await CachePlaylist(siraHttpService, cancellationToken);
                }

                if (RemotePlaylist != null)
                {
                    var songDetails = await RemoteRequest.Run(_ => SongDetails.Init(), cancellationToken,
                        "Initialize song details", attempts: 1, timeoutSeconds: 45);
                    if (songDetails == null) throw new InvalidOperationException("Song details initialization returned no data.");
                    var loadedSongs = new List<Song>();
                    var playlistSongs = RemotePlaylist.Distinct(IPlaylistSongComparer<LegacyPlaylistSong>.Default).ToList();
                    for (var i = 0; i < playlistSongs.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (songDetails.songs.FindByHash(playlistSongs[i].Hash, out var song))
                        {
                            loadedSongs.Add(new Song(song.songName, $"{song.songAuthorName} [{song.levelAuthorName}]", song.coverURL));
                        }
                    }   
                    cancellationToken.ThrowIfCancellationRequested();
                    songs = loadedSongs;
                }
                return songs ?? throw new InvalidOperationException("The remote playlist was unavailable.");
            }
            finally { songsSemaphore.Release(); }
        }
    }
}
