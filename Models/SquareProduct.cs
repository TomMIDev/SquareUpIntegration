namespace SquareUpIntegration.Models
{
    public class SquareProduct
    {
        public string ItemId { get; set; } = string.Empty;

        public string? ItemName { get; set; }

        public string VariationId { get; set; } = string.Empty;

        public string? VariationName { get; set; }

        public string? Sku { get; set; }

        public string? Upc { get; set; }

        public long? PriceAmount { get; set; }

        public string? Currency { get; set; }

        public long? CatalogVersion { get; set; }

        public bool IsActive { get; set; } = true;
    }
}