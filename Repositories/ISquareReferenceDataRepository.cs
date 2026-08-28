using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public interface ISquareReferenceDataRepository
    {
        Task UpsertLocationAsync(
            string squareLocationId,
            string? locationName,
            bool isActive,
            CancellationToken cancellationToken = default);

        Task UpsertCatalogVariationAsync(
            SquareProduct product,
            CancellationToken cancellationToken = default);

        Task SetProductMappingAsync(
        string squareVariationId,
        int boProductCode,
        bool isActive = true,
        CancellationToken cancellationToken = default);
    }
}