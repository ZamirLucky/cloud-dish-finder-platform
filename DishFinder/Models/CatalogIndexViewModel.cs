namespace DishFinder.Models
{
    public class CatalogIndexViewModel
    {
        public string? SearchTerm { get; set; }
        public string SortOrder { get; set; } = "price_asc";
        public List<CatalogMenuItemViewModel> Items { get; set; } = new();
    }
}
