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

        // GET: Upload/Create (renders the upload page and restaurant dropdown)
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = new MenuUploadViewModel();
            await PopulateRestaurantsAsync(model);
            return View(model);
        }

        // AJAX step 1: create/persist a new Menu record and return its generated `menuId`.
        // Images are uploaded in a separate request so the UI can show per-file progress.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartMenuUpload([FromForm] StartMenuUploadRequestModel model)
        {
            if (!ModelState.IsValid){
                    return BadRequest(new { success = false, message = "Invalid menu upload request." });
            }
            
            string menuId = Guid.NewGuid().ToString("N");

            await _firestoreMenuRepository.CreateOrUpdateMenuAsync(
                restaurantId: model.RestaurantId,
                menuId: menuId,
                menuTitle: model.MenuTitle.Trim(),
                ocrText: "",
                status: "pending");

            return Json(new
            {
                success = true,
                menuId = menuId
            });
        }

        // AJAX step 2: upload one image to Cloud Storage, then write an image document under:
        // `restaurants/{restaurantId}/menus/{menuId}/images`.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadSingleImage([FromForm] UploadSingleImageRequestModel model)
        {
            if (!ModelState.IsValid || model.File == null || model.File.Length == 0)
            {
                return BadRequest(new { success = false, message = "Invalid file upload request." });
            }

            var uploadResult = await _bucketStorageService.UploadMenuImageAsync(
                model.File,
                model.RestaurantId,
                model.MenuId);

            string uploadedBy =
                User.FindFirst("email")?.Value
                ?? User.Identity?.Name
                ?? "unknown";

            string imageId = await _firestoreMenuRepository.AddImageReferenceAsync(
                restaurantId: model.RestaurantId,
                menuId: model.MenuId,
                uploadResult: uploadResult,
                uploadedBy: uploadedBy);

            return Json(new
            {
                success = true,
                imageId = imageId,
                fileName = uploadResult.OriginalFileName,
                objectName = uploadResult.ObjectName
            });
        }

        // Loads restaurants to populate the Create page dropdown.
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
