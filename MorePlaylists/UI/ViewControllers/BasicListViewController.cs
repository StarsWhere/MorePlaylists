using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using MorePlaylists.Entries;
using MorePlaylists.Utilities;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MorePlaylists.Sources;
using Zenject;

namespace MorePlaylists.UI
{
    [HotReload(RelativePathToLayout = @"..\Views\BasicListView.bsml")]
    [ViewDefinition("MorePlaylists.UI.Views.BasicListView.bsml")]
    internal class BasicListViewController : BSMLAutomaticViewController, IListViewController, IDisposable
    {
        [Inject]
        private readonly SpriteLoader spriteLoader = null!;
        
        [Inject]
        private readonly InputFieldGrabber inputFieldGrabber = null!;
        
        private readonly SemaphoreSlim playlistLoadSemaphore = new(1, 1);

        private InputFieldView? inputFieldView;
        private CancellationTokenSource? loadCancellationTokenSource;
        private CancellationTokenSource? searchCancellationTokenSource;
        private readonly List<IBasicEntry> currentPlaylists = new();
        private List<IBasicEntry>? allPlaylists;
        private IBasicSource? currentSource;
        
        public ViewController ViewController => this;
        public event Action<IEntry>? DidSelectPlaylist;
        public event Action? DidClickSource;
        public event Action? DetailDismissRequested;

        [UIComponent("filter-bar")] 
        private readonly RectTransform? filterBarTransform = null!;
        
        [UIComponent("source-button")]
        private readonly ButtonIconImage? sourceButton = null!;
        
        [UIComponent("list")]
        private readonly CustomListTableData? customListTableData = null!;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (currentSource != null && customListTableData != null && sourceButton != null && inputFieldView != null)
            {
                sourceButton.Image.sprite = currentSource.Logo;
                inputFieldView.ClearInput();
                loadCancellationTokenSource?.Cancel();
                loadCancellationTokenSource?.Dispose();
                loadCancellationTokenSource = new CancellationTokenSource();
                ClearError();
                _ = LoadPlaylists(currentSource, loadCancellationTokenSource.Token);
            }
        }
        
        public void Dispose()
        {
            AbortLoading();
            if (inputFieldView != null)
            {
                inputFieldView.onValueChanged.RemoveAllListeners();
                inputFieldView.selectionStateDidChangeEvent -= InputFieldSelectionChanged;
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            AbortLoading();
        }

        #region Actions

        [UIAction("#post-parse")]
        private void PostParse()
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0);
            rectTransform.localPosition = Vector3.zero;
            
            inputFieldView = inputFieldGrabber.GetNewInputField(filterBarTransform!, new Vector3(0, -35, 0));
            if (inputFieldView.transform is RectTransform inputFieldTransform)
            {
                inputFieldTransform.SetSiblingIndex(0);
                inputFieldTransform.sizeDelta = new Vector2(50, 8);
            }
            inputFieldView.onValueChanged.AddListener(inputFieldView =>
            {
                searchCancellationTokenSource?.Cancel();
                searchCancellationTokenSource?.Dispose();
                searchCancellationTokenSource = new CancellationTokenSource();
                _ = SearchAsync(searchCancellationTokenSource.Token);
            });
            inputFieldView.selectionStateDidChangeEvent += InputFieldSelectionChanged;
        }

        [UIAction("list-select")]
        private void Select(TableView tableView, int row) => DidSelectPlaylist?.Invoke(currentPlaylists![row]);

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
            _ = LoadPlaylists(currentSource, loadCancellationTokenSource.Token, true);
        }
        
        public void AbortLoading()
        {
            loadCancellationTokenSource?.Cancel();
            loadCancellationTokenSource?.Dispose();
            loadCancellationTokenSource = null;
            searchCancellationTokenSource?.Cancel();
            searchCancellationTokenSource?.Dispose();
            searchCancellationTokenSource = null;
            Loaded = true;
        }

        #endregion
        
        public void SetEntryAsOwned(IEntry playlistEntry)
        {
            var index = currentPlaylists.FindIndex(entry => ReferenceEquals(entry, playlistEntry));
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
        
        private void InputFieldSelectionChanged(InputFieldView.SelectionState selectionState)
        {
            if (selectionState == InputFieldView.SelectionState.Pressed)
            {
                customListTableData!.TableView.ClearSelection();
                DetailDismissRequested?.Invoke();
            }
        }

        #region Show Playlists

        public void ShowPlaylistsForSource(ISource source)
        {
            if (source is IBasicSource basicSource)
            {
                currentSource = basicSource;
            }
        }

        private async Task LoadPlaylists(IBasicSource source, CancellationToken cancellationToken, bool refreshRequested = false)
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
                await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    customListTableData.TableView.ClearSelection();
                    customListTableData.Data.Clear();
                    Loaded = false;
                });

                // We check the cancellationtoken at each interval instead of running everything with a single token
                // due to unity not liking it
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                allPlaylists = await source.GetEndpointResult(refreshRequested, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (allPlaylists == null)
                {
                    SetError("Unable to load playlists. Check your connection and retry.");
                    return;
                }
                
                currentPlaylists.Clear();
                currentPlaylists.AddRange(allPlaylists);
                if (!string.IsNullOrEmpty(source.LoadWarning)) SetError(source.LoadWarning!);
                PlaylistLibUtils.UpdatePlaylistsOwned(currentPlaylists.Cast<IEntry>().ToList());
                
                ShowPlaylists(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                Plugin.Log?.Error($"Unable to load {source.GetType().Name} playlists: {e}");
                if (!cancellationToken.IsCancellationRequested)
                    SetError("Unable to load playlists. Check your connection and retry.");
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
                                customListTableData.TableView.ReloadData();
                            });
                    }
                    catch (Exception exception) { Plugin.Log?.Error("Unable to update playlist list: " + exception); }
                    finally { playlistLoadSemaphore.Release(); }
                }
            }
        }

        private void ShowPlaylists(CancellationToken cancellationToken)
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

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private async Task SearchAsync(CancellationToken cancellationToken)
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

                if (allPlaylists == null)
                {
                    return;
                }

                await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    customListTableData.Data.Clear();
                    Loaded = false;
                });
                
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                
                var searchQuery = inputFieldView != null ? inputFieldView.text : "";
                var searchTerms = searchQuery.Split(' ');
                currentPlaylists.Clear();
                if (string.IsNullOrWhiteSpace(searchQuery))
                {
                    currentPlaylists.AddRange(allPlaylists);
                    await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                    {
                        customListTableData!.TableView.ClearSelection();
                        DetailDismissRequested?.Invoke();
                    });
                }
                else
                {
                    await Task.Run(() =>
                    {
                        foreach (var playlist in allPlaylists)
                        {
                            var words = 0;
                            var matches = 0;

                            var title = $" {playlist.Title.RemoveSpecialCharacters()} ";
                            var author = $" {playlist.Author.RemoveSpecialCharacters()} ";
                            var description = $" {playlist.Description.RemoveSpecialCharacters()} ";
                            
                            for (int i = 0; i < searchTerms.Length; i++)
                            {
                                if (!string.IsNullOrWhiteSpace(searchTerms[i]))
                                {
                                    words++;

                                    var searchTerm = $" {searchTerms[i]} ";
                                    if (i == searchTerms.Length - 1)
                                    {
                                        searchTerm = searchTerm.Substring(0, searchTerm.Length - 1);
                                    }

                                    if (title.IndexOf(searchTerm, 0, StringComparison.OrdinalIgnoreCase) != -1 ||
                                        author.IndexOf(searchTerm, 0, StringComparison.OrdinalIgnoreCase) != -1 ||
                                        description.IndexOf(searchTerm, 0, StringComparison.OrdinalIgnoreCase) != -1)
                                    {
                                        matches++;
                                    }
                                }
                            }

                            if (matches == words)
                            {
                                currentPlaylists.Add(playlist);
                            }
                        }
                    }, cancellationToken);
                }
                
                ShowPlaylists(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                
                await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    Loaded = true;
                    customListTableData.TableView.ReloadData();
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                Plugin.Log?.Error($"Unable to filter {currentSource?.GetType().Name ?? "playlist"} results: {e.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    playlistLoadSemaphore.Release();
                }
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
    }
}
