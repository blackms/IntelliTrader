using IntelliTrader.Core;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Text.Json;

namespace IntelliTrader.Core
{
    internal class CachingService(
        ILoggingService loggingService,
        IConfigProvider configProvider) : ConfigurableServiceBase<CachingConfig>(configProvider), ICachingService
    {
        public override string ServiceName => Constants.ServiceNames.CachingService;

        protected override ILoggingService LoggingService => loggingService;

        ICachingConfig ICachingService.Config => Config;

        private readonly ConcurrentDictionary<string, Lazy<CachedObject>> cachedObjects = new ConcurrentDictionary<string, Lazy<CachedObject>>();

        // Shared cache
        private readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions();
        private DateTimeOffset lastSharedCacheCleanup = DateTimeOffset.Now;
        private DirectoryInfo? sharedCacheDirectoryInfo;
        private readonly int processId = Process.GetCurrentProcess().Id;
        private readonly object sharedCacheLock = new object();

        public T? GetOrRefresh<T>(string objectName, Func<T> refresh)
        {
            T? value = default;

            if (Config.Enabled)
            {
                var maxAge = Config.MaxAge.FirstOrDefault(m => m.Key == objectName).Value;
                if (maxAge > 0)
                {
                    if (Config.Shared)
                    {
                        lock (sharedCacheLock)
                        {
                            if (sharedCacheDirectoryInfo == null)
                            {
                                sharedCacheDirectoryInfo = new DirectoryInfo(Config.SharedCachePath);
                            }

                            var existingCache = sharedCacheDirectoryInfo.GetFiles($"{objectName}*.{Constants.Caching.SharedCacheFileExtension}", SearchOption.TopDirectoryOnly)
                                .OrderByDescending(f => f.CreationTimeUtc).FirstOrDefault();

                            if (existingCache != null && (DateTime.UtcNow - existingCache.CreationTimeUtc).TotalSeconds <= maxAge)
                            {
                                try
                                {
                                    using var cacheFileStream = existingCache.OpenRead();
                                    value = JsonSerializer.Deserialize<T>(cacheFileStream, serializerOptions);
                                }
                                catch (Exception ex)
                                {
                                    loggingService.Error($"Unable to read cache file: {existingCache.Name}", ex);
                                    value = refresh();
                                }
                            }
                            else
                            {
                                string cacheFileName = $"{objectName}_{processId}.{Constants.Caching.SharedCacheFileExtension}";
                                string cacheFilePath = Path.Combine(Config.SharedCachePath, cacheFileName);
                                value = refresh();

                                try
                                {
                                    using var cacheFileStream = new FileStream(cacheFilePath, FileMode.Create, FileAccess.Write);
                                    JsonSerializer.Serialize(cacheFileStream, value, serializerOptions);
                                }
                                catch (Exception ex)
                                {
                                    loggingService.Error($"Unable to write cache file: {cacheFileName}", ex);
                                }
                            }

                            if ((DateTimeOffset.Now - lastSharedCacheCleanup).TotalSeconds > Config.SharedCacheCleanupInterval)
                            {
                                ThreadPool.QueueUserWorkItem((state) => CleanupSharedCache());
                            }
                        }
                    }
                    else
                    {
                        var lazyObj = cachedObjects.GetOrAdd(objectName, _ => new Lazy<CachedObject>(() => new CachedObject
                        {
                            LastUpdated = DateTimeOffset.Now,
                            Value = refresh()
                        }));

                        var obj = lazyObj.Value;

                        if ((DateTimeOffset.Now - obj.LastUpdated).TotalSeconds <= maxAge)
                        {
                            value = (T?)obj.Value;
                        }
                        else
                        {
                            // Entry expired, replace it atomically
                            var newLazy = new Lazy<CachedObject>(() => new CachedObject
                            {
                                LastUpdated = DateTimeOffset.Now,
                                Value = refresh()
                            });
                            cachedObjects[objectName] = newLazy;
                            value = (T?)newLazy.Value.Value;
                        }
                    }
                }
                else
                {
                    value = refresh();
                }
            }
            else
            {
                value = refresh();
            }

            return value;
        }

        private void CleanupSharedCache()
        {
            lock (sharedCacheLock)
            {
                if ((DateTimeOffset.Now - lastSharedCacheCleanup).TotalSeconds > Config.SharedCacheCleanupInterval)
                {
                    if (sharedCacheDirectoryInfo == null) return;

                    foreach (var cache in sharedCacheDirectoryInfo.EnumerateFiles($"*.{Constants.Caching.SharedCacheFileExtension}", SearchOption.TopDirectoryOnly))
                    {
                        if ((DateTimeOffset.Now - cache.CreationTimeUtc).TotalSeconds > Config.SharedCacheCleanupInterval)
                        {
                            try
                            {
                                cache.Delete();
                                loggingService.Debug($"Delete cache file: {cache.Name}");
                            }
                            catch (Exception ex)
                            {
                                loggingService.Error($"Unable to delete cache file: {cache.Name}", ex);
                            }
                        }
                    }

                    lastSharedCacheCleanup = DateTimeOffset.Now;
                }
            }
        }
    }
}
