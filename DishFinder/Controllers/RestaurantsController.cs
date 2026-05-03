

using DishFinder.Interfaces;
using DishFinder.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace DishFinder.Controllers
{
    [Authorize]
    public class RestaurantsController : Controller
    {
        private readonly IFirestoreMenuRepository _firestoreMenuRepository;
        private readonly ILogger<RestaurantsController> _logger;

        public RestaurantsController(
            IFirestoreMenuRepository firestoreMenuRepository,
            ILogger<RestaurantsController> logger)
        {
            _firestoreMenuRepository = firestoreMenuRepository;
            _logger = logger;
        }

        // GET: Restaurants/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View(new RestaurantCreateViewModel());
        }

        // POS: Restaurents/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RestaurantCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string restaurantId = BuildRestaurantId(model.Name, model.Address);

            await _firestoreMenuRepository.CreateOrUpdateRestaurantAsync(
                restaurantId: restaurantId,
                name: model.Name.Trim(),
                address: model.Address.Trim(),
                status: "pending");

            TempData["SuccessMessage"] = "Restaurant created successfully.";
            _logger.LogInformation("Restaurant created: {RestaurantId}", restaurantId);

            return RedirectToAction(nameof(Create));
        }

        // Generates a unique restaurant ID by combining the name and address, converting to lowercase, and replacing non-alphanumeric characters with hyphens.
        //  If the resulting ID is empty, a new GUID is used.
        private static string BuildRestaurantId(string name, string address)
        {
            string raw = $"{name}-{address}".ToLowerInvariant().Trim();
            string cleaned = Regex.Replace(raw, @"[^a-z0-9]+", "-").Trim('-');

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = Guid.NewGuid().ToString("N");
            }

            return cleaned;
        }


    }
}
