using DishFinder.Interfaces;
using DishFinder.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DishFinder.Controllers
{
    public class UploadController : Controller
    {
        private readonly IBucketStorageService _bucketStorageService;
        private readonly IFirestoreMenuRepository _firestoreMenuRepository;
        private readonly ILogger<UploadController> _logger;

        public UploadController(
            IFirestoreMenuRepository firestoreMenuRepository, 
            ILogger<UploadController> logger,
            IBucketStorageService bucketStorageService)
        {
            _firestoreMenuRepository = firestoreMenuRepository;
            _logger = logger;
            _bucketStorageService = bucketStorageService;
        }

        // GET: Upload/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = new MenuUploadViewModel();
            await PopulateRestaurantsAsync(model);
            return View(model);
        }

        // Post: Upload/create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MenuUploadViewModel model)
        {
            if (model.MenuImage == null || model.MenuImage.Length == 0)
            {
                ModelState.AddModelError(nameof(model.MenuImage), "Please select one image.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateRestaurantsAsync(model);
                return View(model);
            }

            // create menu, upload image, save image reference
            string menuId = Guid.NewGuid().ToString("N");

            await _firestoreMenuRepository.CreateOrUpdateMenuAsync(
                restaurantId: model.RestaurantId,
                menuId: menuId,
                menuTitle: model.MenuTitle.Trim(),
                ocrText: "",
                status: "pending");

            var uploadResult = await _bucketStorageService.UploadMenuImageAsync(
                model.MenuImage!,
                model.RestaurantId,
                menuId);

            string uploadedBy =
                User.FindFirst("email")?.Value
                ?? User.Identity?.Name
                ?? "unknown";

            string imageId = await _firestoreMenuRepository.AddImageReferenceAsync(
                restaurantId: model.RestaurantId,
                menuId: menuId,
                uploadResult: uploadResult,
                uploadedBy: uploadedBy);

            // Log the upload result and menu details
            _logger.LogInformation(
                "Menu created, image uploaded, and image reference saved. RestaurantId={RestaurantId}, MenuId={MenuId}, ImageId={ImageId}",
                model.RestaurantId,
                menuId,
                imageId);

            TempData["SuccessMessage"] = "Menu created and image uploaded successfully.";

            return RedirectToAction(nameof(Create));

        }

        // Helper method to populate the list of restaurants for the dropdown
        private async Task PopulateRestaurantsAsync(MenuUploadViewModel model)
        {
            var restaurants = await _firestoreMenuRepository.GetRestaurantsAsync();

            model.Restaurants = restaurants
                .Select(r => new SelectListItem
                {
                    Value = r.Id,
                    Text = r.Name
                })
                .ToList();
        }

    }
}
