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
        [Display(Name = "Menu images")]
        public List<IFormFile> MenuImages { get; set; } = new();

        public List<SelectListItem> Restaurants { get; set; } = new();

    }
}
