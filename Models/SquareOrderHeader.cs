namespace SquareUpIntegration.Models
{
    public class SquareOrderHeader
    {
        public string SquareOrderId { get; set; } = string.Empty;
        public string SquareLocationId { get; set; } = string.Empty;
        public string? CustomerId { get; set; }
        public string? OrderState { get; set; }
        public long? SquareVersion { get; set; }
        public DateTimeOffset? SquareCreatedAtUtc { get; set; }
        public DateTimeOffset? SquareUpdatedAtUtc { get; set; }
        public long? TotalAmountMinor { get; set; }
        public string? Currency { get; set; }
        public string? SourceName { get; set; }
    }
}
