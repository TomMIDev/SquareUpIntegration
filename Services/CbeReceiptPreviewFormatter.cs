using System.Globalization;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Services
{
    public class CbeReceiptPreviewFormatter
    {
        private const string PaymentType = "Square";

        public CbeReceiptPreviewResult Build(
            CbeTransactionRequest transaction)
        {
            ValidateTransaction(transaction);

            var previewLines =
                new List<CbeReceiptLinePreview>();

            foreach (var line in transaction.Lines)
            {
                ValidateLine(
                    transaction.SquareOrderId,
                    line);

                previewLines.Add(
                    new CbeReceiptLinePreview
                    {
                        ReceiptNo =
                            transaction.GlobalPurchaseNumber,

                        PLUID =
                            line.PLUID.Trim(),

                        ProductCode =
                            line.BOProductCode.ToString(
                                CultureInfo.InvariantCulture),

                        Quantity =
                            FormatValue(
                                line.Quantity),

                        Price =
                            FormatValue(
                                ToMajorUnits(
                                    line.UnitPriceAmountMinor!.Value)),

                        Discount =
                            FormatValue(
                                ToMajorUnits(
                                    line.DiscountAmountMinor)),

                        TotalPrice =
                            FormatValue(
                                ToMajorUnits(
                                    line.TotalAmountMinor!.Value)),

                        OrigPrice =
                            FormatValue(
                                ToMajorUnits(
                                    line.OriginalUnitPriceAmountMinor
                                    ?? line.UnitPriceAmountMinor.Value)),

                        LineTax =
                            FormatValue(
                                ToMajorUnits(
                                    line.TaxAmountMinor)),

                        VATPercent =
                            FormatValue(
                                line.VatPercent),

                        Status = 0
                    });
            }

            /*
                These calculations deliberately mirror the behaviour
                of the existing CBE Receipt model.

                SalesQty       = sum of quantities
                SalesValue     = sum of line totals
                AmountPaid     = sum of line totals
                OrderDiscounts = 0
                PreDiscount    = sum of line totals
                Shipping       = 0
            */

            var salesQty =
                transaction.Lines.Sum(
                    line => line.Quantity);

            var salesValue =
                transaction.Lines.Sum(
                    line =>
                        ToMajorUnits(
                            line.TotalAmountMinor!.Value));

            var receipt =
                new CbeReceiptPreview
                {
                    ReceiptNo =
                        transaction.GlobalPurchaseNumber,

                    DateTime =
                        transaction.TransactionDateTime.ToString(
                            "dd MMM yyyy HH:mm",
                            CultureInfo.InvariantCulture),

                    SalesQty =
                        FormatValue(
                            salesQty),

                    SalesValue =
                        FormatValue(
                            salesValue),

                    AmountPaid =
                        FormatValue(
                            salesValue),

                    OrderDiscounts =
                        FormatValue(0m),

                    PreDiscountValue =
                        FormatValue(
                            salesValue),

                    ShippingApplied =
                        FormatValue(0m),

                    ShippingAmount =
                        FormatValue(0m),

                    PaymentType =
                        PaymentType,

                    Status = 0
                };

            return new CbeReceiptPreviewResult
            {
                /*
                    BOStoreCode is routing information.

                    It is deliberately not part of #receipts because
                    the target store BO is selected separately.
                */
                BOStoreCode =
                    transaction.BOStoreCode,

                SquareLocationId =
                    transaction.SquareLocationId,

                Receipt =
                    receipt,

                ReceiptLines =
                    previewLines
            };
        }

        public void WriteToConsole(
            CbeReceiptPreviewResult preview)
        {
            ArgumentNullException.ThrowIfNull(preview);

            Console.WriteLine();
            Console.WriteLine(
                new string('=', 70));

            Console.WriteLine(
                "CBE RECEIPT PREVIEW - NO BO INJECTION");

            Console.WriteLine(
                new string('=', 70));

            Console.WriteLine(
                $"Target BO Store: {preview.BOStoreCode}");

            Console.WriteLine(
                $"Square Location: {preview.SquareLocationId}");

            Console.WriteLine();

            /*
                Match the column names used by #receipts.
            */
            Console.WriteLine("#receipts");
            Console.WriteLine(
                new string('-', 70));

            WriteValue(
                "ReceiptNo",
                preview.Receipt.ReceiptNo);

            WriteValue(
                "DateTime",
                preview.Receipt.DateTime);

            WriteValue(
                "SalesQty",
                preview.Receipt.SalesQty);

            WriteValue(
                "SalesValue",
                preview.Receipt.SalesValue);

            WriteValue(
                "AmountPaid",
                preview.Receipt.AmountPaid);

            WriteValue(
                "OrderDiscounts",
                preview.Receipt.OrderDiscounts);

            WriteValue(
                "PreDiscountValue",
                preview.Receipt.PreDiscountValue);

            WriteValue(
                "ShippingApplied",
                preview.Receipt.ShippingApplied);

            WriteValue(
                "ShippingAmount",
                preview.Receipt.ShippingAmount);

            WriteValue(
                "PaymentType",
                preview.Receipt.PaymentType);

            WriteValue(
                "Status",
                preview.Receipt.Status.ToString(
                    CultureInfo.InvariantCulture));

            Console.WriteLine();
            Console.WriteLine("#receiptLines");
            Console.WriteLine(
                new string('-', 70));

            var lineNumber = 0;

            foreach (var line in preview.ReceiptLines)
            {
                lineNumber++;

                Console.WriteLine(
                    $"Line {lineNumber}");

                WriteValue(
                    "ReceiptNo",
                    line.ReceiptNo);

                WriteValue(
                    "PLUID",
                    line.PLUID);

                WriteValue(
                    "ProductCode",
                    line.ProductCode);

                WriteValue(
                    "Quantity",
                    line.Quantity);

                WriteValue(
                    "Price",
                    line.Price);

                WriteValue(
                    "Discount",
                    line.Discount);

                WriteValue(
                    "TotalPrice",
                    line.TotalPrice);

                WriteValue(
                    "OrigPrice",
                    line.OrigPrice);

                WriteValue(
                    "LineTax",
                    line.LineTax);

                WriteValue(
                    "VATPercent",
                    line.VATPercent);

                WriteValue(
                    "Status",
                    line.Status.ToString(
                        CultureInfo.InvariantCulture));

                Console.WriteLine();
            }
        }

        private static void ValidateTransaction(
            CbeTransactionRequest transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            if (string.IsNullOrWhiteSpace(
                transaction.SquareOrderId))
            {
                throw new InvalidOperationException(
                    "Square Order ID must be supplied.");
            }

            /*
                #receipts.ReceiptNo is varchar(50).
            */
            if (transaction.SquareOrderId.Length > 50)
            {
                throw new InvalidOperationException(
                    $"Square Order ID '{transaction.SquareOrderId}' " +
                    "is longer than the 50-character BO ReceiptNo.");
            }

            if (transaction.BOStoreCode <= 0)
            {
                throw new InvalidOperationException(
                    $"Square order {transaction.SquareOrderId} " +
                    "does not have a valid BO store code.");
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
                    "does not contain any receipt lines.");
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

            /*
                #receiptLines.PLUID is CHAR(13).
            */
            if (line.PLUID.Trim().Length > 13)
            {
                throw new InvalidOperationException(
                    $"Square order {squareOrderId}, product " +
                    $"{line.BOProductCode}, has a PLUID longer " +
                    "than the BO 13-character limit.");
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

        private static string FormatValue(
            decimal value)
        {
            return value.ToString(
                CultureInfo.InvariantCulture);
        }

        private static void WriteValue(
            string name,
            string value)
        {
            Console.WriteLine(
                $"  {name,-20}: {value}");
        }
    }


    public class CbeReceiptPreviewResult
    {
        public int BOStoreCode { get; set; }

        public string SquareLocationId { get; set; } =
            string.Empty;

        public CbeReceiptPreview Receipt { get; set; } =
            new();

        public List<CbeReceiptLinePreview> ReceiptLines
        {
            get;
            set;
        } = [];
    }


    /*
        Mirrors the #receipts table.

        Values are strings because the actual CBE integration creates
        string DataColumns and ultimately bulk-copies them into varchar
        fields.
    */
    public class CbeReceiptPreview
    {
        public string ReceiptNo { get; set; } =
            string.Empty;

        public string DateTime { get; set; } =
            string.Empty;

        public string SalesQty { get; set; } =
            string.Empty;

        public string SalesValue { get; set; } =
            string.Empty;

        public string AmountPaid { get; set; } =
            string.Empty;

        public string OrderDiscounts { get; set; } =
            string.Empty;

        public string PreDiscountValue { get; set; } =
            string.Empty;

        public string ShippingApplied { get; set; } =
            string.Empty;

        public string ShippingAmount { get; set; } =
            string.Empty;

        public string PaymentType { get; set; } =
            string.Empty;

        public int Status { get; set; }
    }


    /*
        Mirrors the #receiptLines table.
    */
    public class CbeReceiptLinePreview
    {
        public string ReceiptNo { get; set; } =
            string.Empty;

        public string PLUID { get; set; } =
            string.Empty;

        public string ProductCode { get; set; } =
            string.Empty;

        public string Quantity { get; set; } =
            string.Empty;

        public string Price { get; set; } =
            string.Empty;

        public string Discount { get; set; } =
            string.Empty;

        public string TotalPrice { get; set; } =
            string.Empty;

        public string OrigPrice { get; set; } =
            string.Empty;

        public string LineTax { get; set; } =
            string.Empty;

        public string VATPercent { get; set; } =
            string.Empty;

        public int Status { get; set; }
    }
}