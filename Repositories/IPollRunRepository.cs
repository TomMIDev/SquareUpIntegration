using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public interface IPollRunRepository
    {
        Task<long> StartPollRunAsync(
            string squareLocationId,
            DateOnly collectionDate,
            CancellationToken cancellationToken = default);

        Task CompletePollRunAsync(
            long pollRunId,
            int ordersFound,
            int ordersStaged,
            CancellationToken cancellationToken = default);

        Task FailPollRunAsync(
            long pollRunId,
            string errorMessage,
            CancellationToken cancellationToken = default);

        Task<PollRunRecord?> GetLastSuccessfulPollAsync(
            string squareLocationId,
            DateOnly collectionDate,
            CancellationToken cancellationToken = default);
    }
}
