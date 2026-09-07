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
        private readonly TimeZoneInfo _ukTimeZone;

        public SquareOrderService(SquareClient client)
        {
            _client = client
                ?? throw new ArgumentNullException(nameof(client));

            _ukTimeZone = GetUkTimeZone();
        }

        /*
            First-poll / unrestricted version.

            This preserves the existing behaviour for a location which has
            never had a successful PollRun.
        */
        public Task<IReadOnlyList<Order>> GetOrdersAsync(
            IEnumerable<string> locationIds,
            CancellationToken cancellationToken = default)
        {
            return GetOrdersAsync(
                locationIds,
                updatedSinceUtc: null,
                cancellationToken);
        }

        /*
            Poll-aware version.

            When updatedSinceUtc is supplied, Square is asked only for
            orders whose UPDATED_AT timestamp is on or after that value.

            Square's UPDATED_AT filter is inclusive, so seeing the same
            order again at a poll boundary is safe. The staging layer
            replaces the existing Pending order data rather than creating
            a duplicate.

            For subsequent polling, pass the StartedAtUtc value from the
            previous successful PollRun rather than CompletedAtUtc. Using
            the previous poll start creates a deliberate overlap and avoids
            missing an amendment which occurred while that poll was running.
        */
        public async Task<IReadOnlyList<Order>> GetOrdersAsync(
            IEnumerable<string> locationIds,
            DateTimeOffset? updatedSinceUtc,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(locationIds);

            var locations = locationIds
                .Where(locationId =>
                    !string.IsNullOrWhiteSpace(locationId))
                .Distinct()
                .ToList();

            if (locations.Count == 0)
            {
                return [];
            }

            var orders = new List<Order>();

            /*
                SearchOrders accepts a maximum of 10 location IDs in one
                request.

                Program.cs currently polls one location at a time, but
                retaining batching keeps this service reusable.
            */
            foreach (var locationBatch in locations.Chunk(10))
            {
                string? cursor = null;

                var query =
                    CreateSearchQuery(updatedSinceUtc);

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var request = new SearchOrdersRequest
                    {
                        LocationIds = locationBatch,
                        Cursor = cursor,
                        Query = query,
                        Limit = 1000,
                        ReturnEntries = false
                    };

                    var response =
                        await _client.Orders.SearchAsync(
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

        public IReadOnlyList<Order> GetOrdersForCollectionDate(
            IEnumerable<Order> orders,
            DateOnly collectionDate)
        {
            ArgumentNullException.ThrowIfNull(orders);

            return orders
                .Where(order =>
                    HasPickupOnCollectionDate(
                        order,
                        collectionDate))
                .ToList();
        }

        /*
            First-poll convenience method.
        */
        public async Task<IReadOnlyList<Order>>
            GetOrdersForCollectionDateAsync(
                IEnumerable<string> locationIds,
                DateOnly collectionDate,
                CancellationToken cancellationToken = default)
        {
            var orders = await GetOrdersAsync(
                locationIds,
                cancellationToken);

            return GetOrdersForCollectionDate(
                orders,
                collectionDate);
        }

        /*
            Subsequent-poll convenience method.

            The API request is first restricted to new/changed orders,
            then the existing UK collection-date rule is applied locally.
        */
        public async Task<IReadOnlyList<Order>>
            GetOrdersForCollectionDateAsync(
                IEnumerable<string> locationIds,
                DateOnly collectionDate,
                DateTimeOffset updatedSinceUtc,
                CancellationToken cancellationToken = default)
        {
            var orders = await GetOrdersAsync(
                locationIds,
                updatedSinceUtc,
                cancellationToken);

            return GetOrdersForCollectionDate(
                orders,
                collectionDate);
        }

        private static SearchOrdersQuery? CreateSearchQuery(
            DateTimeOffset? updatedSinceUtc)
        {
            if (!updatedSinceUtc.HasValue)
            {
                return null;
            }

            var startAt =
                updatedSinceUtc.Value
                    .ToUniversalTime()
                    .ToString(
                        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                        CultureInfo.InvariantCulture);

            /*
                Square requires the SearchOrders sort field to match the
                timestamp used by DateTimeFilter.

                Because we filter on UpdatedAt, we must also sort on
                SearchOrdersSortField.UpdatedAt.
            */
            return new SearchOrdersQuery
            {
                Filter = new SearchOrdersFilter
                {
                    DateTimeFilter =
                        new SearchOrdersDateTimeFilter
                        {
                            UpdatedAt = new TimeRange
                            {
                                StartAt = startAt
                            }
                        }
                },

                Sort = new SearchOrdersSort
                {
                    SortField =
                        SearchOrdersSortField.UpdatedAt,

                    SortOrder =
                        SortOrder.Asc
                }
            };
        }

        private bool HasPickupOnCollectionDate(
            Order order,
            DateOnly collectionDate)
        {
            if (order.Fulfillments == null)
            {
                return false;
            }

            foreach (var fulfillment in order.Fulfillments)
            {
                /*
                    Only pickup fulfilments are relevant to this
                    integration.
                */
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

                /*
                    Convert Square's timestamp to UK local time before
                    deciding which collection date it belongs to.
                */
                var pickupAtUk =
                    TimeZoneInfo.ConvertTime(
                        pickupAt,
                        _ukTimeZone);

                var pickupDate =
                    DateOnly.FromDateTime(
                        pickupAtUk.DateTime);

                if (pickupDate == collectionDate)
                {
                    return true;
                }
            }

            return false;
        }

        private static TimeZoneInfo GetUkTimeZone()
        {
            try
            {
                // Windows
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "GMT Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                // Linux / macOS
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "Europe/London");
            }
        }
    }
}
