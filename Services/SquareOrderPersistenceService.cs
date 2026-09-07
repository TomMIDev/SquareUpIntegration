using System.Globalization;
using SquareUpIntegration.Models;
using SquareUpIntegration.Repositories;

namespace SquareUpIntegration.Services
{
    public class SquareOrderPersistenceService
    {
        private readonly ISquareOrderRepository _repository;

        public SquareOrderPersistenceService(
            ISquareOrderRepository repository)
        {
            _repository = repository
                ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<OrderPersistenceResult> SaveAsync(
            IEnumerable<global::Square.Order> orders,
            long pollRunId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(orders);

            if (pollRunId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pollRunId),
                    "Poll Run ID must be greater than zero.");
            }

            var result = new OrderPersistenceResult();

            foreach (var order in orders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ValidateOrder(order);

                var orderHeader = CreateOrderHeader(order);

                /*
                    Build and validate the complete current Square line set
                    before changing any existing staged lines.
                */
                var orderLines = CreateOrderLines(order);

                if (orderLines.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Square order {orderHeader.SquareOrderId} " +
                        "does not contain any order lines.");
                }

                /*
                    UpsertOrder sets IsRefreshComplete = 0 and records
                    the PollRunId which supplied this order version.
                */
                await _repository.UpsertOrderAsync(
                    orderHeader,
                    pollRunId,
                    cancellationToken);

                /*
                    Square is the source of truth for the current line set.
                    Replacing all staged lines handles amended, added and
                    removed lines.
                */
                await _repository.DeleteOrderLinesAsync(
                    orderHeader.SquareOrderId,
                    cancellationToken);

                foreach (var orderLine in orderLines)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await _repository.InsertOrderLineAsync(
                        orderLine,
                        cancellationToken);

                    result.OrderLinesSaved++;
                }

                /*
                    Only mark the order complete after every current
                    Square line has been stored successfully.
                */
                await _repository.CompleteOrderRefreshAsync(
                    orderHeader.SquareOrderId,
                    cancellationToken);

                result.OrdersSaved++;
            }

            return result;
        }

        private static SquareOrderHeader CreateOrderHeader(
            global::Square.Order order)
        {
            var pickupInformation = GetPickupInformation(order);

            return new SquareOrderHeader
            {
                SquareOrderId = order.Id!,
                SquareLocationId = order.LocationId!,
                CustomerId = order.CustomerId,
                OrderState = order.State?.ToString(),
                SquareVersion = order.Version,
                TotalAmountMinor = order.TotalMoney?.Amount,
                Currency = order.TotalMoney?.Currency?.ToString(),
                SourceName = order.Source?.Name,
                PickupAtUtc = pickupInformation.PickupAtUtc,
                CollectionDate = pickupInformation.CollectionDate
            };
        }

        private static (
            DateTimeOffset? PickupAtUtc,
            DateOnly? CollectionDate)
            GetPickupInformation(global::Square.Order order)
        {
            if (order.Fulfillments == null)
            {
                return (null, null);
            }

            foreach (var fulfillment in order.Fulfillments)
            {
                if (!string.Equals(
                    fulfillment.Type?.ToString(),
                    "PICKUP",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pickupAtText =
                    fulfillment.PickupDetails?.PickupAt;

                if (string.IsNullOrWhiteSpace(pickupAtText))
                {
                    continue;
                }

                if (!DateTimeOffset.TryParse(
                    pickupAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var pickupAt))
                {
                    continue;
                }

                var pickupAtUtc =
                    pickupAt.ToUniversalTime();

                var pickupAtUk =
                    TimeZoneInfo.ConvertTime(
                        pickupAtUtc,
                        GetUkTimeZone());

                return
                (
                    pickupAtUtc,
                    DateOnly.FromDateTime(
                        pickupAtUk.DateTime)
                );
            }

            return (null, null);
        }

        private static TimeZoneInfo GetUkTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "GMT Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "Europe/London");
            }
        }

        private static List<SquareOrderLineRecord> CreateOrderLines(
            global::Square.Order order)
        {
            var orderLines = new List<SquareOrderLineRecord>();

            if (order.LineItems == null)
            {
                return orderLines;
            }

            var lineIndex = 0;

            foreach (var lineItem in order.LineItems)
            {
                lineIndex++;

                orderLines.Add(
                    CreateOrderLine(
                        order.Id!,
                        lineItem,
                        lineIndex));
            }

            return orderLines;
        }

        private static SquareOrderLineRecord CreateOrderLine(
            string squareOrderId,
            global::Square.OrderLineItem lineItem,
            int lineIndex)
        {
            if (!decimal.TryParse(
                lineItem.Quantity,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var quantity)
                || quantity <= 0)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} contains line " +
                    $"{lineIndex} with invalid quantity " +
                    $"'{lineItem.Quantity}'.");
            }

            var lineUid =
                string.IsNullOrWhiteSpace(lineItem.Uid)
                    ? $"line-{lineIndex:D4}"
                    : lineItem.Uid;

            return new SquareOrderLineRecord
            {
                SquareOrderId = squareOrderId,
                LineUid = lineUid,
                CatalogObjectId = lineItem.CatalogObjectId,
                CatalogVersion = lineItem.CatalogVersion,
                ItemName = lineItem.Name,
                VariationName = lineItem.VariationName,
                Quantity = quantity,

                BasePriceAmountMinor =
                    lineItem.BasePriceMoney?.Amount,

                TotalAmountMinor =
                    lineItem.TotalMoney?.Amount,

                Currency =
                    lineItem.TotalMoney?.Currency?.ToString()
                    ??
                    lineItem.BasePriceMoney?.Currency?.ToString()
            };
        }

        private static void ValidateOrder(
            global::Square.Order order)
        {
            if (string.IsNullOrWhiteSpace(order.Id))
            {
                throw new InvalidOperationException(
                    "Square returned an order without an Order ID.");
            }

            if (string.IsNullOrWhiteSpace(order.LocationId))
            {
                throw new InvalidOperationException(
                    $"Square order {order.Id} does not contain a Location ID.");
            }
        }
    }
}
