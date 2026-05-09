using DishFinder.Models;

namespace DishFinder.Interfaces
{
    public interface ITranslationCacheService
    {
        // Get a translation result from the cache
        Task<CachedTranslationResultModel?> GetAsync(
        string restaurantId,
        string menuId,
        string itemName,
        string targetLanguage);

        // Set a translation result in the cache
        Task SetAsync(
            string restaurantId,
            string menuId,
            string itemName,
            string targetLanguage,
            CachedTranslationResultModel value,
            TimeSpan? expiry = null);
        
        // Invalidate the cache for a specific menu
        Task InvalidateMenuAsync(string restaurantId, string menuId);

        // Invalidate the cache for an entire restaurant (all menus)
        Task InvalidateRestaurantAsync(string restaurantId);
    }
}
