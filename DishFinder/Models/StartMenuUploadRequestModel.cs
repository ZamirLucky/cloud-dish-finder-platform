using System.ComponentModel.DataAnnotations;

namespace DishFinder.Models
{
    public class StartMenuUploadRequestModel
    {
        [Required]
        public string RestaurantId { get; set; } = string.Empty;

        [Required]
        public string MenuTitle { get; set; } = string.Empty;
    }
}
