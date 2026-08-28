namespace SquareUpIntegration.Models
{
    public class CbeTransactionLine
    {
        // BO product code obtained from Square.ProductMapping.
        public int BOProductCode { get; set; }

        public decimal Quantity { get; set; }

        // Square money values are stored in minor units:
        // 4000 GBP = £40.00.
        public long? UnitPriceAmountMinor { get; set; }
        public long? TotalAmountMinor { get; set; }

        // These are included because CBETransactionIntegration expects them.
        // Populate them from Square before production injection.
        public long DiscountAmountMinor { get; set; }
        public long? OriginalUnitPriceAmountMinor { get; set; }
        public long TaxAmountMinor { get; set; }
        public decimal VatPercent { get; set; }
    }
}
