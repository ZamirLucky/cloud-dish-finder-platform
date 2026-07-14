namespace DishFinder.Models
{
    public class MenuUploadMessageModel
    {
        public string RestaurantId { get; set; } = string.Empty;
        public string MenuId { get; set; } = string.Empty;
        public string ImageId { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string GsUri { get; set; } = string.Empty;
        public string UploadedBy { get; set; } = string.Empty;
    }
}
