using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using MorePlaylists.Entries;
using MorePlaylists.Utilities;
using System;
using System.Linq;
using System.Threading;
using BeatSaberPlaylistsLib.Types;
using UnityEngine;
using Zenject;

namespace MorePlaylists.UI
{
    [HotReload(RelativePathToLayout = @"..\Views\BasicDetailView.bsml")]
    [ViewDefinition("MorePlaylists.UI.Views.BasicDetailView.bsml")]
    internal class BasicDetailViewController : BSMLAutomaticViewController, IDetailViewController
    {
        [Inject]
        private readonly SpriteLoader spriteLoader = null!;
        
        [Inject] 
        private readonly MaterialGrabber materialGrabber = null!;
        
        private IEntry? selectedPlaylistEntry;
        private string downloadError = string.Empty;
        private CancellationTokenSource? spriteLoadTokenSource;
        private bool viewParsed;
        
        public ViewController ViewController => this;
        public event Action<IEntry, bool>? DidPressDownload;
        public event Action<IPlaylist>? DidGoToPlaylist;

        [UIComponent("playlist-cover")]
        private readonly ImageView playlistCoverView = null!;

        [UIComponent("text-page")]
        private readonly TextPageScrollView descriptionTextPage = null!;

        #region Actions

        [UIAction("#post-parse")]
        private void PostParse()
        {
            rectTransform.anchorMax = new Vector2(0.5f, 1);
            if (materialGrabber != null && playlistCoverView != null) playlistCoverView.material = materialGrabber.NoGlowRoundEdge;
            viewParsed = true;
            ApplySelectedDetails();
        }

        [UIAction("download-click")]
        private void DownloadPressed()
        {
            if (selectedPlaylistEntry != null)
            {
                ClearDownloadError();
                DidPressDownload?.Invoke(selectedPlaylistEntry, false);
                NotifyPropertyChanged(nameof(DownloadInteractable));
            }
        }

        [UIAction("download-all-click")]
        private void DownloadAllPressed()
        {
            if (selectedPlaylistEntry != null)
            {
                ClearDownloadError();
                DidPressDownload?.Invoke(selectedPlaylistEntry, true);
                NotifyPropertyChanged(nameof(DownloadInteractable));
            }
        }

        [UIAction("go-to-playlist")]
        private void GoToPlaylist()
        {
            if (selectedPlaylistEntry?.LocalPlaylist != null)
            {
                DidGoToPlaylist?.Invoke(selectedPlaylistEntry.LocalPlaylist);
            }
        }

        #endregion

        public void ShowDetail(IEntry selectedPlaylistEntry)
        {
            this.selectedPlaylistEntry = selectedPlaylistEntry;
            if (!viewParsed) return;
            ApplySelectedDetails();
        }

        private void ApplySelectedDetails()
        {
            if (!viewParsed || selectedPlaylistEntry == null) return;

            IEntry entry = selectedPlaylistEntry;
            ClearDownloadError();
            NotifyPropertyChanged(nameof(DownloadInteractable));
            NotifyPropertyChanged(nameof(DownloadActive));
            NotifyPropertyChanged(nameof(GoToActive));
            NotifyPropertyChanged(nameof(PlaylistName));
            NotifyPropertyChanged(nameof(PlaylistAuthor));
            NotifyPropertyChanged(nameof(PlaylistDescription));
            if (descriptionTextPage != null) descriptionTextPage.ScrollTo(0, true);

            spriteLoadTokenSource?.Cancel();
            spriteLoadTokenSource?.Dispose();
            spriteLoadTokenSource = new CancellationTokenSource();
            CancellationToken token = spriteLoadTokenSource.Token;
            if (playlistCoverView != null)
            {
                _ = spriteLoader.DownloadSpriteAsync(entry.SpriteURL,
                    sprite =>
                    {
                        if (!token.IsCancellationRequested && ReferenceEquals(selectedPlaylistEntry, entry) && playlistCoverView != null)
                            playlistCoverView.sprite = sprite;
                    }, token);
            }
        }

        public void OnPlaylistDownloaded(IEntry entry)
        {
            if (!ReferenceEquals(selectedPlaylistEntry, entry))
            {
                return;
            }

            ClearDownloadError();
            NotifyPropertyChanged(nameof(DownloadInteractable));
            NotifyPropertyChanged(nameof(DownloadActive));
            NotifyPropertyChanged(nameof(GoToActive));
        }

        public void OnPlaylistDownloadFailed(IEntry entry, Exception exception)
        {
            if (!ReferenceEquals(selectedPlaylistEntry, entry))
            {
                return;
            }

            downloadError = "Download failed. Try again.";
            NotifyPropertyChanged(nameof(HasDownloadError));
            NotifyPropertyChanged(nameof(DownloadError));
            NotifyPropertyChanged(nameof(DownloadInteractable));
            NotifyPropertyChanged(nameof(DownloadActive));
        }

        private void ClearDownloadError()
        {
            if (downloadError.Length == 0)
            {
                return;
            }

            downloadError = string.Empty;
            NotifyPropertyChanged(nameof(HasDownloadError));
            NotifyPropertyChanged(nameof(DownloadError));
        }

        #region Values

        [UIValue("playlist-name")] 
        public string PlaylistName
        {
            get
            {
                if (selectedPlaylistEntry != null)
                {
                    if (selectedPlaylistEntry.Title.Length > 32)
                    {
                        return selectedPlaylistEntry.Title.Substring(0, 28) + "...";
                    }
                    return selectedPlaylistEntry.Title;
                }
                return string.Empty;
            }
        }

        [UIValue("playlist-author")]
        public string PlaylistAuthor
        {
            get
            {
                if (selectedPlaylistEntry != null)
                {
                    if (selectedPlaylistEntry.Author.Length > 32)
                    {
                        return selectedPlaylistEntry.Author.Substring(0, 28) + "...";
                    }
                    return selectedPlaylistEntry.Author;
                }
                return string.Empty;
            }
        }

        [UIValue("playlist-description")]
        private string PlaylistDescription => string.IsNullOrWhiteSpace(selectedPlaylistEntry?.Description) ? "No Description available for this playlist." : selectedPlaylistEntry?.Description ?? "";

        [UIValue("download-interactable")]
        public bool DownloadInteractable => selectedPlaylistEntry is {DownloadBlocked: false};

        [UIValue("download-active")]
        public bool DownloadActive => selectedPlaylistEntry is {LocalPlaylist: null};

        [UIValue("has-download-error")]
        public bool HasDownloadError => downloadError.Length > 0;

        [UIValue("download-error")]
        public string DownloadError => downloadError;

        [UIValue("go-to-active")]
        public bool GoToActive => selectedPlaylistEntry is {LocalPlaylist: { }};

        #endregion
    }
}
