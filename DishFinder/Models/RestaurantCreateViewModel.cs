using System.ComponentModel.DataAnnotations;

namespace DishFinder.Models
{
    public class RestaurantCreateViewModel
    {
        [Required]
        [StringLength(100)]
        [Display(Name = "Restaurant name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        [Display(Name = "Address")]
        public string Address { get; set; } = string.Empty;
    }
}
