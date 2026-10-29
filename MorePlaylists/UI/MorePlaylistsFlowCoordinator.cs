using HMUI;
using Zenject;
using BeatSaberMarkupLanguage;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MorePlaylists.Entries;
using MorePlaylists.Sources;
using MorePlaylists.Utilities;

namespace MorePlaylists.UI
{
    internal class MorePlaylistsFlowCoordinator : FlowCoordinator, IInitializable, IDisposable
    {
        [Inject]
        private readonly MainFlowCoordinator mainFlowCoordinator = null!;

        [Inject] 
        private readonly SoloFreePlayFlowCoordinator soloFreePlayFlowCoordinator = null!;
        
        [Inject]
        private readonly PopupModalsController popupModalsController = null!;
        
        [Inject]
        private readonly SourceModalController sourceModalController = null!;
        
        [Inject]
        private readonly MorePlaylistsNavigationController morePlaylistsNavigationController = null!;

        [Inject]
        private readonly MorePlaylistsDownloaderViewController morePlaylistsDownloaderViewController = null!;

        [Inject]
        private readonly MorePlaylistsSongListViewController morePlaylistsSongListViewController = null!;

        private IListViewController ListViewController => sourceModalController.SelectedSource.ListViewController;
        private IDetailViewController DetailViewController => sourceModalController.SelectedSource.DetailViewController;
        private Coroutine activation;

        public void Initialize()
        {
            sourceModalController.DidSelectSource += SourceSelected;
            morePlaylistsDownloaderViewController.PlaylistDownloaded += OnPlaylistDownloaded;
            morePlaylistsDownloaderViewController.PlaylistDownloadFailed += OnPlaylistDownloadFailed;
        }

        public void Dispose()
        {
            CancelActivation();
            sourceModalController.DidSelectSource -= SourceSelected;
            UnsubFromEvents(sourceModalController.SelectedSource);
            morePlaylistsDownloaderViewController.PlaylistDownloaded -= OnPlaylistDownloaded;
            morePlaylistsDownloaderViewController.PlaylistDownloadFailed -= OnPlaylistDownloadFailed;
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            SetTitle("Download Playlists");
            showBackButton = true;
            ProvideInitialViewControllers(morePlaylistsNavigationController, morePlaylistsDownloaderViewController);
            CancelActivation();
            activation = StartCoroutine(ActivateSource());
        }

        private IEnumerator ActivateSource()
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!morePlaylistsNavigationController.isActivated && Time.realtimeSinceStartup < deadline)
                yield return null;
            activation = null;
            if (!morePlaylistsNavigationController.isActivated)
            {
                Plugin.Log.Error("Playlist navigation did not activate within 10 seconds.");
                yield break;
            }
            SourceSelected(sourceModalController.SelectedSource);
        }

        private void CancelActivation()
        {
            if (activation == null) return;
            StopCoroutine(activation);
            activation = null;
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            CancelActivation();
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        private void SourceSelected(ISource source)
        {
            UnsubFromEvents(sourceModalController.SelectedSource);
            SubToEvents(source);
            source.ListViewController.ShowPlaylistsForSource(source);
            SetViewControllersToNavigationController(morePlaylistsNavigationController, source.ListViewController.ViewController);
            SetRightScreenViewController(null, ViewController.AnimationType.Out);
        }

        private void ShowViewController(ViewController viewController, ViewController.AnimationDirection animationDirection)
        {
            showBackButton = false;
            PresentViewController(viewController, animationDirection: animationDirection);
        }

        private void DismissViewController(ViewController viewController, ViewController.AnimationDirection animationDirection)
        {
            if (viewController.isInViewControllerHierarchy)
            {
                showBackButton = true;
                base.DismissViewController(viewController, animationDirection, null);
            }
        }

        private void PlaylistSelected(IEntry selectedPlaylistEntry)
        {
            if (!DetailViewController.ViewController.isInViewControllerHierarchy)
            {
                PushViewControllerToNavigationController(morePlaylistsNavigationController, DetailViewController.ViewController);
            }
            DetailViewController.ShowDetail(selectedPlaylistEntry);
            SetRightScreenViewController(morePlaylistsSongListViewController, ViewController.AnimationType.In);
            morePlaylistsSongListViewController.SetCurrentPlaylist(selectedPlaylistEntry);
        }

        private void SourceButtonClicked() => sourceModalController.ShowModal(ListViewController.ViewController.transform);
        
        private void DismissDetailView()
        {
            if (DetailViewController.ViewController.isInViewControllerHierarchy)
            {
                PopViewControllerFromNavigationController(morePlaylistsNavigationController, immediately: true);
            }

            if (morePlaylistsSongListViewController.isInViewControllerHierarchy)
            {
                SetRightScreenViewController(null, ViewController.AnimationType.Out);
            }
        }
        
        private void OnPlaylistDownloaded(IEntry playlistEntry)
        {
            if (playlistEntry.LocalPlaylist != null)
            {
                ListViewController.SetEntryAsOwned(playlistEntry);
                DetailViewController.OnPlaylistDownloaded(playlistEntry);
            }
            else
            {
                Plugin.Log?.Error($"Playlist download completed without a local playlist: {playlistEntry.Title}");
            }
        }

        private void OnPlaylistDownloadFailed(IEntry playlistEntry, Exception exception)
        {
            Plugin.Log?.Error($"Playlist download failed for '{playlistEntry.Title}': {exception}");
            DetailViewController.OnPlaylistDownloadFailed(playlistEntry, exception);

            var reason = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
            if (reason.Length > 160)
            {
                reason = reason.Substring(0, 157) + "...";
            }

            popupModalsController.ShowOkModal(
                ListViewController.ViewController.transform,
                $"Playlist download failed: {reason}\nTry again.",
                null);
        }

        private void GoToPlaylistClicked(BeatSaberPlaylistsLib.Types.IPlaylist playlist)
        {
            ListViewController.AbortLoading();
            mainFlowCoordinator.DismissFlowCoordinator(this, immediately: true);
            soloFreePlayFlowCoordinator.Setup(Utils.GetStateForPlaylist(playlist.PlaylistLevelPack));
            mainFlowCoordinator.PresentFlowCoordinator(soloFreePlayFlowCoordinator);
        }
        
        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            ListViewController.AbortLoading();
            mainFlowCoordinator.DismissFlowCoordinator(this);
        }

        private void SubToEvents(ISource source)
        {
            source.ViewControllerRequested += ShowViewController;
            source.ViewControllerDismissRequested += DismissViewController;
            
            source.ListViewController.DidSelectPlaylist += PlaylistSelected;
            source.ListViewController.DidClickSource += SourceButtonClicked;
            source.ListViewController.DetailDismissRequested += DismissDetailView;
            
            source.DetailViewController.DidPressDownload += morePlaylistsDownloaderViewController.DownloadPlaylist;
            source.DetailViewController.DidGoToPlaylist += GoToPlaylistClicked;
        }

        private void UnsubFromEvents(ISource source)
        {
            source.ViewControllerRequested -= ShowViewController;
            source.ViewControllerDismissRequested -= DismissViewController;
            
            source.ListViewController.DidSelectPlaylist -= PlaylistSelected;
            source.ListViewController.DidClickSource -= SourceButtonClicked;
            source.ListViewController.DetailDismissRequested -= DismissDetailView;
            
            source.DetailViewController.DidPressDownload -= morePlaylistsDownloaderViewController.DownloadPlaylist;
            source.DetailViewController.DidGoToPlaylist -= GoToPlaylistClicked;
        }
    }

    internal class MorePlaylistsNavigationController : NavigationController
    {
    }
}
