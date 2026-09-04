namespace SquareUpIntegration.Models
{
    public class CbeTransactionLine
    {
        public string PLUID { get; set; } = string.Empty;
        public int BOProductCode { get; set; }
        public decimal Quantity { get; set; }
        public long? UnitPriceAmountMinor { get; set; }
        public long? TotalAmountMinor { get; set; }
        public long DiscountAmountMinor { get; set; }
        public long? OriginalUnitPriceAmountMinor { get; set; }
        public long TaxAmountMinor { get; set; }
        public decimal VatPercent { get; set; }
    }
}
