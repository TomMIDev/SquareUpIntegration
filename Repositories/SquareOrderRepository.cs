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

        public async Task BeginOrderRefreshAsync(
            SquareOrderHeader order,
            Guid refreshToken,
            long? pollRunId = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(order);
            cancellationToken.ThrowIfCancellationRequested();

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
                ["@PollRunId"] = DbValue(pollRunId),
                ["@RefreshToken"] = refreshToken
            };

            await _dataAccess.ExecuteAsync(
                "Square.BeginOrderRefresh",
                parameters);
        }

        public async Task UpsertOrderFulfillmentAsync(
            SquareOrderFulfillmentRecord fulfillment,
            Guid refreshToken,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fulfillment);
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = new Dictionary<string, object>
            {
                ["@SquareOrderId"] = fulfillment.SquareOrderId,
                ["@FulfillmentUid"] = fulfillment.FulfillmentUid,
                ["@FulfillmentType"] = DbValue(fulfillment.FulfillmentType),
                ["@FulfillmentState"] = DbValue(fulfillment.FulfillmentState),
                ["@PickupAtUtc"] = DbValue(fulfillment.PickupAtUtc),
                ["@CollectionDate"] = DbValue(
                    fulfillment.CollectionDate?.ToDateTime(TimeOnly.MinValue)),
                ["@ScheduleType"] = DbValue(fulfillment.ScheduleType),
                ["@RefreshToken"] = refreshToken
            };

            await _dataAccess.ExecuteAsync(
                "Square.UpsertOrderFulfillment",
                parameters);
        }

        public async Task UpsertOrderLineAsync(
            SquareOrderLineRecord orderLine,
            Guid refreshToken,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(orderLine);
            cancellationToken.ThrowIfCancellationRequested();

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
                ["@ItemType"] = DbValue(orderLine.ItemType),
                ["@RefreshToken"] = refreshToken
            };

            await _dataAccess.ExecuteAsync(
                "Square.UpsertOrderLine",
                parameters);
        }

        public async Task CompleteOrderRefreshAsync(
            string squareOrderId,
            Guid refreshToken,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(squareOrderId))
            {
                throw new ArgumentException(
                    "Square Order ID must be supplied.",
                    nameof(squareOrderId));
            }

            var parameters = new Dictionary<string, object>
            {
                ["@SquareOrderId"] = squareOrderId,
                ["@RefreshToken"] = refreshToken
            };

            await _dataAccess.ExecuteAsync(
                "Square.CompleteOrderRefresh",
                parameters);
        }

        private static object DbValue(object? value)
        {
            return value ?? DBNull.Value;
        }
    }
}
