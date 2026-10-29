using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using MorePlaylists.Entries;
using MorePlaylists.Utilities;
using SiraUtil.Web;
using System;
using System.Threading;
using System.Threading.Tasks;
using Zenject;

namespace MorePlaylists.UI
{
    [HotReload(RelativePathToLayout = @"..\Views\MorePlaylistsSongListView.bsml")]
    [ViewDefinition("MorePlaylists.UI.Views.MorePlaylistsSongListView.bsml")]
    internal class MorePlaylistsSongListViewController : BSMLAutomaticViewController
    {
        [Inject]
        private readonly IHttpService siraHttpService = null!;
        
        [Inject]
        private readonly SpriteLoader spriteLoader = null!;
        
        [UIComponent("list")]
        private readonly CustomListTableData? customListTableData = null!;
        
        private IEntry? currentEntry;
        private CancellationTokenSource? cancellationTokenSource;
        private bool retryFirstPage = true;
        
        private ScrollView? scrollView;
        private float? currentScrollPosition;
        
        private readonly SemaphoreSlim songLoadSemaphore = new(1, 1);

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (scrollView != null)
            {
                scrollView.scrollPositionChangedEvent += OnScrollPositionChanged;
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            if (scrollView != null)
            {
                scrollView.scrollPositionChangedEvent -= OnScrollPositionChanged;
            }
        }

        public void SetCurrentPlaylist(IEntry entry)
        {
            if (customListTableData == null)
            {
                return;
            }

            currentEntry = entry;
            ClearError();
            
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = new CancellationTokenSource();

            _ = InitSongList(entry, cancellationTokenSource.Token, true);
        }

        private void OnScrollPositionChanged(float newPos)
        {
            if (scrollView == null || currentEntry == null || currentEntry.ExhaustedPages || currentScrollPosition == newPos || songLoadSemaphore.CurrentCount == 0)
            {
                return;
            }
            
            currentScrollPosition = newPos;
            if (!Accessors.PageDownAccessor(ref scrollView).interactable)
            {
                cancellationTokenSource?.Cancel();
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
                _ = InitSongList(currentEntry, cancellationTokenSource.Token, false);
            }
        }
        
        [UIAction("#post-parse")]
        private void PostParse()
        {
            var tableView = customListTableData!.TableView;
            scrollView = Accessors.ScrollViewAccessor(ref tableView);
        }

        [UIAction("list-select")]
        private void Select(TableView _, int __) => customListTableData!.TableView.ClearSelection();

        [UIAction("retry-click")]
        private void RetryClicked()
        {
            if (currentEntry == null)
            {
                return;
            }

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = new CancellationTokenSource();
            _ = InitSongList(currentEntry, cancellationTokenSource.Token, retryFirstPage);
        }

        private async Task InitSongList(IEntry entry, CancellationToken cancellationToken, bool firstPage)
        {
            if (customListTableData == null)
            {
                return;
            }

            var semaphoreAcquired = false;
            try
            {
                await songLoadSemaphore.WaitAsync(cancellationToken);
                semaphoreAcquired = true;
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                ClearError();
                retryFirstPage = firstPage;

                if (firstPage)
                {
                    await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                    {
                        customListTableData.Data.Clear();
                        customListTableData.TableView.ReloadData();
                        Loaded = false;
                    });   
                }

                // We check the cancellationtoken at each interval instead of running everything with a single token
                // due to unity not liking it
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            
                var songs = await entry.GetSongs(siraHttpService, cancellationToken, firstPage);
            
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (songs == null)
                {
                    SetError("Unable to load playlist songs. Check your connection and retry.");
                    return;
                }
            
                foreach (var song in songs)
                {
                    var customCellInfo = new CustomListTableData.CustomCellInfo(song.Name, song.SubName,
                        BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite);
                
                    _ = spriteLoader.DownloadSpriteAsync(song.CoverURL, sprite =>
                    {
                        customCellInfo.Icon = sprite;
                        customListTableData.TableView.ReloadDataKeepingPosition();
                    }, cancellationToken);
                
                    customListTableData.Data.Add(customCellInfo);
                                
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                Plugin.Log?.Error($"Unable to load songs for playlist '{entry.Title}': {e}");
                if (!cancellationToken.IsCancellationRequested)
                    SetError("Unable to load playlist songs. Check your connection and retry.");
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
                    catch (Exception exception) { Plugin.Log?.Error("Unable to update playlist songs: " + exception); }
                    finally { songLoadSemaphore.Release(); }
                }
            }
        }
        
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
    }
}
