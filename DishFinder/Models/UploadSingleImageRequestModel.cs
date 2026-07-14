using System.ComponentModel.DataAnnotations;

namespace DishFinder.Models
{
    public class UploadSingleImageRequestModel
    {
        [Required]
        public string RestaurantId { get; set; } = string.Empty;

        [Required]
        public string MenuId { get; set; } = string.Empty;

        [Required]
        public IFormFile? File { get; set; }
    }
}
