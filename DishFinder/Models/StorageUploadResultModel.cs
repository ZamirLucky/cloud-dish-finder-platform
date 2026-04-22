namespace DishFinder.Models
{
    public class StorageUploadResultModel
    {
        public string BucketName { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string GsUri { get; set; } = string.Empty;
        public string PublicUrl { get; set; } = string.Empty;

    }
}
