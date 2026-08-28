namespace SquareUpIntegration.Models
{
    public class CbeTransactionResult
    {
        public string SquareOrderId { get; set; } = string.Empty;

        public int StatusCode { get; set; }

        public string Status { get; set; } = string.Empty;

        public bool IsProcessed { get; set; }
    }
}
