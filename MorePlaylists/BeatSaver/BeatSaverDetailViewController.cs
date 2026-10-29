using System;
using System.Threading;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSaberPlaylistsLib.Types;
using HMUI;
using MorePlaylists.Entries;
using MorePlaylists.UI;
using MorePlaylists.Utilities;
using UnityEngine;
using Zenject;

namespace MorePlaylists.BeatSaver
{
    [HotReload(RelativePathToLayout = @".\BeatSaverDetailView.bsml")]
    [ViewDefinition("MorePlaylists.BeatSaver.BeatSaverDetailView.bsml")]
    internal class BeatSaverDetailViewController : BSMLAutomaticViewController, IDetailViewController
    {
        [Inject] 
        private readonly SpriteLoader spriteLoader = null!;

        [Inject]
        private readonly MaterialGrabber materialGrabber = null!;

        [Inject] 
        private readonly BeatSaverFiltersViewController beatSaverFiltersViewController = null!;

        private IBeatSaverEntry? selectedPlaylistEntry;
        private string downloadError = string.Empty;
        private CancellationTokenSource? spriteLoadTokenSource;
        private bool viewParsed;

        public ViewController ViewController => this;
        public event Action<IEntry, bool>? DidPressDownload;
        public event Action<IPlaylist>? DidGoToPlaylist;

        [UIComponent("playlist-cover")] private readonly ImageView playlistCoverView = null!;

        [UIComponent("user-image")] private readonly ImageView userImageView = null!;

        [UIComponent("text-page")] private readonly TextPageScrollView descriptionTextPage = null!;

        #region Actions

        [UIAction("#post-parse")]
        private void PostParse()
        {
            rectTransform.anchorMax = new Vector2(0.5f, 1);
            if (materialGrabber != null && playlistCoverView != null) playlistCoverView.material = materialGrabber.NoGlowRoundEdge;
            if (materialGrabber != null && userImageView != null) userImageView.material = materialGrabber.NoGlowRoundEdge;
            viewParsed = true;
            ApplySelectedDetails();
        }

        [UIAction("author-click")]
        private void AuthorPressed()
        {
            beatSaverFiltersViewController.filterOptions.FilterMode = FilterMode.User;
            beatSaverFiltersViewController.filterOptions.UserName = PlaylistAuthor;
            beatSaverFiltersViewController.RaiseFiltersSet();
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
            if (selectedPlaylistEntry is IBeatSaverEntry beatSaverEntry)
            {
                this.selectedPlaylistEntry = beatSaverEntry;
                if (!viewParsed) return;
                ApplySelectedDetails();
            }
        }

        private void ApplySelectedDetails()
        {
            if (!viewParsed || selectedPlaylistEntry == null) return;

            IBeatSaverEntry beatSaverEntry = selectedPlaylistEntry;
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
                _ = spriteLoader.DownloadSpriteAsync(beatSaverEntry.SpriteURL,
                    sprite =>
                    {
                        if (!token.IsCancellationRequested && ReferenceEquals(selectedPlaylistEntry, beatSaverEntry) && playlistCoverView != null)
                            playlistCoverView.sprite = sprite;
                    }, token);
            }
            if (userImageView != null && beatSaverEntry.Owner != null)
            {
                userImageView.sprite = BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite;
                _ = spriteLoader.DownloadSpriteAsync(beatSaverEntry.Owner.Avatar,
                    sprite =>
                    {
                        if (!token.IsCancellationRequested && ReferenceEquals(selectedPlaylistEntry, beatSaverEntry) && userImageView != null)
                            userImageView.sprite = sprite;
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
        private string PlaylistDescription => string.IsNullOrWhiteSpace(selectedPlaylistEntry?.Description)
            ? "No Description available for this playlist."
            : selectedPlaylistEntry?.Description ?? "";

        [UIValue("download-interactable")]
        public bool DownloadInteractable => selectedPlaylistEntry is {DownloadBlocked: false};

        [UIValue("download-active")] public bool DownloadActive => selectedPlaylistEntry is {LocalPlaylist: null};

        [UIValue("has-download-error")] public bool HasDownloadError => downloadError.Length > 0;

        [UIValue("download-error")] public string DownloadError => downloadError;

        [UIValue("go-to-active")] public bool GoToActive => selectedPlaylistEntry is {LocalPlaylist: { }};

        #endregion
    }
}
