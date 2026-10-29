using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using MorePlaylists.Entries;
using MorePlaylists.Sources;
using MorePlaylists.UI;
using MorePlaylists.Utilities;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace MorePlaylists.BeatSaver
{ 
    [HotReload(RelativePathToLayout = @"..\UI\Views\BasicListView.bsml")] 
    [ViewDefinition("MorePlaylists.UI.Views.BasicListView.bsml")]
    internal class BeatSaverListViewController : BSMLAutomaticViewController, IListViewController, IDisposable
    {
        [Inject]
        private readonly SpriteLoader spriteLoader = null!;
        
        [Inject]
        private readonly InputFieldGrabber inputFieldGrabber = null!;
        
        private readonly SemaphoreSlim playlistLoadSemaphore = new(1, 1);

        private CancellationTokenSource? loadCancellationTokenSource;
        private List<IBeatSaverEntry> allPlaylists = new();
        private BeatSaver? currentSource;
        private bool retryRefresh = true;
        
        private ScrollView? scrollView;
        private float? currentScrollPosition;

        public ViewController ViewController => this;
        public event Action<IEntry>? DidSelectPlaylist;
        public event Action? DidClickSource;
        public event Action? DetailDismissRequested;

        [UIComponent("source-button")]
        private readonly ButtonIconImage? sourceButton = null!;
        
        [UIComponent("list")]
        private readonly CustomListTableData? customListTableData = null!;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (currentSource != null && customListTableData != null && sourceButton != null && scrollView != null)
            {
                sourceButton.Image.sprite = currentSource.Logo;
                
                loadCancellationTokenSource?.Cancel();
                loadCancellationTokenSource?.Dispose();
                loadCancellationTokenSource = new CancellationTokenSource();
                ClearError();
                
                scrollView.scrollPositionChangedEvent += OnScrollPositionChanged;
                
                _ = LoadPlaylists(currentSource, loadCancellationTokenSource.Token, true);
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            loadCancellationTokenSource?.Cancel();
            if (scrollView != null)
            {
                scrollView.scrollPositionChangedEvent -= OnScrollPositionChanged;
            }
        }

        public void Dispose()
        {
            AbortLoading();
            if (filtersButton != null && clearFiltersButton != null)
            {
                filtersButton.onClick.RemoveAllListeners();
                clearFiltersButton.onClick.RemoveAllListeners();
            }
        }

        #region Actions

        [UIAction("#post-parse")]
        private void PostParse()
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0);
            rectTransform.localPosition = Vector3.zero;
            var tableView = customListTableData!.TableView;
            scrollView = Accessors.ScrollViewAccessor(ref tableView);
            InstantiateFilterBar();
        }

        [UIAction("list-select")]
        private void Select(TableView tableView, int row) => DidSelectPlaylist?.Invoke(allPlaylists![row]);

        [UIAction("source-click")]
        private void DisplaySources() => DidClickSource?.Invoke();

        [UIAction("retry-click")]
        private void RetryClicked()
        {
            if (currentSource == null)
            {
                return;
            }

            loadCancellationTokenSource?.Cancel();
            loadCancellationTokenSource?.Dispose();
            loadCancellationTokenSource = new CancellationTokenSource();
            _ = LoadPlaylists(currentSource, loadCancellationTokenSource.Token, retryRefresh);
        }
        
        public void AbortLoading()
        {
            loadCancellationTokenSource?.Cancel();
            loadCancellationTokenSource?.Dispose();
            loadCancellationTokenSource = null;
            Loaded = true;
        }

        #endregion
        
        public void SetEntryAsOwned(IEntry playlistEntry)
        {
            var index = allPlaylists.FindIndex(entry => ReferenceEquals(entry, playlistEntry));
            if (index >= 0 && customListTableData != null)
            {
                customListTableData.Data[index] = new CustomListTableData.CustomCellInfo($"<#7F7F7F>{playlistEntry.Title}", playlistEntry.Author);
                _ = spriteLoader.DownloadSpriteAsync(playlistEntry.SpriteURL, sprite =>
                {
                    customListTableData.Data[index].Icon = sprite;
                    customListTableData.TableView.ReloadDataKeepingPosition();
                });
            }
        }

        #region Show Playlists

        public void ShowPlaylistsForSource(ISource source)
        {
            if (source is BeatSaver beatSaverSource)
            {
                currentSource = beatSaverSource;
                ClearFilters();
            }
        }

        private async Task LoadPlaylists(BeatSaver source, CancellationToken cancellationToken, bool refreshRequested = false)
        {
            if (customListTableData == null)
            {
                return;
            }
                
            var semaphoreAcquired = false;
            try
            {
                await playlistLoadSemaphore.WaitAsync(cancellationToken);
                semaphoreAcquired = true;
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                ClearError();
                retryRefresh = refreshRequested;
                if (refreshRequested)
                {
                    await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                    {
                        customListTableData.TableView.ClearSelection();
                        customListTableData.Data.Clear();
                        allPlaylists.Clear();
                        Loaded = false;
                    });   
                }

                // We check the cancellationtoken at each interval instead of running everything with a single token
                // due to unity not liking it
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var currentPlaylists = await source.GetPage(refreshRequested, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (currentPlaylists == null)
                {
                    SetError("Unable to load BeatSaver playlists. Check your connection and retry.");
                    return;
                }
                
                PlaylistLibUtils.UpdatePlaylistsOwned(currentPlaylists.Cast<IEntry>().ToList());
                    
                ShowPlaylists(currentPlaylists, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                Plugin.Log?.Error($"Unable to load BeatSaver playlists: {e}");
                if (!cancellationToken.IsCancellationRequested)
                    SetError("Unable to load BeatSaver playlists. Check your connection and retry.");
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    try
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                            {
                                Loaded = true;
                                customListTableData.TableView.ReloadDataKeepingPosition();
                            });
                    }
                    catch (Exception exception) { Plugin.Log?.Error("Unable to update BeatSaver playlist list: " + exception); }
                    finally { playlistLoadSemaphore.Release(); }
                }
            }
        }

        private void ShowPlaylists(List<IBeatSaverEntry> currentPlaylists, CancellationToken cancellationToken)
        {
            if (customListTableData == null)
            {
                return;
            }
                
            foreach (var playlistEntry in currentPlaylists)
            {
                var customCellInfo = new CustomListTableData.CustomCellInfo(playlistEntry.DownloadBlocked
                        ? $"<#7F7F7F>{playlistEntry.Title}"
                        : playlistEntry.Title,
                    playlistEntry.Author);

                _ = spriteLoader.DownloadSpriteAsync(playlistEntry.SpriteURL, sprite =>
                {
                    customCellInfo.Icon = sprite;
                    customListTableData.TableView.ReloadDataKeepingPosition();
                }, cancellationToken);

                customListTableData.Data.Add(customCellInfo);
                allPlaylists.Add(playlistEntry);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        
        private void OnScrollPositionChanged(float newPos)
        {
            if (scrollView == null || currentSource == null || currentSource.ExhaustedPlaylists || playlistLoadSemaphore.CurrentCount == 0 || currentScrollPosition == newPos)
            {
                return;
            }

            currentScrollPosition = newPos;
            if (!Accessors.PageDownAccessor(ref scrollView).interactable)
            {
                loadCancellationTokenSource?.Cancel();
                loadCancellationTokenSource?.Dispose();
                loadCancellationTokenSource = new CancellationTokenSource();
                _ = LoadPlaylists(currentSource, loadCancellationTokenSource.Token);
            }
        }

        #endregion

        #region Loading

        private bool loaded;
        private string errorText = string.Empty;

        private void ClearError()
        {
            if (errorText.Length == 0)
            {
                return;
            }

            errorText = string.Empty;
            NotifyPropertyChanged(nameof(HasError));
            NotifyPropertyChanged(nameof(ErrorText));
            NotifyPropertyChanged(nameof(HasNoResults));
        }

        private void SetError(string message)
        {
            errorText = message;
            NotifyPropertyChanged(nameof(HasError));
            NotifyPropertyChanged(nameof(ErrorText));
            NotifyPropertyChanged(nameof(HasNoResults));
        }

        [UIValue("has-error")]
        private bool HasError => errorText.Length > 0;

        [UIValue("has-no-results")]
        private bool HasNoResults => Loaded && !HasError && customListTableData?.Data.Count == 0;

        [UIValue("error-text")]
        private string ErrorText => errorText;
        
        [UIValue("is-loading")]
        private bool IsLoading => !Loaded;

        [UIValue("has-loaded")]
        private bool Loaded
        {
            get => loaded;
            set
            {
                loaded = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(IsLoading));
                NotifyPropertyChanged(nameof(HasNoResults));
            }
        }
        
        #endregion

        #region FilterBar
        
        private Button? filtersButton;
        private Button? clearFiltersButton;
        private GameObject? placeholderText;
        private CurvedTextMeshPro? filterText;

        public event Action? FilterViewRequested;
        public event Action? FilterClearRequested;

        [UIComponent("filter-bar")] 
        private readonly RectTransform filterBarTransform = null!;
        
        private void InstantiateFilterBar()
        {
            filtersButton = inputFieldGrabber.GetNewFiltersButton(filterBarTransform);
            if (filtersButton.transform is RectTransform filtersButtonTransform)
            {
                clearFiltersButton = filtersButtonTransform.Find("ClearButton").GetComponent<NoTransitionsButton>();
                placeholderText = filtersButtonTransform.Find("PlaceholderText").gameObject;
                filterText = filtersButtonTransform.Find("Text").GetComponent<CurvedTextMeshPro>();
                filtersButton.onClick.AddListener(FilterClicked);
                clearFiltersButton.onClick.AddListener(ClearFilters);
                
                filtersButtonTransform.SetSiblingIndex(0);
                filtersButtonTransform.sizeDelta = new Vector2(50, 8);
            }

            if (currentSource != null)
            {
                SetActiveFilter(currentSource.CurrentFilters);
            }
        }

        private void FilterClicked()
        {
            DetailDismissRequested?.Invoke();
            FilterViewRequested?.Invoke();
        }

        private void ClearFilters()
        {
            DetailDismissRequested?.Invoke();
            FilterClearRequested?.Invoke();

            if (currentSource != null)
            {
                loadCancellationTokenSource?.Cancel();
                loadCancellationTokenSource?.Dispose();
                loadCancellationTokenSource = new CancellationTokenSource();
                _ = LoadPlaylists(currentSource, loadCancellationTokenSource.Token, true);
            }
        }

        public void SetActiveFilter(BeatSaverFilterModel filterOptions)
        {
            switch (filterOptions.FilterMode)
            {
                case FilterMode.Search:
                    if (filterOptions.NullableSearchFilter == null)
                    {
                        FilterString = string.Empty;
                        break;
                    }

                    var sb = new StringBuilder();
            
                    if (!string.IsNullOrWhiteSpace(filterOptions.SearchFilter.Query))
                    {
                        sb.Append(filterOptions.SearchFilter.Query + ", ");
                    }

                    if (filterOptions.SearchFilter.IncludeEmpty)
                    {
                        sb.Append("IncludeEmpty, ");
                    }

                    if (filterOptions.SearchFilter.IsCurated)
                    {
                        sb.Append("CuratedOnly, ");
                    }

                    if (sb.Length >= 2)
                    {
                        sb.Remove(sb.Length - 2, 2);
                    }
                    
                    FilterString = sb.ToString();
                    break;
                
                case FilterMode.User:
                    FilterString = $"user:{filterOptions.UserName}";
                    break;
            }

            if (isActiveAndEnabled && currentSource != null)
            {
                DetailDismissRequested?.Invoke();
                loadCancellationTokenSource?.Cancel();
                loadCancellationTokenSource?.Dispose();
                loadCancellationTokenSource = new CancellationTokenSource();
                _ = LoadPlaylists(currentSource, loadCancellationTokenSource.Token, true);
            }
        }

        private string filterString = string.Empty;
        private string FilterString
        {
            get => filterString;
            set
            {
                filterString = value;

                if (clearFiltersButton == null || placeholderText == null || filterText == null)
                {
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(value))
                {
                    filterText.gameObject.SetActive(false);
                    placeholderText.SetActive(true);
                    clearFiltersButton.gameObject.SetActive(false);
                }
                else
                {
                    filterText.text = value;
                    filterText.gameObject.SetActive(true);
                    placeholderText.SetActive(false);
                    clearFiltersButton.gameObject.SetActive(true);
                }
            }
        }

        #endregion
    }
}
