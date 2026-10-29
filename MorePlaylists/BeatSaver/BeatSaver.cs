using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BeatSaverSharp;
using BeatSaverSharp.Models.Pages;
using HMUI;
using IPA.Loader;
using MorePlaylists.Sources;
using MorePlaylists.UI;
using MorePlaylists.Utilities;
using PlaylistManager.Utilities;
using SiraUtil.Zenject;
using UnityEngine;
using Zenject;

namespace MorePlaylists.BeatSaver
{
    internal class BeatSaver : ISource, IInitializable, IDisposable
    {
        private readonly BeatSaverSharp.BeatSaver beatSaverInstance;
        private readonly BeatSaverFiltersViewController filtersViewController;
        private readonly BeatSaverListViewController listViewController;
        
        private PlaylistPage? page;
        public bool ExhaustedPlaylists { get; private set; }

        private Sprite? logo;
        public Sprite Logo
        {
            get
            {
                if (logo == null)
                {
                    logo = PlaylistMods.Common.SpriteLoader.FromResource(typeof(BeatSaver).Assembly, "MorePlaylists.Images.BeatSaver.png");
                }
                return logo;
            }
        }

        public BeatSaverFilterModel CurrentFilters => filtersViewController.filterOptions;
        public IListViewController ListViewController => listViewController;
        public IDetailViewController DetailViewController { get; }
        
        public event Action<ViewController, ViewController.AnimationDirection>? ViewControllerRequested;
        public event Action<ViewController, ViewController.AnimationDirection>? ViewControllerDismissRequested;

        public BeatSaver(UBinder<Plugin, PluginMetadata> metadata, BeatSaverFiltersViewController filtersViewController, BeatSaverListViewController listViewController, BeatSaverDetailViewController detailViewController)
        {
            var assemblyVersion = typeof(Plugin).Assembly.GetName().Version;
            var userAgentVersion = assemblyVersion == null
                ? "2.0.0"
                : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
            var options = new BeatSaverOptions(metadata.Value.Name, userAgentVersion);
            beatSaverInstance = BeatSaverClientFactory.Create(options);

            this.filtersViewController = filtersViewController;
            this.listViewController = listViewController;
            DetailViewController = detailViewController;
        }
        
        public void Initialize()
        {
            listViewController.FilterViewRequested += RequestFilterView;
            listViewController.FilterClearRequested += ClearFilters;
            
            filtersViewController.FiltersSet += OnFiltersSet;
            filtersViewController.RequestDismiss += RequestFilterViewDismiss;
        }

        public void Dispose()
        {
            listViewController.FilterViewRequested -= RequestFilterView;
            listViewController.FilterClearRequested -= ClearFilters;
            
            filtersViewController.FiltersSet -= OnFiltersSet;
            filtersViewController.RequestDismiss -= RequestFilterViewDismiss;
            beatSaverInstance.Dispose();
        }

        public async Task<List<IBeatSaverEntry>?> GetPage(bool refreshRequested, CancellationToken token)
        {
            List<IBeatSaverEntry>? entries = null;
            token.ThrowIfCancellationRequested();
            if (!refreshRequested && ExhaustedPlaylists) return new List<IBeatSaverEntry>();
            PlaylistPage? nextPage = null;
            
            if (refreshRequested || page == null)
            {
                switch (CurrentFilters.FilterMode)
                {
                    case FilterMode.Search:
                        nextPage = await RemoteRequest.BeatSaverPage(requestToken => beatSaverInstance.SearchPlaylists(CurrentFilters.NullableSearchFilter, token: requestToken),
                            token, "Search BeatSaver playlists");
                        break;
                    
                    case FilterMode.User:
                        if (CurrentFilters.UserName != null)
                        {
                            var user = await RemoteRequest.BeatSaverPage(requestToken => beatSaverInstance.User(CurrentFilters.UserName, requestToken),
                                token, "Find BeatSaver user");
                            if (user != null)
                            {
                                nextPage = await RemoteRequest.BeatSaverPage(requestToken => beatSaverInstance.UserPlaylists(user.ID, token: requestToken),
                                    token, "Find BeatSaver user playlists");
                                entries = new List<IBeatSaverEntry> {new BeatSaverUserPlaylistEntry(user)};
                            }
                        }
                        break;
                }
            }
            else
            {
                nextPage = await RemoteRequest.BeatSaverPage(requestToken => page.Next(requestToken), token,
                    "Next BeatSaver playlist page");
            }
            token.ThrowIfCancellationRequested();
            ExhaustedPlaylists = nextPage == null || nextPage.Empty;
            page = nextPage;
            if (entries == null && ExhaustedPlaylists)
            {
                return new List<IBeatSaverEntry>();
            }

            entries ??= new List<IBeatSaverEntry>();
            if (page != null)
            {
                foreach (var playlist in page.Playlists)
                {
                    entries.Add(new BeatSaverPlaylistEntry(playlist));
                }   
            }
            return entries;
        }

        private void RequestFilterView() =>
            ViewControllerRequested?.Invoke(filtersViewController, ViewController.AnimationDirection.Vertical);

        private void ClearFilters() => filtersViewController.ClearFilters();

        private void OnFiltersSet(BeatSaverFilterModel filterOptions) => listViewController.SetActiveFilter(filterOptions);

        private void RequestFilterViewDismiss() => ViewControllerDismissRequested?.Invoke(filtersViewController, ViewController.AnimationDirection.Vertical);
    }
}
