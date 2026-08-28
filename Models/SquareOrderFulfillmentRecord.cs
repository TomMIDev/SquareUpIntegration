namespace SquareUpIntegration.Models
{
    public class SquareOrderFulfillmentRecord
    {
        public string SquareOrderId { get; set; } = string.Empty;
        public string FulfillmentUid { get; set; } = string.Empty;
        public string? FulfillmentType { get; set; }
        public string? FulfillmentState { get; set; }
        public DateTimeOffset? PickupAtUtc { get; set; }
        public DateOnly? CollectionDate { get; set; }
        public string? ScheduleType { get; set; }
    }
}
