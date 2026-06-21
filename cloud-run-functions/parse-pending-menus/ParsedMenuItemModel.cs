namespace HelloHttp{

    /// <summary>
    /// Structured menu item produced by the parser and later written to Firestore for catalog search and sorting.
    /// </summary>
    public sealed class ParsedMenuItemModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? PriceText { get; set; }
        // public int? PriceCents { get; set; }
        public double? PriceValue { get; set; }
        public string? Section { get; set; }
        public string NormalizedName { get; set; } = string.Empty;
    }
}