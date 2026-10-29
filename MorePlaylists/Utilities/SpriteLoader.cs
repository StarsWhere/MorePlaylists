using BeatSaberPlaylistsLib.Types;
using MorePlaylists.Entries;
using SiraUtil;
using SiraUtil.Web;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberPlaylistsLib;
using MorePlaylists.Sources;
using UnityEngine;

namespace MorePlaylists.Utilities
{
    internal class SpriteLoader
    {
        private readonly IHttpService siraHttpService;
        private readonly ConcurrentDictionary<string, Sprite> cachedSprites;
        private readonly Queue<Action> spriteQueue;
        private readonly object queueLock;
        private bool coroutineRunning;

        public SpriteLoader(IHttpService siraHttpService)
        {
            this.siraHttpService = siraHttpService;
            cachedSprites = new ConcurrentDictionary<string, Sprite>();
            spriteQueue = new Queue<Action>();
            queueLock = new object();
        }
        
        public async Task DownloadSpriteAsync(string spriteURL, Action<Sprite> onCompletion, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(spriteURL))
            {
                Plugin.Log?.Warn("A playlist image URL was empty.");
                if (!cancellationToken.IsCancellationRequested)
                {
                    onCompletion?.Invoke(BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite);
                }
                return;
            }

            // Check Cache
            if (cachedSprites.TryGetValue(spriteURL, out var cachedSprite))
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    onCompletion?.Invoke(cachedSprite);
                }
                return;
            }

            try
            {
                if (spriteURL.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var separator = spriteURL.IndexOf(',');
                    if (separator < 0)
                    {
                        throw new InvalidDataException("The data URI has no payload.");
                    }

                    var imageBytes = Convert.FromBase64String(spriteURL.Substring(separator + 1));
                    QueueLoadSprite(spriteURL, imageBytes, onCompletion, cancellationToken);
                    return;
                }

                var downloadedImage = await RemoteRequest.GetPlaylist(siraHttpService, spriteURL, cancellationToken, responseStream =>
                {
                    using var downscaledStream = BeatSaberPlaylistsLib.Utilities.DownscaleImage(responseStream, 128);
                    return downscaledStream.ToArray();
                });
                if (!cancellationToken.IsCancellationRequested)
                {
                    QueueLoadSprite(spriteURL, downloadedImage, onCompletion, cancellationToken);
                }
            }
            catch (Exception e)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Plugin.Log?.Warn($"Unable to load playlist image '{spriteURL}': {e.Message}");
                }
                if (!cancellationToken.IsCancellationRequested)
                {
                    onCompletion?.Invoke(BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite);
                }
            }
        }

        private void QueueLoadSprite(string key, byte[] imageBytes, Action<Sprite> onCompletion, CancellationToken cancellationToken)
        {
            spriteQueue.Enqueue(() =>
            {
                try
                {
                    var sprite = PlaylistMods.Common.SpriteLoader.FromBytes(imageBytes);
                    sprite.texture.wrapMode = TextureWrapMode.Clamp;
                    cachedSprites.TryAdd(key, sprite);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        onCompletion?.Invoke(sprite);
                    }
                }
                catch (Exception)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        onCompletion?.Invoke(BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite);
                    }
                }
            });
            if (!coroutineRunning)
            {
                SharedCoroutineStarter.instance!.StartCoroutine(SpriteLoadCoroutine());
            }
        }

        private static readonly YieldInstruction LoadWait = new WaitForEndOfFrame();
        private IEnumerator<YieldInstruction> SpriteLoadCoroutine()
        {
            lock (queueLock)
            {
                if (coroutineRunning)
                    yield break;
                coroutineRunning = true;
            }
            while (spriteQueue.Count > 0)
            {
                yield return LoadWait;
                if (spriteQueue.Count == 0)
                {
                    break;
                }
                var loader = spriteQueue.Dequeue();
                _ = IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => loader?.Invoke());
            }
            coroutineRunning = false;
            if (spriteQueue.Count > 0)
            {
                SharedCoroutineStarter.instance!.StartCoroutine(SpriteLoadCoroutine());
            }
        }

    }
}
