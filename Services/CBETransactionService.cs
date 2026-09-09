using System.Globalization;
using CBETransactionIntegration.Models;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Services
{
    public class CBETransactionService
    {
        private const string PaymentType = "Square";

        public Receipt BuildReceipt(
            CbeTransactionRequest transaction)
        {
            ValidateTransaction(transaction);

            var receipt = new Receipt(PaymentType)
            {
                ReceiptNo = transaction.GlobalPurchaseNumber,
                DateTime = transaction.TransactionDateTime,
                Status = 0
            };

            foreach (var line in transaction.Lines)
            {
                ValidateLine(
                    transaction.SquareOrderId,
                    line);

                receipt.ReceiptLines.Add(
                    BuildReceiptLine(line));
            }

            return receipt;
        }

        private static ReceiptLine BuildReceiptLine(
            CbeTransactionLine line)
        {
            return new ReceiptLine
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
                    "does not contain any lines to build a receipt.");
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
