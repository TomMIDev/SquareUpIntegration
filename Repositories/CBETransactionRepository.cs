using DataAccessUtility;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public class CbeTransactionRepository
        : ICbeTransactionRepository
    {
        private readonly IDataAccess _dataAccess;
        private readonly TimeZoneInfo _ukTimeZone;

        public CbeTransactionRepository(
            IDataAccess dataAccess)
        {
            _dataAccess = dataAccess
                ?? throw new ArgumentNullException(nameof(dataAccess));

            _ukTimeZone = GetUkTimeZone();
        }

        public async Task<IReadOnlyList<CbeTransactionRequest>>
            GetReadyTransactionsAsync(
                DateOnly asOfDate,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = new Dictionary<string, object>
            {
                ["@AsOfDate"] =
                    asOfDate.ToDateTime(TimeOnly.MinValue)
            };

            var rows = (
                await _dataAccess.QueryAsync<ReadyTransactionRow>(
                    "Square.GetOrdersReadyForInjection",
                    parameters))
                .ToList();

            cancellationToken.ThrowIfCancellationRequested();

            if (rows.Count == 0)
            {
                return [];
            }

            var transactions = new List<CbeTransactionRequest>();

            foreach (var orderGroup in rows.GroupBy(
                row => row.SquareOrderId))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var firstRow = orderGroup.First();

                ValidateOrderRows(
                    firstRow.SquareOrderId,
                    orderGroup);

                var pickupAtUk = TimeZoneInfo.ConvertTime(
                    firstRow.PickupAtUtc,
                    _ukTimeZone);

                var transactionDateTime =
                    DateTime.SpecifyKind(
                        pickupAtUk.DateTime,
                        DateTimeKind.Unspecified);

                var transaction =
                    new CbeTransactionRequest
                    {
                        SquareOrderId =
                            firstRow.SquareOrderId,

                        SquareLocationId =
                            firstRow.SquareLocationId,

                        BOStoreCode =
                            firstRow.BOStoreCode,

                        TransactionDateTime =
                            transactionDateTime
                    };

                foreach (var row in orderGroup)
                {
                    transaction.Lines.Add(
                        new CbeTransactionLine
                        {
                            BOProductCode =
                                row.BOProductCode,

                            Quantity =
                                row.Quantity,

                            UnitPriceAmountMinor =
                                row.BasePriceAmountMinor,

                            TotalAmountMinor =
                                row.LineTotalAmountMinor,

                            DiscountAmountMinor = 0,

                            OriginalUnitPriceAmountMinor =
                                row.BasePriceAmountMinor,

                            TaxAmountMinor = 0,
                            VatPercent = 0
                        });
                }

                transactions.Add(transaction);
            }

            return transactions;
        }

        private static void ValidateOrderRows(
            string squareOrderId,
            IEnumerable<ReadyTransactionRow> rows)
        {
            var rowList = rows.ToList();

            if (rowList.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} " +
                    "does not contain any lines.");
            }

            var storeCodes = rowList
                .Select(row => row.BOStoreCode)
                .Distinct()
                .ToList();

            if (storeCodes.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} " +
                    "returned more than one BO store code.");
            }

            var locationIds = rowList
                .Select(row => row.SquareLocationId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (locationIds.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} " +
                    "returned more than one Square Location ID.");
            }

            var pickupTimes = rowList
                .Select(row => row.PickupAtUtc)
                .Distinct()
                .ToList();

            if (pickupTimes.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} " +
                    "returned more than one pickup date/time.");
            }

            var currencies = rowList
                .Select(row => row.LineCurrency)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (currencies.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} " +
                    "contains multiple currencies.");
            }

            if (!string.Equals(
                currencies[0],
                "GBP",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} is not in GBP.");
            }
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

        public sealed class ReadyTransactionRow
        {
            public string SquareOrderId { get; set; } =
                string.Empty;

            public string SquareLocationId { get; set; } =
                string.Empty;

            public int BOStoreCode { get; set; }

            public DateTimeOffset PickupAtUtc { get; set; }

            public string LineUid { get; set; } =
                string.Empty;

            public int BOProductCode { get; set; }

            public decimal Quantity { get; set; }

            public long BasePriceAmountMinor { get; set; }

            public long LineTotalAmountMinor { get; set; }

            public string LineCurrency { get; set; } =
                string.Empty;
        }
    }
}
