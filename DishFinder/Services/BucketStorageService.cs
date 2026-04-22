using DishFinder.Interfaces;
using DishFinder.Models;
using Google.Cloud.Storage.V1;

namespace DishFinder.Services
{
    public class BucketStorageService : IBucketStorageService
    {
        private readonly ILogger<BucketStorageService> _logger;
        private readonly string _bucketName;
        private readonly StorageClient _storageClient;

        public BucketStorageService(ILogger<BucketStorageService> logger, IConfiguration config)
        {
            _logger = logger;
            _bucketName = config["GoogleCloud:StorageBucketName"]
                ?? throw new InvalidOperationException("Missing GoogleCloud:StorageBucketName");

            _storageClient = StorageClient.Create();
        }

        public async Task<StorageUploadResultModel> UploadMenuImageAsync(
            IFormFile file,
            string restaurantId,
            string menuId)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentNullException(nameof(file), "File is null or empty.");
            }

            string[] permittedExtensions = { ".jpg", ".jpeg", ".png", ".gif" };
            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!permittedExtensions.Contains(extension))
            {
                throw new ArgumentException(
                    $"Invalid file extension. Allowed extensions: {string.Join(", ", permittedExtensions)}",
                    nameof(file));
            }

            string safeOriginalName = Path.GetFileName(file.FileName);
            string objectName =
                $"restaurants/{restaurantId}/menus/{menuId}/{Guid.NewGuid()}-{safeOriginalName}";

            string contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

            try
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                await _storageClient.UploadObjectAsync(
                    bucket: _bucketName,
                    objectName: objectName,
                    contentType: contentType,
                    source: memoryStream);

                _logger.LogInformation(
                    "Uploaded file {FileName} to bucket {BucketName} as object {ObjectName}",
                    safeOriginalName, _bucketName, objectName);

                return new StorageUploadResultModel
                {
                    BucketName = _bucketName,
                    ObjectName = objectName,
                    ContentType = contentType,
                    OriginalFileName = safeOriginalName,
                    GsUri = $"gs://{_bucketName}/{objectName}",
                    PublicUrl = $"https://storage.googleapis.com/{_bucketName}/{objectName}"
                };
            }
            catch (Google.GoogleApiException gae)
            {
                _logger.LogError($"Google API error while uploading file to Cloud Storage: {gae.Message}");
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError($"Unexpected error during file upload: {e.Message}");
                throw;
            }
        }
    }
}
