using DataAccessUtility;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public class SquareReferenceDataRepository
        : ISquareReferenceDataRepository
    {
        private readonly IDataAccess _dataAccess;

        public SquareReferenceDataRepository(
            IDataAccess dataAccess)
        {
            _dataAccess = dataAccess
                ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        public async Task UpsertLocationAsync(
            string squareLocationId,
            string? locationName,
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(squareLocationId))
            {
                throw new ArgumentException(
                    "Square Location ID must be supplied.",
                    nameof(squareLocationId));
            }

            var parameters = new Dictionary<string, object>
            {
                ["@SquareLocationId"] = squareLocationId,
                ["@LocationName"] = DbValue(locationName),
                ["@IsActive"] = isActive
            };

            await _dataAccess.ExecuteAsync(
                "Square.UpsertLocation",
                parameters);
        }

        public async Task UpsertCatalogVariationAsync(
            SquareProduct product,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(product);

            if (string.IsNullOrWhiteSpace(product.VariationId))
            {
                throw new ArgumentException(
                    "Square Variation ID must be supplied.",
                    nameof(product));
            }

            if (string.IsNullOrWhiteSpace(product.ItemId))
            {
                throw new ArgumentException(
                    "Square Item ID must be supplied.",
                    nameof(product));
            }

            var parameters = new Dictionary<string, object>
            {
                ["@SquareVariationId"] = product.VariationId,
                ["@SquareItemId"] = product.ItemId,
                ["@ItemName"] = DbValue(product.ItemName),
                ["@VariationName"] = DbValue(product.VariationName),
                ["@SKU"] = DbValue(product.Sku),
                ["@UPC"] = DbValue(product.Upc),
                ["@PriceAmountMinor"] = DbValue(product.PriceAmount),
                ["@Currency"] = DbValue(product.Currency),
                ["@CatalogVersion"] = DbValue(product.CatalogVersion),
                ["@IsActive"] = product.IsActive
            };

            await _dataAccess.ExecuteAsync(
                "Square.UpsertCatalogVariation",
                parameters);
        }

        public async Task SetProductMappingAsync(
        string squareVariationId,
        int boProductCode,
        bool isActive = true,
        CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(squareVariationId))
            {
                throw new ArgumentException(
                    "Square Variation ID must be supplied.",
                    nameof(squareVariationId));
            }

            var parameters = new Dictionary<string, object>
            {
                ["@SquareVariationId"] = squareVariationId,
                ["@BOProductCode"] = boProductCode,
                ["@IsActive"] = isActive
            };

            await _dataAccess.ExecuteAsync(
                "Square.SetProductMapping",
                parameters);
        }

        private static object DbValue(object? value)
        {
            return value ?? DBNull.Value;
        }
    }
}