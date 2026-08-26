using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Square;
using Square.Orders;

namespace SquareUpIntegration.Services
{
    public class SquareOrderService
    {
        private readonly SquareClient _client;

        public SquareOrderService(SquareClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<IReadOnlyList<Order>> GetOrdersAsync(
            IEnumerable<string> locationIds,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(locationIds);

            var locations = locationIds
                .Where(locationId => !string.IsNullOrWhiteSpace(locationId))
                .Distinct()
                .ToList();

            if (locations.Count == 0)
            {
                return [];
            }

            var orders = new List<Order>();

            // Square SearchOrders accepts a maximum of 10 location IDs
            // in a single request.
            foreach (var locationBatch in locations.Chunk(10))
            {
                string? cursor = null;

                do
                {
                    var request = new SearchOrdersRequest
                    {
                        LocationIds = locationBatch,
                        Cursor = cursor,
                        Limit = 1000,
                        ReturnEntries = false
                    };

                    var response = await _client.Orders.SearchAsync(
                        request,
                        cancellationToken: cancellationToken);

                    if (response.Orders != null)
                    {
                        orders.AddRange(response.Orders);
                    }

                    cursor = response.Cursor;

                } while (!string.IsNullOrWhiteSpace(cursor));
            }

            return orders;
        }

        public async Task<IReadOnlyList<Order>> GetOrdersForCollectionDateAsync(
            IEnumerable<string> locationIds,
            DateOnly collectionDate,
            CancellationToken cancellationToken = default)
        {
            var orders = await GetOrdersAsync(
                locationIds,
                cancellationToken);

            return orders
                .Where(order => HasPickupOnCollectionDate(order, collectionDate))
                .ToList();
        }

        private static bool HasPickupOnCollectionDate(
            Order order,
            DateOnly collectionDate)
        {
            if (order.Fulfillments == null)
            {
                return false;
            }

            foreach (var fulfillment in order.Fulfillments)
            {
                var pickupAtText = fulfillment.PickupDetails?.PickupAt;

                if (string.IsNullOrWhiteSpace(pickupAtText))
                {
                    continue;
                }

                if (!DateTimeOffset.TryParse(
                    pickupAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var pickupAt))
                {
                    continue;
                }

                var pickupDate = DateOnly.FromDateTime(pickupAt.DateTime);

                if (pickupDate == collectionDate)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
