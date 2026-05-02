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

        public Task<string> AddImageReferenceAsync(string restaurantId, string menuId, StorageUploadResultModel uploadResult, string uploadedBy)
        {
            throw new NotImplementedException();
        }

        public Task CreateOrUpdateMenuAsync(
            string restaurantId, 
            string menuId, 
            string menuTitle, 
            string? ocrText = "", 
            string status = "pending")
        {
            throw new NotImplementedException();
        }

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
    }
}
