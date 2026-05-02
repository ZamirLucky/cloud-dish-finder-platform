using DishFinder.Interfaces;
using DishFinder.Models;
using Google.Cloud.Firestore;

namespace DishFinder.DataAccess
{
    public class FirestoreMenuRepository : IFirestoreMenuRepository
    {
        private readonly ILogger<FirestoreMenuRepository> _logger;
        private readonly FirestoreDb _db;

        public FirestoreMenuRepository(
            ILogger<FirestoreMenuRepository> logger,
            FirestoreDb db)
        {
            _logger = logger;
            _db = db;
        }

        // Adds a reference to an uploaded menu image in Firestore under the specified restaurant and menu.
        public async Task<string> AddImageReferenceAsync(
            string restaurantId, 
            string menuId, 
            StorageUploadResultModel uploadResult, 
            string uploadedBy)
        {
            if (string.IsNullOrWhiteSpace(restaurantId))
                throw new ArgumentException("restaurantId is required", nameof(restaurantId));

            if (string.IsNullOrWhiteSpace(menuId))
                throw new ArgumentException("menuId is required", nameof(menuId));

            if (uploadResult == null)
                throw new ArgumentNullException(nameof(uploadResult));

            CollectionReference imagesRef = _db
                .Collection("restaurants")
                .Document(restaurantId)
                .Collection("menus")
                .Document(menuId)
                .Collection("images");

            var imageData = new Dictionary<string, object>
            {
                ["fileName"] = uploadResult.OriginalFileName,
                ["bucketName"] = uploadResult.BucketName,
                ["objectName"] = uploadResult.ObjectName,
                ["contentType"] = uploadResult.ContentType,
                ["uploadedBy"] = uploadedBy,
                ["uploadedAt"] = FieldValue.ServerTimestamp,
                ["gsUri"] = uploadResult.GsUri
            };

            DocumentReference addedDoc = await imagesRef.AddAsync(imageData);

            _logger.LogInformation(
                "Image reference added. RestaurantId={RestaurantId}, MenuId={MenuId}, ImageId={ImageId}",
                restaurantId,
                menuId,
                addedDoc.Id);

            return addedDoc.Id;
        }

        // Creates or updates a menu document in Firestore with the given details under the specified restaurant.
        public async Task CreateOrUpdateMenuAsync(
            string restaurantId, 
            string menuId, 
            string menuTitle, 
            string? ocrText = "", 
            string status = "pending")
        {
            if (string.IsNullOrWhiteSpace(restaurantId))
                throw new ArgumentException("restaurantId is required", nameof(restaurantId));

            if (string.IsNullOrWhiteSpace(menuId))
                throw new ArgumentException("menuId is required", nameof(menuId));

            DocumentReference menuRef = _db
                .Collection("restaurants")
                .Document(restaurantId)
                .Collection("menus")
                .Document(menuId);

            DocumentSnapshot snapshot = await menuRef.GetSnapshotAsync();

            var data = new Dictionary<string, object>
            {
                ["menuTitle"] = menuTitle,
                ["ocrText"] = ocrText ?? "",
                ["status"] = status,
                ["latestUploadAt"] = FieldValue.ServerTimestamp,
                ["updatedAt"] = FieldValue.ServerTimestamp
            };

            if (!snapshot.Exists)
            {
                data["createdAt"] = FieldValue.ServerTimestamp;
                data["lastProcessedAt"] = null!;
            }

            await menuRef.SetAsync(data, SetOptions.MergeAll);

            _logger.LogInformation(
                "Menu created/updated: {RestaurantId}/{MenuId}",
                restaurantId,
                menuId);
        }

        // Creates or updates a restaurant document in Firestore with the given details.
        public async Task CreateOrUpdateRestaurantAsync(
            string restaurantId, 
            string name, 
            string address, 
            string status = "pending")
        {
            if (string.IsNullOrWhiteSpace(restaurantId))
                throw new ArgumentException("restaurantId is required", nameof(restaurantId));

            DocumentReference restaurantRef =
                _db.Collection("restaurants").Document(restaurantId);

            DocumentSnapshot snapshot = await restaurantRef.GetSnapshotAsync();

            var data = new Dictionary<string, object>
            {
                ["name"] = name,
                ["address"] = address,
                ["status"] = status,
                ["updatedAt"] = FieldValue.ServerTimestamp
            };

            if (!snapshot.Exists)
            {
                data["createdAt"] = FieldValue.ServerTimestamp;
            }

            await restaurantRef.SetAsync(data, SetOptions.MergeAll);

            _logger.LogInformation(
                "Restaurant created/updated: {RestaurantId}",
                restaurantId);

        }

        // Retrieves a list of restaurants from Firestore, ordered by name, and returns them as view models.
        public async Task<List<RestaurantOptionViewModel>> GetRestaurantsAsync()
        {
            QuerySnapshot snapshot = await _db
                .Collection("restaurants")
                .OrderBy("name")
                .GetSnapshotAsync();

            var restaurants = new List<RestaurantOptionViewModel>();

            foreach (DocumentSnapshot doc in snapshot.Documents)
            {
                string name = doc.ContainsField("name")
                    ? doc.GetValue<string>("name")
                    : doc.Id;

                restaurants.Add(new RestaurantOptionViewModel
                {
                    Id = doc.Id,
                    Name = name
                });
            }

            return restaurants;
        }
    }
}
