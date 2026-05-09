using DishFinder.Interfaces;
using DishFinder.Models;
using Google.Cloud.Firestore;
using System.Globalization;

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

        //
        public async Task<List<CatalogMenuItemViewModel>> GetConfirmedCatalogItemsAsync(
            string? searchTerm,
            string? sortOrder)
        {
            var results = new List<CatalogMenuItemViewModel>();
            var restaurantNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            QuerySnapshot menusSnapshot = await _db
                .CollectionGroup("menus")
                .WhereEqualTo("status", "confirmed")
                .GetSnapshotAsync();

            foreach (DocumentSnapshot menuDoc in menusSnapshot.Documents)
            {
                string menuId = menuDoc.Id;
                string restaurantId = menuDoc.Reference.Parent.Parent?.Id ?? string.Empty;

                if (string.IsNullOrWhiteSpace(restaurantId))
                {
                    continue;
                }

                string restaurantName = await GetRestaurantNameAsync(restaurantId, restaurantNameCache);

                string? menuTitle = menuDoc.ContainsField("menuTitle")
                    ? menuDoc.GetValue<string>("menuTitle")
                    : null;

                Dictionary<string, object> menuData = menuDoc.ToDictionary();

                if (!menuData.TryGetValue("parsedItems", out object? parsedItemsObj) ||
                    parsedItemsObj is not IEnumerable<object> parsedItemsList)
                {
                    continue;
                }

                foreach (object parsedItemObj in parsedItemsList)
                {
                    if (parsedItemObj is not IDictionary<string, object> itemMap)
                    {
                        continue;
                    }

                    string itemName = GetString(itemMap, "name") ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(itemName))
                    {
                        continue;
                    }

                    decimal? priceValue = GetDecimal(itemMap, "priceValue");
                    string? priceText = GetString(itemMap, "priceText");

                    results.Add(new CatalogMenuItemViewModel
                    {
                        RestaurantId = restaurantId,
                        RestaurantName = restaurantName,
                        MenuId = menuId,
                        MenuTitle = menuTitle,
                        ItemName = itemName,
                        Description = GetString(itemMap, "description"),
                        Section = GetString(itemMap, "section") ?? "UNCATEGORISED",
                        PriceText = priceText ?? (priceValue.HasValue ? $"€{priceValue.Value:0.00}" : string.Empty),
                        PriceValue = priceValue,
                        NormalizedName = GetString(itemMap, "normalizedName") ?? itemName.ToLowerInvariant()
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.Trim().ToLowerInvariant();

                results = results
                    .Where(x =>
                        x.ItemName.ToLowerInvariant().Contains(term) ||
                        (x.Description?.ToLowerInvariant().Contains(term) ?? false) ||
                        x.RestaurantName.ToLowerInvariant().Contains(term) ||
                        (x.Section?.ToLowerInvariant().Contains(term) ?? false) ||
                        (x.MenuTitle?.ToLowerInvariant().Contains(term) ?? false))
                    .ToList();
            }

            results = (sortOrder ?? "price_asc").ToLowerInvariant() switch
            {
                "price_desc" => results
                    .OrderBy(x => x.PriceValue.HasValue ? 0 : 1)
                    .ThenByDescending(x => x.PriceValue)
                    .ThenBy(x => x.ItemName)
                    .ToList(),

                _ => results
                    .OrderBy(x => x.PriceValue.HasValue ? 0 : 1)
                    .ThenBy(x => x.PriceValue)
                    .ThenBy(x => x.ItemName)
                    .ToList()
            };

            return results;
        }


        // 
        private async Task<string> GetRestaurantNameAsync(
            string restaurantId,
            Dictionary<string, string> cache)
        {
            if (cache.TryGetValue(restaurantId, out string? cachedName))
            {
                return cachedName;
            }

            DocumentSnapshot restaurantDoc = await _db
                .Collection("restaurants")
                .Document(restaurantId)
                .GetSnapshotAsync();

            string restaurantName =
                restaurantDoc.Exists && restaurantDoc.ContainsField("name")
                    ? restaurantDoc.GetValue<string>("name")
                    : restaurantId;

            cache[restaurantId] = restaurantName;
            return restaurantName;
        }

        // 
        private static string? GetString(IDictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out object? value) || value is null)
            {
                return null;
            }

            return value.ToString();
        }

        // 
        private static decimal? GetDecimal(IDictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out object? value) || value is null)
            {
                return null;
            }

            return value switch
            {
                decimal d => d,
                double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
                float f => Convert.ToDecimal(f, CultureInfo.InvariantCulture),
                long l => l,
                int i => i,
                string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
                _ => null
            };
        }
    }
}
