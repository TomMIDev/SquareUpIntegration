using System.Globalization;
using SquareUpIntegration.Models;
using SquareUpIntegration.Repositories;

namespace SquareUpIntegration.Services
{
    public class SquareOrderPersistenceService
    {
        private readonly ISquareOrderRepository _repository;
        private readonly TimeZoneInfo _ukTimeZone;

        public SquareOrderPersistenceService(
            ISquareOrderRepository repository)
        {
            _repository = repository
                ?? throw new ArgumentNullException(nameof(repository));

            _ukTimeZone = GetUkTimeZone();
        }

        public async Task<OrderPersistenceResult> SaveAsync(
            IEnumerable<global::Square.Order> orders,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(orders);

            var result = new OrderPersistenceResult();

            foreach (var order in orders)
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                var refreshToken = Guid.NewGuid();

                var orderHeader = new SquareOrderHeader
                {
                    SquareOrderId = order.Id,
                    SquareLocationId = order.LocationId,
                    CustomerId = order.CustomerId,
                    OrderState = order.State?.ToString(),
                    SquareVersion = order.Version,

                    // Square stores money in minor units.
                    // For GBP, 4000 represents £40.00.
                    TotalAmountMinor =
                        order.TotalMoney?.Amount,

                    Currency =
                        order.TotalMoney?
                            .Currency?
                            .ToString(),

                    SourceName =
                        order.Source?.Name
                };

                await _repository.BeginOrderRefreshAsync(
                    orderHeader,
                    refreshToken,
                    cancellationToken: cancellationToken);

                if (order.Fulfillments != null)
                {
                    var fulfillmentIndex = 0;

                    foreach (var fulfillment in order.Fulfillments)
                    {
                        fulfillmentIndex++;

                        DateTimeOffset? pickupAtUtc = null;
                        DateOnly? collectionDate = null;

                        var pickupAtText =
                            fulfillment.PickupDetails?.PickupAt;

                        if (TryParseSquareDateTime(
                            pickupAtText,
                            out var pickupAt))
                        {
                            pickupAtUtc = pickupAt.ToUniversalTime();

                            var pickupAtUk = TimeZoneInfo.ConvertTime(
                                pickupAt,
                                _ukTimeZone);

                            collectionDate = DateOnly.FromDateTime(
                                pickupAtUk.DateTime);
                        }

                        var fulfillmentUid =
                            string.IsNullOrWhiteSpace(fulfillment.Uid)
                                ? $"fulfillment-{fulfillmentIndex:D4}"
                                : fulfillment.Uid;

                        var fulfillmentRecord =
                            new SquareOrderFulfillmentRecord
                            {
                                SquareOrderId = order.Id,
                                FulfillmentUid = fulfillmentUid,
                                FulfillmentType =
                                    fulfillment.Type?.ToString(),
                                FulfillmentState =
                                    fulfillment.State?.ToString(),
                                PickupAtUtc = pickupAtUtc,
                                CollectionDate = collectionDate,
                                ScheduleType =
                                    fulfillment.PickupDetails?
                                        .ScheduleType?
                                        .ToString()
                            };

                        await _repository
                            .UpsertOrderFulfillmentAsync(
                                fulfillmentRecord,
                                refreshToken,
                                cancellationToken);

                        result.FulfillmentsSaved++;
                    }
                }

                if (order.LineItems != null)
                {
                    var lineIndex = 0;

                    foreach (var lineItem in order.LineItems)
                    {
                        lineIndex++;

                        if (!decimal.TryParse(
                            lineItem.Quantity,
                            NumberStyles.Number,
                            CultureInfo.InvariantCulture,
                            out var quantity)
                            || quantity <= 0)
                        {
                            throw new InvalidOperationException(
                                $"Square order {order.Id} contains line " +
                                $"{lineIndex} with invalid quantity " +
                                $"'{lineItem.Quantity}'.");
                        }

                        var lineUid =
                            string.IsNullOrWhiteSpace(lineItem.Uid)
                                ? $"line-{lineIndex:D4}"
                                : lineItem.Uid;

                        var orderLine = new SquareOrderLineRecord
                        {
                            SquareOrderId = order.Id,
                            LineUid = lineUid,
                            CatalogObjectId = lineItem.CatalogObjectId,
                            CatalogVersion = lineItem.CatalogVersion,
                            ItemName = lineItem.Name,
                            VariationName = lineItem.VariationName,
                            Quantity = quantity,

                            // Unit price in Square minor currency units.
                            BasePriceAmountMinor =
                                lineItem.BasePriceMoney?.Amount,

                            // Actual total value for this Square order line.
                            // This is required by the receipt/injection views.
                            TotalAmountMinor =
                                lineItem.TotalMoney?.Amount,

                            Currency =
                                lineItem.TotalMoney?
                                    .Currency?
                                    .ToString()
                                ??
                                lineItem.BasePriceMoney?
                                    .Currency?
                                    .ToString()
                        };

                        await _repository.UpsertOrderLineAsync(
                            orderLine,
                            refreshToken,
                            cancellationToken);

                        result.OrderLinesSaved++;
                    }
                }

                await _repository.CompleteOrderRefreshAsync(
                    order.Id,
                    refreshToken,
                    cancellationToken);

                result.OrdersSaved++;
            }

            return result;
        }

        private static bool TryParseSquareDateTime(
            string? value,
            out DateTimeOffset result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out result);
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
    }
}
