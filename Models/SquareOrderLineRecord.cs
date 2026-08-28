namespace SquareUpIntegration.Models
{
    public class SquareOrderLineRecord
    {
        public string SquareOrderId { get; set; } = string.Empty;
        public string LineUid { get; set; } = string.Empty;
        public string? CatalogObjectId { get; set; }
        public long? CatalogVersion { get; set; }
        public string? ItemName { get; set; }
        public string? VariationName { get; set; }
        public decimal Quantity { get; set; }
        public long? BasePriceAmountMinor { get; set; }
        public long? TotalAmountMinor { get; set; }
        public string? Currency { get; set; }
        public string? ItemType { get; set; }
    }
}
