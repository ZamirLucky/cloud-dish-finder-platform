using DishFinder.Models;

namespace DishFinder.Interfaces
{
    public interface IFirestoreMenuRepository
    {
        Task CreateOrUpdateRestaurantAsync(
            string restaurantId,
            string name,
            string address,
            string status = "pending");

        Task CreateOrUpdateMenuAsync(
            string restaurantId,
            string menuId,
            string menuTitle,
            string? ocrText = "",
            string status = "pending");

        Task<string> AddImageReferenceAsync(
            string restaurantId,
            string menuId,
            StorageUploadResultModel uploadResult,
            string uploadedBy);
    }
}
