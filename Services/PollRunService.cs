using SquareUpIntegration.Models;
using SquareUpIntegration.Repositories;

namespace SquareUpIntegration.Services
{
    public class PollRunService
    {
        private readonly IPollRunRepository _repository;

        public PollRunService(
            IPollRunRepository repository)
        {
            _repository = repository
                ?? throw new ArgumentNullException(nameof(repository));
        }

        public Task<long> StartAsync(
            string squareLocationId,
            DateOnly collectionDate,
            CancellationToken cancellationToken = default)
        {
            return _repository.StartPollRunAsync(
                squareLocationId,
                collectionDate,
                cancellationToken);
        }

        public Task<PollRunRecord?> GetLastSuccessfulAsync(
            string squareLocationId,
            DateOnly collectionDate,
            CancellationToken cancellationToken = default)
        {
            return _repository.GetLastSuccessfulPollAsync(
                squareLocationId,
                collectionDate,
                cancellationToken);
        }

        public Task CompleteAsync(
            long pollRunId,
            int ordersFound,
            int ordersStaged,
            CancellationToken cancellationToken = default)
        {
            return _repository.CompletePollRunAsync(
                pollRunId,
                ordersFound,
                ordersStaged,
                cancellationToken);
        }

        public Task FailAsync(
            long pollRunId,
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(exception);

            return _repository.FailPollRunAsync(
                pollRunId,
                exception.Message,
                cancellationToken);
        }

        public Task FailAsync(
            long pollRunId,
            string errorMessage,
            CancellationToken cancellationToken = default)
        {
            return _repository.FailPollRunAsync(
                pollRunId,
                errorMessage,
                cancellationToken);
        }
    }
}
