using DataAccessUtility;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public class PollRunRepository : IPollRunRepository
    {
        private readonly IDataAccess _dataAccess;

        public PollRunRepository(IDataAccess dataAccess)
        {
            _dataAccess = dataAccess
                ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        public async Task<long> StartPollRunAsync(
            string squareLocationId,
            DateOnly collectionDate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValidateLocationId(squareLocationId);

            if (collectionDate == default)
            {
                throw new ArgumentException(
                    "Collection date must be supplied.",
                    nameof(collectionDate));
            }

            var parameters = new Dictionary<string, object>
            {
                ["@SquareLocationId"] = squareLocationId,
                ["@CollectionDate"] =
                    collectionDate.ToDateTime(TimeOnly.MinValue)
            };

            var rows = (
                await _dataAccess.QueryAsync<PollRunIdResult>(
                    "Square.StartPollRun",
                    parameters))
                .ToList();

            cancellationToken.ThrowIfCancellationRequested();

            if (rows.Count != 1 || rows[0].PollRunId <= 0)
            {
                throw new InvalidOperationException(
                    "Square.StartPollRun did not return a valid PollRunId.");
            }

            return rows[0].PollRunId;
        }

        public async Task CompletePollRunAsync(
            long pollRunId,
            int ordersFound,
            int ordersStaged,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValidatePollRunId(pollRunId);

            if (ordersFound < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ordersFound),
                    "Orders found cannot be negative.");
            }

            if (ordersStaged < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ordersStaged),
                    "Orders staged cannot be negative.");
            }

            if (ordersStaged > ordersFound)
            {
                throw new ArgumentException(
                    "Orders staged cannot exceed orders found.",
                    nameof(ordersStaged));
            }

            var parameters = new Dictionary<string, object>
            {
                ["@PollRunId"] = pollRunId,
                ["@OrdersFound"] = ordersFound,
                ["@OrdersStaged"] = ordersStaged
            };

            await _dataAccess.ExecuteAsync(
                "Square.CompletePollRun",
                parameters);
        }

        public async Task FailPollRunAsync(
            long pollRunId,
            string errorMessage,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValidatePollRunId(pollRunId);

            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage =
                    "The poll failed without a supplied error message.";
            }

            if (errorMessage.Length > 2000)
            {
                errorMessage = errorMessage[..2000];
            }

            var parameters = new Dictionary<string, object>
            {
                ["@PollRunId"] = pollRunId,
                ["@ErrorMessage"] = errorMessage
            };

            await _dataAccess.ExecuteAsync(
                "Square.FailPollRun",
                parameters);
        }

        public async Task<PollRunRecord?> GetLastSuccessfulPollAsync(
            string squareLocationId,
            DateOnly collectionDate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValidateLocationId(squareLocationId);

            if (collectionDate == default)
            {
                throw new ArgumentException(
                    "Collection date must be supplied.",
                    nameof(collectionDate));
            }

            var parameters = new Dictionary<string, object>
            {
                ["@SquareLocationId"] = squareLocationId,
                ["@CollectionDate"] =
                    collectionDate.ToDateTime(TimeOnly.MinValue)
            };

            var rows = (
                await _dataAccess.QueryAsync<PollRunRecord>(
                    "Square.GetLastSuccessfulPoll",
                    parameters))
                .ToList();

            cancellationToken.ThrowIfCancellationRequested();

            return rows.SingleOrDefault();
        }

        private static void ValidateLocationId(
            string squareLocationId)
        {
            if (string.IsNullOrWhiteSpace(squareLocationId))
            {
                throw new ArgumentException(
                    "Square Location ID must be supplied.",
                    nameof(squareLocationId));
            }
        }

        private static void ValidatePollRunId(
            long pollRunId)
        {
            if (pollRunId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pollRunId),
                    "Poll Run ID must be greater than zero.");
            }
        }

        private sealed class PollRunIdResult
        {
            public long PollRunId { get; set; }
        }
    }
}
