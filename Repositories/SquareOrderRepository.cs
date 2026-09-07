using DataAccessUtility;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public class SquareOrderRepository : ISquareOrderRepository
    {
        private readonly IDataAccess _dataAccess;

        public SquareOrderRepository(IDataAccess dataAccess)
        {
            _dataAccess = dataAccess
                ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        public async Task UpsertOrderAsync(
            SquareOrderHeader order,
            long pollRunId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(order);
            cancellationToken.ThrowIfCancellationRequested();

            if (pollRunId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pollRunId),
                    "Poll Run ID must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(order.SquareOrderId))
            {
                throw new ArgumentException(
                    "Square Order ID must be supplied.",
                    nameof(order));
            }

            if (string.IsNullOrWhiteSpace(order.SquareLocationId))
            {
                throw new ArgumentException(
                    "Square Location ID must be supplied.",
                    nameof(order));
            }

            var parameters = new Dictionary<string, object>
            {
                ["@SquareOrderId"] = order.SquareOrderId,
                ["@SquareLocationId"] = order.SquareLocationId,
                ["@CustomerId"] = DbValue(order.CustomerId),
                ["@OrderState"] = DbValue(order.OrderState),
                ["@SquareVersion"] = DbValue(order.SquareVersion),
                ["@SquareCreatedAtUtc"] = DbValue(order.SquareCreatedAtUtc),
                ["@SquareUpdatedAtUtc"] = DbValue(order.SquareUpdatedAtUtc),
                ["@TotalAmountMinor"] = DbValue(order.TotalAmountMinor),
                ["@Currency"] = DbValue(order.Currency),
                ["@SourceName"] = DbValue(order.SourceName),
                ["@PickupAtUtc"] = DbValue(order.PickupAtUtc),
                ["@CollectionDate"] = DbValue(
                    order.CollectionDate?
                        .ToDateTime(TimeOnly.MinValue)),
                ["@PollRunId"] = pollRunId
            };

            await _dataAccess.ExecuteAsync(
                "Square.UpsertOrder",
                parameters);
        }

        public async Task DeleteOrderLinesAsync(
            string squareOrderId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValidateSquareOrderId(squareOrderId);

            var parameters = new Dictionary<string, object>
            {
                ["@SquareOrderId"] = squareOrderId
            };

            await _dataAccess.ExecuteAsync(
                "Square.DeleteOrderLines",
                parameters);
        }

        public async Task InsertOrderLineAsync(
            SquareOrderLineRecord orderLine,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(orderLine);
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(orderLine.SquareOrderId))
            {
                throw new ArgumentException(
                    "Square Order ID must be supplied.",
                    nameof(orderLine));
            }

            if (string.IsNullOrWhiteSpace(orderLine.LineUid))
            {
                throw new ArgumentException(
                    "Square order line UID must be supplied.",
                    nameof(orderLine));
            }

            if (orderLine.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(orderLine),
                    "Order line quantity must be greater than zero.");
            }

            var parameters = new Dictionary<string, object>
            {
                ["@SquareOrderId"] = orderLine.SquareOrderId,
                ["@LineUid"] = orderLine.LineUid,
                ["@CatalogObjectId"] = DbValue(orderLine.CatalogObjectId),
                ["@CatalogVersion"] = DbValue(orderLine.CatalogVersion),
                ["@ItemName"] = DbValue(orderLine.ItemName),
                ["@VariationName"] = DbValue(orderLine.VariationName),
                ["@Quantity"] = orderLine.Quantity,
                ["@BasePriceAmountMinor"] = DbValue(orderLine.BasePriceAmountMinor),
                ["@TotalAmountMinor"] = DbValue(orderLine.TotalAmountMinor),
                ["@Currency"] = DbValue(orderLine.Currency),
                ["@ItemType"] = DbValue(orderLine.ItemType)
            };

            await _dataAccess.ExecuteAsync(
                "Square.InsertOrderLine",
                parameters);
        }

        public async Task CompleteOrderRefreshAsync(
            string squareOrderId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValidateSquareOrderId(squareOrderId);

            var parameters = new Dictionary<string, object>
            {
                ["@SquareOrderId"] = squareOrderId
            };

            await _dataAccess.ExecuteAsync(
                "Square.CompleteOrderRefresh",
                parameters);
        }

        private static void ValidateSquareOrderId(
            string squareOrderId)
        {
            if (string.IsNullOrWhiteSpace(squareOrderId))
            {
                throw new ArgumentException(
                    "Square Order ID must be supplied.",
                    nameof(squareOrderId));
            }
        }

        private static object DbValue(object? value)
        {
            return value ?? DBNull.Value;
        }
    }
}
