namespace DishFinder.Models
{
    public class CatalogMenuItemViewModel
    {
        public string RestaurantId { get; set; } = string.Empty;
        public string RestaurantName { get; set; } = string.Empty;

        public string MenuId { get; set; } = string.Empty;
        public string? MenuTitle { get; set; }

        public string ItemName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Section { get; set; } = "UNCATEGORISED";

        public string PriceText { get; set; } = string.Empty;
        public decimal? PriceValue { get; set; }

        public string NormalizedName { get; set; } = string.Empty;

    }
}
