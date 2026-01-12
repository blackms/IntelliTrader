using IntelliTrader.Core;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;

namespace IntelliTrader.Core
{
    internal class CachingService : ConfigrableServiceBase<CachingConfig>, ICachingService
    {
        public override string ServiceName => Constants.ServiceNames.CachingService;

        ICachingConfig ICachingService.Config => Config;

        private readonly ILoggingService loggingService;
        private readonly ConcurrentDictionary<string, CachedObject> cachedObjects = new ConcurrentDictionary<string, CachedObject>();

        // Shared cache
        private readonly JsonSerializer serializer;
        private DateTimeOffset lastSharedCacheCleanup;
        private DirectoryInfo sharedCacheDirectoryInfo;
        private int processId;

        public CachingService(ILoggingService loggingService)
        {
            this.loggingService = loggingService;

            this.serializer = new JsonSerializer();
            this.lastSharedCacheCleanup = DateTimeOffset.Now;
            this.processId = Process.GetCurrentProcess().Id;
        }

        public T GetOrRefresh<T>(string objectName, Func<T> refresh)
        {
            lock (cachedObjects)
            {
                T value = default(T);

                if (Config.Enabled)
                {
                    var maxAge = Config.MaxAge.FirstOrDefault(m => m.Key == objectName).Value;
                    if (maxAge > 0)
                    {
                        if (Config.Shared)
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
                                    using (var cacheFile = existingCache.OpenText())
                                    using (var cacheReader = new JsonTextReader(cacheFile))
                                    {
                                        value = serializer.Deserialize<T>(cacheReader);
                                    }
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
                                    using (var cacheFile = new StreamWriter(cacheFilePath))
                                    using (var cacheWriter = new JsonTextWriter(cacheFile))
                                    {
                                        serializer.Serialize(cacheWriter, value);
                                    }
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
                        else
                        {
                            if (cachedObjects.TryGetValue(objectName, out CachedObject obj) && (DateTimeOffset.Now - obj.LastUpdated).TotalSeconds <= maxAge)
                            {
                                value = (T)obj.Value;
                            }
                            else
                            {
                                value = refresh();

                                cachedObjects[objectName] = new CachedObject
                                {
                                    LastUpdated = DateTimeOffset.Now,
                                    Value = value
                                };

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
        }

        private void CleanupSharedCache()
        {
            lock (cachedObjects)
            {
                if ((DateTimeOffset.Now - lastSharedCacheCleanup).TotalSeconds > Config.SharedCacheCleanupInterval)
                {
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