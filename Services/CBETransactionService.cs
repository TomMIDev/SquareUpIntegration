using System.Globalization;
using CBETransactionIntegration;
using CBETransactionIntegration.Models;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Services
{
    public class CBETransactionService
    {
        private const string PaymentType = "Square";

        public async Task<CbeTransactionResult> PushAsync(
            string backOfficeConnectionString,
            CbeTransactionRequest transaction,
            bool processImmediately = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            var results = await PushBatchAsync(
                backOfficeConnectionString,
                new[] { transaction },
                processImmediately,
                cancellationToken);

            return results.Single();
        }

        public async Task<IReadOnlyList<CbeTransactionResult>> PushBatchAsync(
            string backOfficeConnectionString,
            IEnumerable<CbeTransactionRequest> transactions,
            bool processImmediately = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(backOfficeConnectionString))
            {
                throw new ArgumentException(
                    "Back Office connection string must be supplied.",
                    nameof(backOfficeConnectionString));
            }

            ArgumentNullException.ThrowIfNull(transactions);

            var transactionList = transactions.ToList();

            if (transactionList.Count == 0)
            {
                return [];
            }

            var receipts = new List<Receipt>();

            foreach (var transaction in transactionList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                receipts.Add(BuildReceipt(transaction));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var statuses = await Receipts.Push(
                backOfficeConnectionString,
                receipts,
                autoProcess: false);

            var results = new List<CbeTransactionResult>();

            foreach (var transaction in transactionList)
            {
                var receiptStatus = statuses.SingleOrDefault(status =>
                    string.Equals(
                        status.ReceiptNo,
                        transaction.SquareOrderId,
                        StringComparison.Ordinal));

                if (receiptStatus == null)
                {
                    throw new InvalidOperationException(
                        "CBETransactionIntegration did not return a status " +
                        $"for Square order {transaction.SquareOrderId}.");
                }

                results.Add(
                    new CbeTransactionResult
                    {
                        SquareOrderId = transaction.SquareOrderId,
                        StatusCode = (int)receiptStatus.Status,
                        Status = receiptStatus.Status.ToString(),
                        IsProcessed =
                            receiptStatus.Status == Statuses.Processed
                    });
            }

            var acceptedReceiptCount = results.Count(result =>
                result.IsProcessed);

            if (processImmediately && acceptedReceiptCount > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Receipts.Process(
                    backOfficeConnectionString,
                    acceptedReceiptCount);
            }

            return results;
        }

        public Receipt BuildReceipt(
            CbeTransactionRequest transaction)
        {
            ValidateTransaction(transaction);

            var receipt = new Receipt(PaymentType)
            {
                ReceiptNo = transaction.SquareOrderId,
                DateTime = transaction.TransactionDateTime,
                Status = 0
            };

            foreach (var line in transaction.Lines)
            {
                ValidateLine(
                    transaction.SquareOrderId,
                    line);

                var receiptLine = new ReceiptLine
                {
                    PLUID = line.PLUID,

                    ProductCode =
                        line.BOProductCode.ToString(
                            CultureInfo.InvariantCulture),

                    Quantity = line.Quantity,

                    Price = ToMajorUnits(
                        line.UnitPriceAmountMinor!.Value),

                    Discount = ToMajorUnits(
                        line.DiscountAmountMinor),

                    TotalPrice = ToMajorUnits(
                        line.TotalAmountMinor!.Value),

                    OrigPrice = ToMajorUnits(
                        line.OriginalUnitPriceAmountMinor
                        ?? line.UnitPriceAmountMinor.Value),

                    LineTax = ToMajorUnits(
                        line.TaxAmountMinor),

                    VATPercent = line.VatPercent,

                    Status = 0
                };

                receipt.ReceiptLines.Add(receiptLine);
            }

            return receipt;
        }

        private static void ValidateTransaction(
            CbeTransactionRequest transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            if (string.IsNullOrWhiteSpace(transaction.SquareOrderId))
            {
                throw new InvalidOperationException(
                    "Square Order ID must be supplied.");
            }

            if (transaction.SquareOrderId.Length > 50)
            {
                throw new InvalidOperationException(
                    $"Square Order ID '{transaction.SquareOrderId}' " +
                    "is longer than the 50-character ReceiptNo supported by CBE.");
            }

            if (transaction.TransactionDateTime == default)
            {
                throw new InvalidOperationException(
                    $"Square order {transaction.SquareOrderId} " +
                    "does not have a valid transaction date/time.");
            }

            if (transaction.Lines == null ||
                transaction.Lines.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Square order {transaction.SquareOrderId} " +
                    "does not contain any lines to inject.");
            }
        }

        private static void ValidateLine(
            string squareOrderId,
            CbeTransactionLine line)
        {
            ArgumentNullException.ThrowIfNull(line);

            if (string.IsNullOrWhiteSpace(line.PLUID))
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId}, product " +
                    $"{line.BOProductCode}, does not have a PLUID.");
            }

            if (line.PLUID.Length > 13)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId}, product " +
                    $"{line.BOProductCode}, has PLUID '{line.PLUID}' " +
                    "which is longer than 13 characters.");
            }

            if (line.BOProductCode <= 0)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId} contains a line " +
                    "without a valid BO product code.");
            }

            if (line.Quantity <= 0)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId}, product " +
                    $"{line.BOProductCode}, has invalid quantity.");
            }

            if (!line.UnitPriceAmountMinor.HasValue)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId}, product " +
                    $"{line.BOProductCode}, does not have a unit price.");
            }

            if (!line.TotalAmountMinor.HasValue)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId}, product " +
                    $"{line.BOProductCode}, does not have a line total.");
            }

            if (line.VatPercent < 0 ||
                line.VatPercent > 100)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId}, product " +
                    $"{line.BOProductCode}, has an invalid VAT percentage.");
            }
        }

        private static decimal ToMajorUnits(
            long amountMinor)
        {
            return amountMinor / 100m;
        }
    }
}
