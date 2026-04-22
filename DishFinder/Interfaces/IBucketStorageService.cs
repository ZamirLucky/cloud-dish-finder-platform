using DishFinder.Models;

namespace DishFinder.Interfaces
{
    public interface IBucketStorageService
    {
        Task<StorageUploadResultModel> UploadMenuImageAsync(
            IFormFile file,
            string restaurantId,
            string menuId);
    }
}
