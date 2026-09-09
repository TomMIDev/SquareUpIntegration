using DataAccessUtility;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public class CbeTransactionRepository : ICbeTransactionRepository
    {
        private readonly IDataAccess _dataAccess;
        private readonly TimeZoneInfo _ukTimeZone;

        public CbeTransactionRepository(IDataAccess dataAccess)
        {
            _dataAccess = dataAccess
                ?? throw new ArgumentNullException(nameof(dataAccess));

            _ukTimeZone = GetUkTimeZone();
        }

        public async Task<IReadOnlyList<CbeTransactionRequest>>
            GetPendingTransactionsAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rows = (
            await _dataAccess.QueryAsync<PendingTransactionRow>(
                "Square.GetPendingTransactions",
                new Dictionary<string, object>()))
            .ToList();

            if (rows.Count == 0)
            {
                return [];
            }

            var transactions = new List<CbeTransactionRequest>();

            foreach (var orderGroup in rows.GroupBy(r => r.SquareOrderId))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var orderRows = orderGroup.ToList();
                var firstRow = orderRows[0];

                ValidateOrderRows(firstRow.SquareOrderId, orderRows);

                var pickupAtUk =
                    TimeZoneInfo.ConvertTime(
                        firstRow.PickupAtUtc,
                        _ukTimeZone);

                var transaction =
                    new CbeTransactionRequest
                    {
                        SquareOrderId = firstRow.SquareOrderId,
                        GlobalPurchaseNumber = firstRow.GlobalPurchaseNumber,
                        SquareLocationId = firstRow.SquareLocationId,
                        BOStoreCode = firstRow.BOStoreCode,
                        TransactionDateTime =
                            DateTime.SpecifyKind(
                                pickupAtUk.DateTime,
                                DateTimeKind.Unspecified)
                    };

                foreach (var row in orderRows)
                {
                    transaction.Lines.Add(
                        new CbeTransactionLine
                        {
                            PLUID = row.PLUID,
                            BOProductCode = row.BOProductCode,
                            Quantity = row.Quantity,
                            UnitPriceAmountMinor = row.BasePriceAmountMinor,
                            TotalAmountMinor = row.LineTotalAmountMinor,
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
            IReadOnlyList<PendingTransactionRow> rows)
        {
            if (rows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} has no transaction lines.");
            }

            var globalPurchaseNumbers =
                rows.Select(r => r.GlobalPurchaseNumber)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

            if (globalPurchaseNumbers.Count != 1 ||
                string.IsNullOrWhiteSpace(globalPurchaseNumbers[0]))
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} must have one valid Global Purchase Number.");
            }

            if (rows.Select(r => r.BOStoreCode).Distinct().Count() != 1)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} returned more than one BO store code.");
            }

            if (rows.Select(r => r.SquareLocationId)
                .Distinct(StringComparer.Ordinal).Count() != 1)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} returned more than one Square Location ID.");
            }

            if (rows.Select(r => r.PickupAtUtc).Distinct().Count() != 1)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} returned more than one pickup date/time.");
            }

            var currencies =
                rows.Select(row => row.LineCurrency)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (currencies.Count != 1 ||
                !string.Equals(
                    currencies[0],
                    "GBP",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} must contain one GBP currency.");
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

        private sealed class PendingTransactionRow
        {
            public string SquareOrderId { get; set; } = string.Empty;
            public string GlobalPurchaseNumber { get; set; } = string.Empty;
            public string SquareLocationId { get; set; } = string.Empty;
            public int BOStoreCode { get; set; }
            public DateTimeOffset PickupAtUtc { get; set; }
            public string LineUid { get; set; } = string.Empty;
            public int BOProductCode { get; set; }
            public string PLUID { get; set; } = string.Empty;
            public decimal Quantity { get; set; }
            public long BasePriceAmountMinor { get; set; }
            public long LineTotalAmountMinor { get; set; }
            public string LineCurrency { get; set; } = string.Empty;
        }
    }
}
