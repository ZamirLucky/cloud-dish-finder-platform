using DishFinder.Interfaces;
using DishFinder.Models;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.RegularExpressions;


namespace DishFinder.Services
{
    /// <summary>
    /// Redis-backed cache for translated menu items.
    /// Uses deterministic keys for lookups and maintains index sets to support efficient invalidation
    /// by menu and by restaurant.
    /// </summary>
    public sealed class RedisTranslationCacheService : ITranslationCacheService, IDisposable
    {
        private readonly ILogger<RedisTranslationCacheService> _logger;
        private readonly Lazy<ConnectionMultiplexer> _connection;

        public RedisTranslationCacheService(
            IConfiguration configuration,
            ILogger<RedisTranslationCacheService> logger)
        {
            _logger = logger;

            string host = configuration["Redis:Host"]
                ?? throw new InvalidOperationException("Missing Redis:Host");

            string portText = configuration["Redis:Port"]
                ?? throw new InvalidOperationException("Missing Redis:Port");

            string user = configuration["Redis:User"]
                ?? throw new InvalidOperationException("Missing Redis:User");

            string password = configuration["Redis:Password"]
                ?? throw new InvalidOperationException("Missing Redis:Password");

            if (!int.TryParse(portText, out int port))
            {
                throw new InvalidOperationException("Redis:Port must be a valid integer.");
            }

            // Lazy connection so app startup doesn't fail if Redis is temporarily unavailable.
            _connection = new Lazy<ConnectionMultiplexer>(() =>
                ConnectionMultiplexer.Connect(new ConfigurationOptions
                {
                    EndPoints = { { host, port } },
                    User = user,
                    Password = password,
                    AbortOnConnectFail = false,
                    ConnectTimeout = 10000,
                    SyncTimeout = 10000
                }));
        }


        private IDatabase Db => _connection.Value.GetDatabase();

        /// <summary>
        /// Retrieves a cached translation by its deterministic key.
        /// Returns null when the cache entry is missing.
        /// </summary>
        public async Task<CachedTranslationResultModel?> GetAsync(
            string restaurantId,
            string menuId,
            string itemName,
            string targetLanguage)
        {
            string key = BuildTranslationKey(restaurantId, menuId, itemName, targetLanguage);

            RedisValue cached = await Db.StringGetAsync(key);

            if (cached.IsNullOrEmpty)
            {
                _logger.LogInformation("Redis cache miss for key {CacheKey}", key);
                return null;
            }

            _logger.LogInformation("Redis cache hit for key {CacheKey}", key);

            return JsonSerializer.Deserialize<CachedTranslationResultModel>(cached!);
        }

        /// <summary>
        /// Stores a translation and registers it in menu/restaurant index sets to enable invalidation.
        /// </summary>
        public async Task SetAsync(
            string restaurantId,
            string menuId,
            string itemName,
            string targetLanguage,
            CachedTranslationResultModel value,
            TimeSpan? expiry = null)
        {
            string translationKey = BuildTranslationKey(restaurantId, menuId, itemName, targetLanguage);
            string menuIndexKey = BuildMenuIndexKey(restaurantId, menuId);
            string restaurantIndexKey = BuildRestaurantIndexKey(restaurantId);

            string json = JsonSerializer.Serialize(value);

            await Db.StringSetAsync(
                translationKey,
                json,
                expiry ?? TimeSpan.FromDays(7));

            // Maintain reverse indexes so we can delete all translations for a menu/restaurant efficiently.
            await Db.SetAddAsync(menuIndexKey, translationKey);
            await Db.SetAddAsync(restaurantIndexKey, translationKey);

            _logger.LogInformation("Stored translation in Redis for key {CacheKey}", translationKey);
        }

        /// <summary>
        /// Removes all cached translations for a specific menu and updates related index sets.
        /// </summary>
        public async Task InvalidateMenuAsync(string restaurantId, string menuId)
        {
            string menuIndexKey = BuildMenuIndexKey(restaurantId, menuId);
            string restaurantIndexKey = BuildRestaurantIndexKey(restaurantId);

            RedisValue[] translationKeys = await Db.SetMembersAsync(menuIndexKey);

            if (translationKeys.Length > 0)
            {
                RedisKey[] redisKeys = translationKeys
                    .Select(x => (RedisKey)x.ToString())
                    .ToArray();

                await Db.KeyDeleteAsync(redisKeys);

                foreach (RedisValue key in translationKeys)
                {
                    await Db.SetRemoveAsync(restaurantIndexKey, key);
                }
            }

            await Db.KeyDeleteAsync(menuIndexKey);

            _logger.LogInformation(
                "Invalidated cache for menu. RestaurantId={RestaurantId}, MenuId={MenuId}",
                restaurantId,
                menuId);
        }

        /// <summary>
        /// Removes all cached translations for an entire restaurant.
        /// </summary>
        public async Task InvalidateRestaurantAsync(string restaurantId)
        {
            string restaurantIndexKey = BuildRestaurantIndexKey(restaurantId);

            RedisValue[] translationKeys = await Db.SetMembersAsync(restaurantIndexKey);

            if (translationKeys.Length > 0)
            {
                RedisKey[] redisKeys = translationKeys
                    .Select(x => (RedisKey)x.ToString())
                    .ToArray();

                await Db.KeyDeleteAsync(redisKeys);
            }

            await Db.KeyDeleteAsync(restaurantIndexKey);

            _logger.LogInformation(
                "Invalidated cache for restaurant. RestaurantId={RestaurantId}",
                restaurantId);
        }

        // Key format:
        // translation:{restaurantId}:{menuId}:{normalizedItemName}:{normalizedLanguage}
        private static string BuildTranslationKey(
            string restaurantId,
            string menuId,
            string itemName,
            string targetLanguage)
        {
            string normalizedName = Normalize(itemName);
            string normalizedLanguage = targetLanguage.Trim().ToLowerInvariant();

            return $"translation:{restaurantId}:{menuId}:{normalizedName}:{normalizedLanguage}";
        }

        // Index sets to support invalidation:
        // - menu index: holds translation keys for a single menu
        // - restaurant index: holds translation keys for all menus under a restaurant
        private static string BuildMenuIndexKey(string restaurantId, string menuId) =>
            $"translation:index:menu:{restaurantId}:{menuId}";

        private static string BuildRestaurantIndexKey(string restaurantId) =>
            $"translation:index:restaurant:{restaurantId}";

        // Normalizes input to keep cache keys stable across trivial formatting differences.
        private static string Normalize(string input)
        {
            string value = input.Trim().ToLowerInvariant();
            value = Regex.Replace(value, @"\s+", " ");
            value = Regex.Replace(value, @"[^a-z0-9\s&-]", "");
            return value;
        }

        public void Dispose()
        {
            if (_connection.IsValueCreated)
            {
                _connection.Value.Dispose();
            }
        }
    }
}
