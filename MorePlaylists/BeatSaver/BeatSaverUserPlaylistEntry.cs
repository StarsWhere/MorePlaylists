using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberPlaylistsLib.Types;
using BeatSaverSharp.Models;
using BeatSaverSharp.Models.Pages;
using MorePlaylists.Entries;
using MorePlaylists.Utilities;
using SiraUtil.Web;

namespace MorePlaylists.BeatSaver
{
    internal class BeatSaverUserPlaylistEntry : IBeatSaverEntry
    {
        public string Title => $"Maps by {Owner.Name}";
        public string Author => Owner.Name;
        public string Description => $"All maps by {Owner.Name}";
        public string PlaylistURL => $"https://api.beatsaver.com/users/id/{Owner.ID}/playlist";
        public string SpriteURL => Owner.Avatar;
        public string FolderName => "BeatSaver";
        public IPlaylist? LocalPlaylist { get; set; }
        public bool DownloadBlocked { get; set; }
        public bool ExhaustedPages { get; private set; }
        public User Owner { get; }

        private Page? beatmapsPage;
        
        public BeatSaverUserPlaylistEntry(User owner)
        {
            Owner = owner;
        }
        
        public async Task<List<Song>?> GetSongs(IHttpService siraHttpService, CancellationToken cancellationToken = default, bool firstPage = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!firstPage && ExhaustedPages) return new List<Song>();
            Page? nextPage;
            if (beatmapsPage == null || firstPage)
            {
                nextPage = await RemoteRequest.BeatSaverPage(requestToken => Owner.Beatmaps(token: requestToken),
                    cancellationToken, "Fetch songs by " + Owner.Name);
            }
            else
            {
                nextPage = await RemoteRequest.BeatSaverPage(requestToken => beatmapsPage.Next(requestToken),
                    cancellationToken, "Fetch next song page by " + Owner.Name);
            }
            cancellationToken.ThrowIfCancellationRequested();
            beatmapsPage = nextPage;
            ExhaustedPages = beatmapsPage == null || beatmapsPage.Empty;
            if (beatmapsPage == null || beatmapsPage.Empty)
            {
                return new List<Song>();
            }

            var songs = new List<Song>();
            foreach (var beatmap in beatmapsPage.Beatmaps)
            {
                songs.Add(new Song(beatmap.Name, $"{beatmap.Metadata.SongAuthorName} [{beatmap.Metadata.LevelAuthorName}]", beatmap.LatestVersion.CoverURL));
            }
            return songs;
        }

        public async Task<IPlaylist?> DownloadPlaylist(IHttpService siraHttpService, CancellationToken cancellationToken = default)
        {
            return await RemoteRequest.GetPlaylist(siraHttpService, PlaylistURL, cancellationToken, stream =>
            {
                var handler = BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.DefaultHandler
                    ?? throw new InvalidOperationException("No playlist deserializer is registered.");
                return handler.Deserialize(stream) ?? throw new System.IO.InvalidDataException("The downloaded user playlist could not be deserialized: " + PlaylistURL);
            });
        }
    }
}
