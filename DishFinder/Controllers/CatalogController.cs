using DishFinder.Interfaces;
using DishFinder.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DishFinder.Controllers
{
    [Authorize]
    public class CatalogController : Controller
    {
        private readonly IFirestoreMenuRepository _firestoreMenuRepository;
        private readonly IMenuTranslationService _menuTranslationService;
        private readonly ILogger<CatalogController> _logger;

        public CatalogController(
            IFirestoreMenuRepository firestoreMenuRepository,
            IMenuTranslationService menuTranslationService,
            ILogger<CatalogController> logger)
        {
            _firestoreMenuRepository = firestoreMenuRepository;
            _menuTranslationService = menuTranslationService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchTerm, string? sortOrder = "price_asc")
        {
            List<CatalogMenuItemViewModel> items =
                await _firestoreMenuRepository.GetConfirmedCatalogItemsAsync(searchTerm, sortOrder);

            var model = new CatalogIndexViewModel
            {
                SearchTerm = searchTerm,
                SortOrder = sortOrder ?? "price_asc",
                Items = items
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TranslateItem(
            [FromForm] string restaurantId,
            [FromForm] string menuId,
            [FromForm] string itemName,
            [FromForm] string targetLanguage,
            [FromForm] string? sourceLanguage = "auto")
        {
            if (string.IsNullOrWhiteSpace(restaurantId) ||
                string.IsNullOrWhiteSpace(menuId) ||
                string.IsNullOrWhiteSpace(itemName) ||
                string.IsNullOrWhiteSpace(targetLanguage))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "restaurantId, menuId, itemName and targetLanguage are required."
                });
            }

            try
            {
               string? normalizedSourceLanguage =
                    string.IsNullOrWhiteSpace(sourceLanguage) ||
                    string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : sourceLanguage;

               var result = await _menuTranslationService.TranslateAsync(
                    restaurantId,
                    menuId,
                    itemName,
                    targetLanguage,
                    normalizedSourceLanguage);

                return Json(new
                {
                    success = true,
                    originalText = result.OriginalText,
                    translatedText = result.TranslatedText,
                    targetLanguage = result.TargetLanguage,
                    sourceLanguage = result.SourceLanguage,
                    cachedAtUtc = result.CachedAtUtc
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "TranslateItem failed. RestaurantId={RestaurantId}, MenuId={MenuId}, ItemName={ItemName}, TargetLanguage={TargetLanguage}",
                    restaurantId, menuId, itemName, targetLanguage);

                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

    }
}
