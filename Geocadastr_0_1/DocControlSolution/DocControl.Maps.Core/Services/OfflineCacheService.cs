using DocControl.Maps.Core.Data;
using DocControl.Maps.Core.Interfaces;
using DocControl.Maps.Core.Models;
using System;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Services
{
    /// <summary>
    /// Сервіс офлайн-кешування карт
    /// </summary>
    public class OfflineCacheService : IOfflineCache
    {
        private readonly MapCacheRepository _repository;
        private readonly IMapProvider _provider;

        public OfflineCacheService(MapCacheRepository repository, IMapProvider provider)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task<MapTile> GetCachedTileAsync(int x, int y, int zoom)
        {
            return await _repository.GetTileAsync(x, y, zoom, _provider.ProviderName);
        }

        public async Task SaveTileAsync(MapTile tile)
        {
            await _repository.SaveTileAsync(tile);
        }

        public async Task<bool> IsTileCachedAsync(int x, int y, int zoom)
        {
            return await _repository.IsTileCachedAsync(x, y, zoom, _provider.ProviderName);
        }

        public async Task<long> GetCacheSizeAsync()
        {
            return await _repository.GetCacheSizeAsync();
        }

        public async Task ClearCacheAsync()
        {
            await _repository.ClearCacheAsync();
        }

        public async Task ClearOldCacheAsync(int daysOld)
        {
            await _repository.ClearOldCacheAsync(daysOld);
        }

        public async Task<CachedRegion> DownloadRegionAsync(double minLat, double minLon,
            double maxLat, double maxLon, int minZoom, int maxZoom)
        {
            var region = new CachedRegion
            {
                Name = $"Region_{DateTime.Now:yyyyMMdd_HHmmss}",
                MinLatitude = minLat,
                MinLongitude = minLon,
                MaxLatitude = maxLat,
                MaxLongitude = maxLon,
                MinZoom = minZoom,
                MaxZoom = maxZoom,
                Provider = _provider.ProviderName,
                DownloadedAt = DateTime.Now
            };

            int totalTiles = 0;
            long totalSize = 0;

            for (int zoom = minZoom; zoom <= maxZoom; zoom++)
            {
                var (minX, minY, maxX, maxY) = LatLonToTile(minLat, minLon, maxLat, maxLon, zoom);

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        try
                        {
                            // Перевіряємо чи вже є в кеші
                            if (await IsTileCachedAsync(x, y, zoom))
                                continue;

                            // Завантажуємо тайл
                            var imageData = await _provider.DownloadTileAsync(x, y, zoom);

                            if (imageData != null && imageData.Length > 0)
                            {
                                var tile = new MapTile
                                {
                                    X = x,
                                    Y = y,
                                    Zoom = zoom,
                                    Provider = _provider.ProviderName,
                                    ImageData = imageData,
                                    DownloadedAt = DateTime.Now,
                                    IsCached = true
                                };

                                await SaveTileAsync(tile);

                                totalTiles++;
                                totalSize += imageData.Length;
                            }

                            // Затримка щоб не перевантажувати сервер
                            await Task.Delay(100);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error downloading tile {x},{y} at zoom {zoom}: {ex.Message}");
                        }
                    }
                }
            }

            region.TileCount = totalTiles;
            region.SizeBytes = totalSize;

            region.Id = await _repository.SaveRegionAsync(region);

            return region;
        }

        private (int minX, int minY, int maxX, int maxY) LatLonToTile(
            double minLat, double minLon, double maxLat, double maxLon, int zoom)
        {
            int minX = LonToTileX(minLon, zoom);
            int maxX = LonToTileX(maxLon, zoom);
            int minY = LatToTileY(maxLat, zoom);
            int maxY = LatToTileY(minLat, zoom);

            return (minX, minY, maxX, maxY);
        }

        private int LonToTileX(double lon, int zoom)
        {
            return (int)Math.Floor((lon + 180.0) / 360.0 * Math.Pow(2, zoom));
        }

        private int LatToTileY(double lat, int zoom)
        {
            double latRad = lat * Math.PI / 180.0;
            return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * Math.Pow(2, zoom));
        }
    }
}