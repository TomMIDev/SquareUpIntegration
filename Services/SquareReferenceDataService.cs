using Square;
using SquareUpIntegration.Models;
using SquareUpIntegration.Repositories;

namespace SquareUpIntegration.Services
{
    public class SquareReferenceDataService
    {
        private readonly ISquareReferenceDataRepository
            _referenceDataRepository;

        public SquareReferenceDataService(
            ISquareReferenceDataRepository referenceDataRepository)
        {
            _referenceDataRepository = referenceDataRepository
                ?? throw new ArgumentNullException(
                    nameof(referenceDataRepository));
        }

        public async Task<ReferenceDataSaveResult> SaveAsync(
            IEnumerable<Location> locations,
            IEnumerable<SquareProduct> products,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(locations);
            ArgumentNullException.ThrowIfNull(products);

            var result = new ReferenceDataSaveResult();

            foreach (var location in locations)
            {
                if (string.IsNullOrWhiteSpace(location.Id))
                {
                    continue;
                }

                var isActive =
                    !string.Equals(
                        location.Status?.ToString(),
                        "INACTIVE",
                        StringComparison.OrdinalIgnoreCase);

                await _referenceDataRepository.UpsertLocationAsync(
                    location.Id,
                    location.Name,
                    isActive,
                    cancellationToken);

                result.LocationsSaved++;
            }

            foreach (var product in products)
            {
                if (string.IsNullOrWhiteSpace(product.VariationId))
                {
                    continue;
                }

                await _referenceDataRepository
                    .UpsertCatalogVariationAsync(
                        product,
                        cancellationToken);

                result.CatalogVariationsSaved++;
            }

            return result;
        }
    }
}