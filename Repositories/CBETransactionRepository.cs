using DataAccessUtility;
using Microsoft.Data.SqlClient;
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
                await _dataAccess.QueryAsync(
                    "Square.GetPendingTransactions",
                    MapPendingTransactionRow,
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

        private static PendingTransactionRow MapPendingTransactionRow(
            SqlDataReader reader)
        {
            return new PendingTransactionRow
            {
                SquareOrderId =
                    GetRequiredString(reader, "SquareOrderId"),

                SquareLocationId =
                    GetRequiredString(reader, "SquareLocationId"),

                BOStoreCode =
                    GetRequiredInt32(reader, "BOStoreCode"),

                PickupAtUtc =
                    GetRequiredDateTimeOffset(reader, "PickupAtUtc"),

                LineUid =
                    GetRequiredString(reader, "LineUid"),

                BOProductCode =
                    GetRequiredInt32(reader, "BOProductCode"),

                PLUID =
                    GetRequiredValueAsString(reader, "PLUID"),

                Quantity =
                    GetRequiredDecimal(reader, "Quantity"),

                BasePriceAmountMinor =
                    GetRequiredInt64(reader, "BasePriceAmountMinor"),

                LineTotalAmountMinor =
                    GetRequiredInt64(reader, "LineTotalAmountMinor"),

                Currency =
                    GetRequiredString(reader, "LineCurrency")
            };
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

            var currencies = rows.Select(r => r.Currency)
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

        private static string GetRequiredString(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
            {
                throw new InvalidOperationException(
                    $"Required database column {columnName} was NULL.");
            }

            return reader.GetString(ordinal);
        }

        private static string GetRequiredValueAsString(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
            {
                throw new InvalidOperationException(
                    $"Required database column {columnName} was NULL.");
            }

            return Convert.ToString(
                       reader.GetValue(ordinal),
                       System.Globalization.CultureInfo.InvariantCulture)
                   ?? throw new InvalidOperationException(
                       $"Required database column {columnName} could not be converted.");
        }

        private static int GetRequiredInt32(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
            {
                throw new InvalidOperationException(
                    $"Required database column {columnName} was NULL.");
            }

            return reader.GetInt32(ordinal);
        }

        private static long GetRequiredInt64(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
            {
                throw new InvalidOperationException(
                    $"Required database column {columnName} was NULL.");
            }

            return reader.GetInt64(ordinal);
        }

        private static decimal GetRequiredDecimal(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
            {
                throw new InvalidOperationException(
                    $"Required database column {columnName} was NULL.");
            }

            return reader.GetDecimal(ordinal);
        }

        private static DateTimeOffset GetRequiredDateTimeOffset(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
            {
                throw new InvalidOperationException(
                    $"Required database column {columnName} was NULL.");
            }

            return reader.GetDateTimeOffset(ordinal);
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
            public string SquareLocationId { get; set; } = string.Empty;
            public int BOStoreCode { get; set; }
            public DateTimeOffset PickupAtUtc { get; set; }
            public string LineUid { get; set; } = string.Empty;
            public int BOProductCode { get; set; }
            public string PLUID { get; set; } = string.Empty;
            public decimal Quantity { get; set; }
            public long BasePriceAmountMinor { get; set; }
            public long LineTotalAmountMinor { get; set; }
            public string Currency { get; set; } = string.Empty;
        }
    }
}
