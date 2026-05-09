using DishFinder.Models;

namespace DishFinder.Interfaces
{
    public interface IMenuTranslationService
    {
        Task<CachedTranslationResultModel> TranslateAsync(
        string restaurantId,
        string menuId,
        string itemName,
        string targetLanguage,
        string? sourceLanguage = "auto");
    }
}
