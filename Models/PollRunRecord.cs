namespace SquareUpIntegration.Models
{
    public class PollRunRecord
    {
        public long PollRunId { get; set; }

        public string SquareLocationId { get; set; } = string.Empty;

        public DateOnly CollectionDate { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public string PollStatus { get; set; } = string.Empty;

        public int OrdersFound { get; set; }

        public int OrdersStaged { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
