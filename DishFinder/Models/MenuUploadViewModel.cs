using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace DishFinder.Models
{
    public class MenuUploadViewModel
    {
        [Required]
        [Display(Name = "Restaurant")]
        public string RestaurantId { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Menu title")]
        public string MenuTitle { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Menu image")]
        public IFormFile? MenuImage { get; set; }

        public List<SelectListItem> Restaurants { get; set; } = new();

    }
}
