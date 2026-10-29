using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberPlaylistsLib.Types;
using BeatSaverSharp.Models;
using BeatSaverSharp.Models.Pages;
using MorePlaylists.Entries;
using MorePlaylists.Utilities;
using SiraUtil.Web;
using Playlist = BeatSaverSharp.Models.Playlist;

namespace MorePlaylists.BeatSaver
{
     internal class BeatSaverPlaylistEntry : IBeatSaverEntry
     {
        private readonly Playlist playlist;
        private PlaylistDetail? playlistDetail;

        public BeatSaverPlaylistEntry(Playlist playlist)
        {
            this.playlist = playlist;
        }

        public string Title => playlist.Name;
        public string Author => playlist.Owner.Name;
        public string Description => playlist.Description;
        public string PlaylistURL => playlist.DownloadURL;
        public string SpriteURL => playlist.CoverURL;
        public string FolderName => "BeatSaver";
        public IPlaylist? LocalPlaylist { get; set; }
        public bool DownloadBlocked { get; set; }
        public bool ExhaustedPages { get; private set; }
        public User Owner => playlist.Owner;

        public async Task<List<Song>?> GetSongs(IHttpService siraHttpService, CancellationToken cancellationToken = default, bool firstPage = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!firstPage && ExhaustedPages) return new List<Song>();
            PlaylistDetail? nextDetail;
            if (playlistDetail == null || firstPage)
            {
                nextDetail = await RemoteRequest.BeatSaverPage(requestToken => playlist.GetPlaylistDetail(requestToken),
                    cancellationToken, "Fetch songs from " + PlaylistURL);
            }
            else
            {
                nextDetail = await RemoteRequest.BeatSaverPage(requestToken => playlistDetail.Next(requestToken),
                    cancellationToken, "Fetch next song page from " + PlaylistURL);
            }
            cancellationToken.ThrowIfCancellationRequested();
            playlistDetail = nextDetail;
            ExhaustedPages = playlistDetail == null || playlistDetail.Empty;
            if (playlistDetail == null || playlistDetail.Empty)
            {
                return new List<Song>();
            }

            var songs = new List<Song>();
            foreach (var beatmap in playlistDetail.Beatmaps)
            {
                songs.Add(new Song(beatmap.Map.Name, $"{beatmap.Map.Metadata.SongAuthorName} [{beatmap.Map.Metadata.LevelAuthorName}]", beatmap.Map.LatestVersion.CoverURL));
            }
            return songs;
        }

        public async Task<IPlaylist?> DownloadPlaylist(IHttpService siraHttpService, CancellationToken cancellationToken = default)
        { 
            return await RemoteRequest.GetPlaylist(siraHttpService, PlaylistURL, cancellationToken, stream =>
            {
                var handler = BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.DefaultHandler
                    ?? throw new System.InvalidOperationException("No playlist deserializer is registered.");
                return handler.Deserialize(stream) ?? throw new InvalidDataException("The downloaded playlist could not be deserialized: " + PlaylistURL);
            });
        }
    }
}
