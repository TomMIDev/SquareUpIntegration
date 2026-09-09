namespace SquareUpIntegration.Models
{
    public class CbeTransactionRequest
    {
        public string SquareOrderId { get; set; } = string.Empty;

        // Square location that the order belongs to.
        public string SquareLocationId { get; set; } = string.Empty;

        // Back Office store code derived from the Square location mapping.
        public int BOStoreCode { get; set; }

        // Pickup / transaction date and time used by CBE.
        public DateTime TransactionDateTime { get; set; }

        public List<CbeTransactionLine> Lines { get; set; } = [];

        public string GlobalPurchaseNumber { get; set; } = string.Empty;
    }
}