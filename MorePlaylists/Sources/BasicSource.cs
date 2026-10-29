using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HMUI;
using MorePlaylists.Entries;
using MorePlaylists.UI;
using MorePlaylists.Utilities;
using Newtonsoft.Json;
using SiraUtil.Web;
using UnityEngine;

namespace MorePlaylists.Sources
{
    internal abstract class BasicSource<T> : IBasicSource where T : IBasicEntry
    {
        protected abstract string Website { get; }
        protected abstract string Endpoint { get; }
        protected virtual IEnumerable<string> Endpoints => new[] { Website + Endpoint };
        public abstract Sprite Logo { get; }

        private readonly Dictionary<string, List<T>> endpointCache = new();
        private readonly Dictionary<string, DateTime> cacheTimes = new();
        private readonly HashSet<string> failedEndpoints = new();
        private readonly SemaphoreSlim cacheSemaphore = new(1, 1);
        public string? LoadWarning { get; private set; }
        private IHttpService SiraHttpService { get; }
        public IListViewController ListViewController { get; }
        public IDetailViewController DetailViewController { get; }
        public event Action<ViewController, ViewController.AnimationDirection>? ViewControllerRequested;
        public event Action<ViewController, ViewController.AnimationDirection>? ViewControllerDismissRequested;

        protected BasicSource(IHttpService siraHttpService, BasicListViewController listViewController, BasicDetailViewController detailViewController)
        {
            SiraHttpService = siraHttpService;
            ListViewController = listViewController;
            DetailViewController = detailViewController;
        }
        
        public async Task<List<IBasicEntry>?> GetEndpointResult(bool refreshRequested, CancellationToken token)
        {
            await cacheSemaphore.WaitAsync(token);
            try
            {
                var retryFailedOnly = refreshRequested && failedEndpoints.Count > 0;
                LoadWarning = null;
                var result = new List<T>();
                foreach (var endpoint in Endpoints)
                {
                    token.ThrowIfCancellationRequested();
                    if ((!refreshRequested || retryFailedOnly) && !failedEndpoints.Contains(endpoint) &&
                        endpointCache.TryGetValue(endpoint, out var cached) &&
                        cacheTimes.TryGetValue(endpoint, out var cachedAt) && DateTime.UtcNow - cachedAt < TimeSpan.FromMinutes(15))
                    {
                        result.AddRange(cached);
                        continue;
                    }
                    try
                    {
                        var payload = await RemoteRequest.GetText(SiraHttpService, endpoint, token);
                        var parsed = DeserializeEntries(payload);
                        if (parsed == null)
                        {
                            throw new JsonSerializationException($"The {typeof(T).Name} endpoint returned a null payload: {endpoint}");
                        }

                        token.ThrowIfCancellationRequested();
                        endpointCache[endpoint] = parsed;
                        cacheTimes[endpoint] = DateTime.UtcNow;
                        failedEndpoints.Remove(endpoint);
                        result.AddRange(parsed);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (endpointCache.TryGetValue(endpoint, out _))
                    {
                        failedEndpoints.Add(endpoint);
                        LoadWarning = "Showing saved playlists. Refresh failed; retry to update.";
                        Plugin.Log?.Warn($"Refresh failed; retaining successful playlist cache for {endpoint}: {exception}");
                        result.AddRange(endpointCache[endpoint]);
                    }
                    catch (Exception)
                    {
                        failedEndpoints.Add(endpoint);
                        throw;
                    }
                }

                return result.Cast<IBasicEntry>().ToList();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Plugin.Log?.Error($"An error occurred while trying to fetch the {typeof(T).Name} playlists\nException: {exception}");
                throw;
            }
            finally
            {
                cacheSemaphore.Release();
            }
        }

        private static List<T>? DeserializeEntries(string payload)
        {
            var content = payload.TrimStart();
            if (content.StartsWith("[", StringComparison.Ordinal))
            {
                return JsonConvert.DeserializeObject<List<T>>(content);
            }

            if (content.StartsWith("{", StringComparison.Ordinal))
            {
                var entry = JsonConvert.DeserializeObject<T>(content);
                return entry == null ? null : new List<T> { entry };
            }

            throw new JsonSerializationException("Playlist endpoint returned neither a JSON array nor an object.");
        }

    }
}
