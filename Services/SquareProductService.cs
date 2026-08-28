using Square;
using Square.Catalog;
using SquareUpIntegration.Models;

namespace SquareUpIntegration.Services
{
    public class SquareProductService
    {
        private readonly SquareClient _client;

        public SquareProductService(SquareClient client)
        {
            _client = client
                ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<IReadOnlyList<SquareProduct>> GetProductsAsync(
            CancellationToken cancellationToken = default)
        {
            var products = new List<SquareProduct>();

            var pager = await _client.Catalog.ListAsync(
                new ListCatalogRequest
                {
                    Types = "ITEM"
                },
                cancellationToken: cancellationToken);

            await foreach (var catalogObject in pager)
            {
                if (!catalogObject.IsItem)
                {
                    continue;
                }

                var item = catalogObject.AsItem();
                var itemData = item.ItemData;

                if (itemData == null)
                {
                    continue;
                }

                if (itemData.Variations == null)
                {
                    continue;
                }

                foreach (var variationObject in itemData.Variations)
                {
                    if (!variationObject.IsItemVariation)
                    {
                        continue;
                    }

                    var variation = variationObject.AsItemVariation();
                    var variationData = variation.ItemVariationData;

                    if (variationData == null)
                    {
                        continue;
                    }

                    products.Add(
                        new SquareProduct
                        {
                            ItemId = item.Id,
                            ItemName = itemData.Name,
                            VariationId = variation.Id,
                            VariationName = variationData.Name,
                            Sku = variationData.Sku,
                            Upc = variationData.Upc,
                            PriceAmount = variationData.PriceMoney?.Amount,
                            Currency =
                                variationData.PriceMoney?
                                    .Currency?
                                    .ToString()
                        });
                }
            }

            return products;
        }
    }
}